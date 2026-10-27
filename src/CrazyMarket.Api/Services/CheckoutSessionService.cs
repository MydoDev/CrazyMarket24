using CrazyMarket.Api.Abstractions;
using CrazyMarket.Api.Contracts;
using CrazyMarket.Api.Data;
using CrazyMarket.Api.Domain.Entities;
using CrazyMarket.Api.Domain.Enums;
using CrazyMarket.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CrazyMarket.Api.Services;

public record CheckoutActionResult(
    bool Success,
    int HttpStatusCode,
    string? ErrorCode,
    string Message,
    CheckoutStateDto State,
    CompletedOrderDto? CompletedOrder = null);

/// <summary>
/// Produkční stavový automat samoobslužné pokladny (Self-Checkout).
/// Zajišťuje dynamickou identifikaci zákazníka (QR TOTP nebo Auto-assign při 1 osobě v prodejně),
/// kontrolu plnoletosti 18+, verifikaci hmotnosti na RS232 váze po každém naskenovaném kusu,
/// uplatnění věrnostních bodů, platbu terminálem, export POHODA XML a odemknutí dveří pro odchod.
/// </summary>
public sealed class CheckoutSessionService : IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TotpAccessService _totpService;
    private readonly PricingService _pricingService;
    private readonly IScaleService _scaleService;
    private readonly IPaymentTerminal _paymentTerminal;
    private readonly IPohodaClient _pohodaClient;
    private readonly IDoorController _doorController;
    private readonly IHubContext<StoreHub, IStoreHubClient> _hub;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CheckoutSessionService> _logger;

    private readonly SemaphoreSlim _mutex = new(1, 1);

    private Guid _checkoutSessionId = Guid.NewGuid();
    private CheckoutStatus _status = CheckoutStatus.Idle;
    private Guid? _customerId;
    private string? _customerName;
    private bool _customerIsAdult;
    private int _customerAvailablePoints;
    private Guid? _storeSessionId;
    private readonly List<CartLine> _items = [];
    private int _redeemedPoints;
    private string _statusMessage = "Připraveno. Pípněte zboží nebo svůj zákaznický QR kód.";

    public CheckoutSessionService(
        IServiceScopeFactory scopeFactory,
        TotpAccessService totpService,
        PricingService pricingService,
        IScaleService scaleService,
        IPaymentTerminal paymentTerminal,
        IPohodaClient pohodaClient,
        IDoorController doorController,
        IHubContext<StoreHub, IStoreHubClient> hub,
        TimeProvider timeProvider,
        ILogger<CheckoutSessionService> logger)
    {
        _scopeFactory = scopeFactory;
        _totpService = totpService;
        _pricingService = pricingService;
        _scaleService = scaleService;
        _paymentTerminal = paymentTerminal;
        _pohodaClient = pohodaClient;
        _doorController = doorController;
        _hub = hub;
        _timeProvider = timeProvider;
        _logger = logger;

        _scaleService.OnWeightStabilized += HandleScaleWeightStabilizedAsync;
    }

    public async Task<CheckoutStateDto> GetStateAsync(CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            return BuildStateSnapshot();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<CheckoutStateDto> ResetSessionAsync(CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            ResetInternal();
            var state = BuildStateSnapshot();
            await _hub.Clients.All.CheckoutStateChanged(state);
            return state;
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <summary>
    /// Globální vstup ze čtečky na pokladně (<c>POST /api/kiosk/scan</c>).
    /// Rozpozná zákaznický QR kód (<c>CM24:...</c>) vs. EAN čárový kód zboží.
    /// </summary>
    public async Task<CheckoutActionResult> ProcessScanAsync(string? scannedCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(scannedCode))
        {
            var emptyState = await GetStateAsync(ct);
            return await FailAsync("EMPTY_SCAN", "Naskenovaný kód je prázdný.", StatusCodes.Status400BadRequest, emptyState);
        }

        var code = scannedCode.Trim();

        await _mutex.WaitAsync(ct);
        try
        {
            if (_status == CheckoutStatus.Paying)
            {
                return await FailAsync(
                    "PAYMENT_IN_PROGRESS",
                    "Právě probíhá platba na terminálu. Skenování není povoleno.",
                    StatusCodes.Status409Conflict,
                    BuildStateSnapshot());
            }

            // A. Skenování Zákaznického QR kódu (Identifikace: CM24:CUSTOMER:{Id}:{TotpCode})
            if (code.StartsWith(TotpAccessService.Prefix + ":", StringComparison.OrdinalIgnoreCase))
            {
                return await HandleCustomerQrScanInternalAsync(code, ct);
            }

            // B. Skenování EAN kódu zboží
            return await HandleEanScanInternalAsync(code, ct);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <summary>Uplatnění věrnostních bodů zákazníka (<c>POST /api/kiosk/apply-points</c>).</summary>
    public async Task<CheckoutActionResult> ApplyPointsAsync(int? requestedPoints, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            if (_customerId is null)
            {
                return await FailAsync(
                    "NO_CUSTOMER",
                    "Nejprve se identifikujte nebo naskenujte první položku.",
                    StatusCodes.Status400BadRequest,
                    BuildStateSnapshot());
            }

            if (_items.Count == 0)
            {
                return await FailAsync(
                    "EMPTY_CART",
                    "Košík je prázdný, nelze uplatnit věrnostní body.",
                    StatusCodes.Status400BadRequest,
                    BuildStateSnapshot());
            }

            var subtotal = _items.Sum(i => i.UnitPrice * i.Quantity);
            var maxApplicable = (int)Math.Min(_customerAvailablePoints, Math.Floor(subtotal));

            int pointsToUse;
            if (requestedPoints is null)
            {
                pointsToUse = maxApplicable;
            }
            else if (requestedPoints.Value < 0)
            {
                return await FailAsync(
                    "INVALID_POINTS",
                    "Počet bodů k uplatnění nesmí být záporný.",
                    StatusCodes.Status400BadRequest,
                    BuildStateSnapshot());
            }
            else if (requestedPoints.Value > _customerAvailablePoints)
            {
                return await FailAsync(
                    "INSUFFICIENT_POINTS",
                    $"Zákazník má pouze {_customerAvailablePoints} bodů (požadováno {requestedPoints.Value}).",
                    StatusCodes.Status400BadRequest,
                    BuildStateSnapshot());
            }
            else
            {
                pointsToUse = Math.Min(requestedPoints.Value, maxApplicable);
            }

            _redeemedPoints = pointsToUse;
            _statusMessage = _redeemedPoints > 0
                ? $"Uplatněno {_redeemedPoints} věrnostních bodů (sleva {_redeemedPoints} Kč)."
                : "Věrnostní body nejsou uplatněny.";

            var state = BuildStateSnapshot();
            await _hub.Clients.All.CheckoutStateChanged(state);

            return new CheckoutActionResult(true, StatusCodes.Status200OK, null, _statusMessage, state);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <summary>
    /// Zahájení a dokončení platby (<c>POST /api/kiosk/pay</c>):
    /// kontrola váhy -> platební terminál -> uložení Order -> odečet skladu -> připsání bodů -> POHODA XML -> odemknutí dveří.
    /// </summary>
    public async Task<CheckoutActionResult> PayAsync(CancellationToken ct = default)
    {
        Guid orderId;
        Guid customerId;
        string customerName;
        Guid? storeSessionId;
        decimal subtotal;
        int redeemedPoints;
        decimal finalAmountToPay;
        int earnedPoints;
        int expectedWeight;
        int currentScaleWeight;
        bool isNightPricing;
        List<CartLine> cartCopy;

        await _mutex.WaitAsync(ct);
        try
        {
            if (_customerId is null)
            {
                return await FailAsync(
                    "NO_CUSTOMER",
                    "Na pokladně není přihlášen žádný zákazník.",
                    StatusCodes.Status400BadRequest,
                    BuildStateSnapshot());
            }

            if (_items.Count == 0)
            {
                return await FailAsync(
                    "EMPTY_CART",
                    "Košík je prázdný.",
                    StatusCodes.Status400BadRequest,
                    BuildStateSnapshot());
            }

            // Zkontrolujeme aktuální váhu ještě těsně před platbou
            EvaluateWeightVerificationInternal(_scaleService.CurrentWeightGrams);

            if (_status == CheckoutStatus.WaitingForWeightVerification)
            {
                var exp = _items.Sum(i => i.UnitWeightGrams * i.Quantity);
                var tol = _items.Sum(i => i.UnitToleranceGrams * i.Quantity);
                var cur = _scaleService.CurrentWeightGrams;

                return await FailAsync(
                    "WEIGHT_NOT_VERIFIED",
                    $"Nelze zaplatit – hmotnost na váze ({cur} g) neodpovídá košíku ({exp} g ± {tol} g). Položte prosím zboží na váhu.",
                    StatusCodes.Status409Conflict,
                    BuildStateSnapshot());
            }

            if (_status == CheckoutStatus.Paying)
            {
                return await FailAsync(
                    "PAYMENT_ALREADY_IN_PROGRESS",
                    "Platba již probíhá.",
                    StatusCodes.Status409Conflict,
                    BuildStateSnapshot());
            }

            orderId = Guid.NewGuid();
            customerId = _customerId.Value;
            customerName = _customerName ?? "Zákazník";
            storeSessionId = _storeSessionId;
            subtotal = _items.Sum(i => i.UnitPrice * i.Quantity);
            redeemedPoints = Math.Min(_redeemedPoints, (int)Math.Floor(subtotal));
            finalAmountToPay = Math.Max(0m, subtotal - redeemedPoints);
            earnedPoints = _items.Sum(i => i.BonusPointsPerUnit * i.Quantity);
            expectedWeight = _items.Sum(i => i.UnitWeightGrams * i.Quantity);
            currentScaleWeight = _scaleService.CurrentWeightGrams;
            isNightPricing = _pricingService.IsNightTime();
            cartCopy = _items.Select(i => i with { }).ToList();

            _status = CheckoutStatus.Paying;
            _statusMessage = $"Čekám na platbu {finalAmountToPay:0.00} Kč na platebním terminálu...";
            await _hub.Clients.All.CheckoutStateChanged(BuildStateSnapshot());
        }
        finally
        {
            _mutex.Release();
        }

        // Volání platebního terminálu probíhá mimo hlavní zámek pokladny, aby bylo možné číst stav
        var terminalResult = await _paymentTerminal.ProcessPaymentAsync(finalAmountToPay, orderId, ct);

        await _mutex.WaitAsync(ct);
        try
        {
            if (!terminalResult.Approved)
            {
                _status = CheckoutStatus.Ready;
                _statusMessage = $"Platba zamítnuta: {terminalResult.Message} Zkuste to prosím znovu.";
                var declinedState = BuildStateSnapshot();
                await _hub.Clients.All.CheckoutStateChanged(declinedState);

                return await FailAsync("PAYMENT_DECLINED", _statusMessage, StatusCodes.Status402PaymentRequired, declinedState);
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CrazyMarketDbContext>();
            var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

            var customer = await db.Customers.FirstAsync(c => c.Id == customerId, ct);
            var productIds = cartCopy.Select(i => i.ProductId).ToList();
            var productsById = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

            // Odečet skladu
            foreach (var item in cartCopy)
            {
                if (productsById.TryGetValue(item.ProductId, out var prod))
                {
                    prod.StockCount = Math.Max(0, prod.StockCount - item.Quantity);
                }
            }

            // Aktualizace věrnostních bodů zákazníka
            customer.LoyaltyPoints = Math.Max(0, customer.LoyaltyPoints - redeemedPoints) + earnedPoints;

            // Uzavření StoreSession zákazníka
            StoreSession? session = null;
            if (storeSessionId is not null)
            {
                session = await db.StoreSessions.FirstOrDefaultAsync(s => s.Id == storeSessionId.Value, ct);
            }
            session ??= await db.StoreSessions
                .Where(s => s.CustomerId == customerId && s.ExitedAt == null)
                .OrderByDescending(s => s.EnteredAt)
                .FirstOrDefaultAsync(ct);

            if (session is not null)
            {
                session.CurrentState = SessionState.Paid;
                session.ExitedAt = utcNow;
            }

            var order = new Order
            {
                Id = orderId,
                CustomerId = customer.Id,
                Customer = customer,
                StoreSessionId = session?.Id,
                StoreSession = session,
                CreatedAt = utcNow,
                PaidAt = utcNow,
                TotalAmount = finalAmountToPay,
                IsNightPricing = isNightPricing,
                PaymentStatus = PaymentStatus.Approved,
                PaymentReference = terminalResult.TransactionReference,
                WeightValidation = WeightValidationStatus.Passed,
                ExpectedWeightGrams = expectedWeight,
                MeasuredWeightGrams = currentScaleWeight,
                Items = cartCopy.Select(i => new OrderItem
                {
                    ProductId = i.ProductId,
                    Product = productsById[i.ProductId],
                    Ean = i.Ean,
                    ProductName = i.Name,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    LineTotal = i.UnitPrice * i.Quantity,
                    UnitWeightGrams = i.UnitWeightGrams,
                    UnitToleranceGrams = i.UnitToleranceGrams
                }).ToList()
            };

            db.Orders.Add(order);

            // Export do POHODY (Windows-1250 XML)
            var pohodaExport = await _pohodaClient.ExportOrderToPohodaXmlAsync(order, ct);
            order.PohodaXmlExported = true;
            order.PohodaExportedAt = utcNow;

            await db.SaveChangesAsync(ct);

            // Odemknutí dveří pro odchod + rozloučení
            var goodbye = $"Děkujeme za nákup, {customer.FullName}! Získáváte {earnedPoints} bodů. Dveře jsou odemčeny.";
            await _doorController.UnlockDoorAsync(durationSeconds: 8, ct);
            await _doorController.PlayWelcomeAudioAsync(goodbye, ct);

            var completedDto = new CompletedOrderDto(
                order.Id,
                customer.Id,
                customer.FullName,
                subtotal,
                redeemedPoints,
                finalAmountToPay,
                earnedPoints,
                customer.LoyaltyPoints,
                order.PaymentReference,
                pohodaExport.FilePath,
                utcNow);

            _status = CheckoutStatus.Completed;
            _customerAvailablePoints = customer.LoyaltyPoints;
            _statusMessage = goodbye;
            var completedState = BuildStateSnapshot();

            await _hub.Clients.All.OrderCompleted(completedDto);
            await _hub.Clients.All.CheckoutStateChanged(completedState);

            // Připravíme pokladnu na další nákup
            ResetInternal();

            return new CheckoutActionResult(true, StatusCodes.Status200OK, null, goodbye, completedState, completedDto);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<CheckoutActionResult> HandleCustomerQrScanInternalAsync(string rawQr, CancellationToken ct)
    {
        if (!TotpAccessService.TryParseEntryQr(rawQr, out var parsed, out var parseError) || parsed is null)
        {
            return await FailAsync("INVALID_QR", parseError ?? "Neplatný QR kód.", StatusCodes.Status400BadRequest, BuildStateSnapshot());
        }

        if (parsed.ActorType != AccessActorType.Customer)
        {
            return await FailAsync(
                "SUPPLIER_QR_NOT_ALLOWED_ON_KIOSK",
                "Na samoobslužné pokladně se může přihlásit pouze zákazník (nikoliv dodavatel).",
                StatusCodes.Status400BadRequest,
                BuildStateSnapshot());
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CrazyMarketDbContext>();
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().Date);

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == parsed.ActorId, ct);
        if (customer is null)
        {
            return await FailAsync("CUSTOMER_NOT_FOUND", "Zákazník nebyl nalezen.", StatusCodes.Status404NotFound, BuildStateSnapshot());
        }

        if (customer.IsBanned)
        {
            return await FailAsync("CUSTOMER_BANNED", "Zákazník má zablokovaný účet.", StatusCodes.Status403Forbidden, BuildStateSnapshot());
        }

        if (!_totpService.VerifyCode(customer.TotpSecret, parsed.TotpCode))
        {
            return await FailAsync(
                "INVALID_TOTP",
                "Neplatný nebo expirovaný TOTP kód zákazníka.",
                StatusCodes.Status401Unauthorized,
                BuildStateSnapshot());
        }

        // Pokud na pokladně ještě nebyl přihlášen tento zákazník a košík je z předchozího pokusu, vyčistíme košík
        if (_customerId is not null && _customerId != customer.Id)
        {
            _items.Clear();
            _redeemedPoints = 0;
        }

        var activeSession = await db.StoreSessions
            .Where(s => s.CustomerId == customer.Id && s.ExitedAt == null)
            .OrderByDescending(s => s.EnteredAt)
            .FirstOrDefaultAsync(ct);

        if (activeSession is not null)
        {
            activeSession.CurrentState = SessionState.AtCheckout;
            await db.SaveChangesAsync(ct);
        }

        var isAdult = customer.IsAdult || customer.IsAdultOn(today);
        _customerId = customer.Id;
        _customerName = customer.FullName;
        _customerIsAdult = isAdult;
        _customerAvailablePoints = customer.LoyaltyPoints;
        _storeSessionId = activeSession?.Id;

        if (_status == CheckoutStatus.Idle)
            _status = CheckoutStatus.Ready;

        _statusMessage = $"Přihlášen zákazník: {customer.FullName} ({customer.LoyaltyPoints} bodů). Naskenujte zboží.";

        var identifiedEvt = new KioskCustomerIdentifiedEvent(
            customer.Id, customer.FullName, isAdult, customer.LoyaltyPoints, AutoAssigned: false);
        await _hub.Clients.All.KioskCustomerIdentified(identifiedEvt);

        var state = BuildStateSnapshot();
        await _hub.Clients.All.CheckoutStateChanged(state);

        _logger.LogInformation("Pokladna: identifikován zákazník přes QR TOTP: {Name} ({Id}), body={Points}.",
            customer.FullName, customer.Id, customer.LoyaltyPoints);

        return new CheckoutActionResult(true, StatusCodes.Status200OK, null, _statusMessage, state);
    }

    private async Task<CheckoutActionResult> HandleEanScanInternalAsync(string ean, CancellationToken ct)
    {
        // Dokud předchozí položka nebyla zvážena (WaitingForWeightVerification), zablokujeme další skenování!
        EvaluateWeightVerificationInternal(_scaleService.CurrentWeightGrams);
        if (_status == CheckoutStatus.WaitingForWeightVerification)
        {
            var exp = _items.Sum(i => i.UnitWeightGrams * i.Quantity);
            var tol = _items.Sum(i => i.UnitToleranceGrams * i.Quantity);
            var cur = _scaleService.CurrentWeightGrams;

            return await FailAsync(
                "WAITING_FOR_WEIGHT",
                $"Nejprve položte předchozí zboží na váhu! Očekáváno {exp} g (±{tol} g), na váze je {cur} g.",
                StatusCodes.Status409Conflict,
                BuildStateSnapshot());
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CrazyMarketDbContext>();
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().Date);

        // 1. Ověření zákazníka (Auto-assign pokud je v prodejně 1 osoba, jinak výzva k pípnutí QR)
        if (_customerId is null)
        {
            var activeCustomerSessions = await db.StoreSessions
                .Include(s => s.Customer)
                .Where(s => s.ExitedAt == null && s.CustomerId != null)
                .ToListAsync(ct);

            // Počet unikátních zákazníků aktuálně v prodejně
            var distinctCustomers = activeCustomerSessions
                .Where(s => s.Customer is not null)
                .GroupBy(s => s.CustomerId!.Value)
                .Select(g => g.OrderByDescending(x => x.EnteredAt).First())
                .ToList();

            if (distinctCustomers.Count > 1)
            {
                const string multiMsg = "V prodejně je více osob. Pro zahájení nákupu prosím pípněte svůj QR kód z mobilu.";
                return await FailAsync("MULTIPLE_CUSTOMERS_IN_STORE", multiMsg, StatusCodes.Status409Conflict, BuildStateSnapshot());
            }

            if (distinctCustomers.Count == 0)
            {
                const string noneMsg = "V prodejně není evidován žádný zákazník. Pro zahájení nákupu prosím pípněte svůj QR kód z mobilu.";
                return await FailAsync("NO_ACTIVE_CUSTOMER_IN_STORE", noneMsg, StatusCodes.Status409Conflict, BuildStateSnapshot());
            }

            // Počet == 1 -> Automatické přiřazení zákazníka (Auto-assign)
            var singleSession = distinctCustomers[0];
            var autoCustomer = singleSession.Customer!;
            var isAdult = autoCustomer.IsAdult || autoCustomer.IsAdultOn(today);

            singleSession.CurrentState = SessionState.AtCheckout;
            await db.SaveChangesAsync(ct);

            _customerId = autoCustomer.Id;
            _customerName = autoCustomer.FullName;
            _customerIsAdult = isAdult;
            _customerAvailablePoints = autoCustomer.LoyaltyPoints;
            _storeSessionId = singleSession.Id;

            _logger.LogInformation("Pokladna: Auto-assign zákazníka {Name} ({Id}) – je sám v prodejně.",
                autoCustomer.FullName, autoCustomer.Id);

            await _hub.Clients.All.KioskCustomerIdentified(new KioskCustomerIdentifiedEvent(
                autoCustomer.Id, autoCustomer.FullName, isAdult, autoCustomer.LoyaltyPoints, AutoAssigned: true));
        }

        // Vyhledání produktu podle EAN
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Ean == ean, ct);
        if (product is null)
        {
            return await FailAsync(
                "PRODUCT_NOT_FOUND",
                $"Produkt s čárovým kódem '{ean}' nebyl v databázi nalezen.",
                StatusCodes.Status404NotFound,
                BuildStateSnapshot());
        }

        // 2. Ověření věku (18+)
        if (product.IsAgeRestricted && !_customerIsAdult)
        {
            return await FailAsync(
                "AGE_RESTRICTED",
                $"Prodej položky '{product.Name}' je povolen pouze osobám starším 18 let. Položka byla zamítnuta.",
                StatusCodes.Status403Forbidden,
                BuildStateSnapshot());
        }

        // Kontrola dostupného skladu
        var existingLine = _items.FirstOrDefault(i => i.ProductId == product.Id);
        var currentQtyInCart = existingLine?.Quantity ?? 0;
        if (product.StockCount <= currentQtyInCart)
        {
            return await FailAsync(
                "OUT_OF_STOCK",
                $"Produkt '{product.Name}' již není v požadovaném množství skladem.",
                StatusCodes.Status409Conflict,
                BuildStateSnapshot());
        }

        // 3. Přidání do košíku a přepnutí do WaitingForWeightVerification
        var unitPrice = _pricingService.GetActivePrice(product);
        if (existingLine is null)
        {
            _items.Add(new CartLine(
                product.Id,
                product.Ean,
                product.Name,
                Quantity: 1,
                unitPrice,
                product.WeightGrams,
                product.ToleranceGrams,
                product.BonusPointsReward,
                product.IsAgeRestricted));
        }
        else
        {
            var idx = _items.IndexOf(existingLine);
            _items[idx] = existingLine with { Quantity = existingLine.Quantity + 1, UnitPrice = unitPrice };
        }

        _status = CheckoutStatus.WaitingForWeightVerification;
        var expectedWeight = _items.Sum(i => i.UnitWeightGrams * i.Quantity);
        var totalTolerance = _items.Sum(i => i.UnitToleranceGrams * i.Quantity);
        _statusMessage = $"Položte '{product.Name}' ({product.WeightGrams} g) na odkládací váhu. Očekávaná celková hmotnost: {expectedWeight} g (±{totalTolerance} g).";

        // Pokud už váha v tuto chvíli odpovídá (např. při okamžitém zvážení), vyhodnotíme ji
        EvaluateWeightVerificationInternal(_scaleService.CurrentWeightGrams);

        var state = BuildStateSnapshot();
        await _hub.Clients.All.CheckoutStateChanged(state);

        return new CheckoutActionResult(true, StatusCodes.Status200OK, null, _statusMessage, state);
    }

    private async Task HandleScaleWeightStabilizedAsync(int totalGrams)
    {
        await _mutex.WaitAsync();
        try
        {
            if (_items.Count == 0 || _status is CheckoutStatus.Idle or CheckoutStatus.Paying or CheckoutStatus.Completed)
                return;

            EvaluateWeightVerificationInternal(totalGrams);
            var state = BuildStateSnapshot(totalGrams);
            await _hub.Clients.All.CheckoutStateChanged(state);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private void EvaluateWeightVerificationInternal(int currentScaleGrams)
    {
        if (_items.Count == 0)
            return;

        var expected = _items.Sum(i => i.UnitWeightGrams * i.Quantity);
        var tolerance = _items.Sum(i => i.UnitToleranceGrams * i.Quantity);
        var diff = Math.Abs(currentScaleGrams - expected);

        if (diff <= tolerance)
        {
            _status = CheckoutStatus.Ready;
            _statusMessage = $"Váha souhlasí ({currentScaleGrams} g / očekáváno {expected} g ± {tolerance} g). Můžete skenovat další zboží nebo zaplatit.";
        }
        else
        {
            _status = CheckoutStatus.WaitingForWeightVerification;
            _statusMessage = $"Kontrola váhy: na váze je {currentScaleGrams} g, očekáváno {expected} g (±{tolerance} g). Upravte zboží na odkládací ploše.";
        }
    }

    private async Task<CheckoutActionResult> FailAsync(string errorCode, string message, int httpStatus, CheckoutStateDto state)
    {
        _logger.LogWarning("Pokladna [{ErrorCode}]: {Message}", errorCode, message);
        await _hub.Clients.All.KioskError(new KioskErrorEvent(errorCode, message, _timeProvider.GetUtcNow().UtcDateTime));
        return new CheckoutActionResult(false, httpStatus, errorCode, message, state);
    }

    private CheckoutStateDto BuildStateSnapshot(int? overrideScaleGrams = null)
    {
        var currentGrams = overrideScaleGrams ?? _scaleService.CurrentWeightGrams;
        var expectedGrams = _items.Sum(i => i.UnitWeightGrams * i.Quantity);
        var totalTolerance = _items.Sum(i => i.UnitToleranceGrams * i.Quantity);
        var isWeightVerified = _items.Count > 0 && Math.Abs(currentGrams - expectedGrams) <= totalTolerance;

        var subtotal = _items.Sum(i => i.UnitPrice * i.Quantity);
        var effectiveRedeemed = Math.Min(_redeemedPoints, (int)Math.Floor(subtotal));
        var finalToPay = Math.Max(0m, subtotal - effectiveRedeemed);
        var totalBonus = _items.Sum(i => i.BonusPointsPerUnit * i.Quantity);

        var itemsDto = _items.Select(i => new CheckoutCartItemDto(
            i.ProductId,
            i.Ean,
            i.Name,
            i.Quantity,
            i.UnitPrice,
            i.UnitPrice * i.Quantity,
            i.UnitWeightGrams,
            i.UnitToleranceGrams,
            i.BonusPointsPerUnit,
            i.IsAgeRestricted)).ToList();

        return new CheckoutStateDto(
            _checkoutSessionId,
            _status,
            _customerId,
            _customerName,
            _customerIsAdult,
            _customerAvailablePoints,
            _storeSessionId,
            itemsDto,
            expectedGrams,
            totalTolerance,
            currentGrams,
            isWeightVerified,
            subtotal,
            effectiveRedeemed,
            PointsDiscountAmount: effectiveRedeemed,
            finalToPay,
            totalBonus,
            _pricingService.IsNightTime(),
            _statusMessage);
    }

    private void ResetInternal()
    {
        _checkoutSessionId = Guid.NewGuid();
        _status = CheckoutStatus.Idle;
        _customerId = null;
        _customerName = null;
        _customerIsAdult = false;
        _customerAvailablePoints = 0;
        _storeSessionId = null;
        _items.Clear();
        _redeemedPoints = 0;
        _statusMessage = "Připraveno. Pípněte zboží nebo svůj zákaznický QR kód.";
    }

    public void Dispose()
    {
        _scaleService.OnWeightStabilized -= HandleScaleWeightStabilizedAsync;
        _mutex.Dispose();
    }

    private sealed record CartLine(
        int ProductId,
        string Ean,
        string Name,
        int Quantity,
        decimal UnitPrice,
        int UnitWeightGrams,
        int UnitToleranceGrams,
        int BonusPointsPerUnit,
        bool IsAgeRestricted);
}
