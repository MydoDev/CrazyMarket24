using CrazyMarket.Api.Abstractions;
using CrazyMarket.Api.Contracts;
using CrazyMarket.Api.Data;
using CrazyMarket.Api.Domain.Enums;
using CrazyMarket.Api.Hubs;
using CrazyMarket.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CrazyMarket.Api.Endpoints;

public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Admin Store Dashboard");

        group.MapGet("/overview", GetOverview).WithName("GetAdminDashboardOverview");
        group.MapPost("/door/emergency-unlock", EmergencyUnlockDoor).WithName("AdminEmergencyUnlockDoor");
        group.MapPost("/scale/tare", TareScale).WithName("AdminTareScale");
        group.MapPost("/pricing/toggle-night", ToggleDayNightPricing).WithName("AdminToggleDayNightPricing");
        group.MapPost("/sessions/{sessionId:guid}/close", CloseStoreSession).WithName("AdminCloseStoreSession");

        return group;
    }

    private static async Task<Ok<AdminDashboardDto>> GetOverview(
        CrazyMarketDbContext db,
        PricingService pricing,
        IScaleService scaleService,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().Date);

        // 1. Aktivní osoby v prodejně (StoreSession)
        var sessions = await db.StoreSessions
            .AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.Supplier)
            .Where(s => s.ExitedAt == null)
            .OrderByDescending(s => s.EnteredAt)
            .ToListAsync(ct);

        var sessionDtos = sessions.Select(s =>
        {
            if (s.Customer is not null)
            {
                return new StoreSessionDto(
                    s.Id,
                    AccessActorType.Customer,
                    s.Customer.Id,
                    s.Customer.FullName,
                    s.Customer.IsAdultOn(today),
                    s.Customer.LoyaltyPoints,
                    s.EnteredAt,
                    s.CurrentState);
            }

            return new StoreSessionDto(
                s.Id,
                AccessActorType.Supplier,
                s.SupplierId ?? Guid.Empty,
                s.Supplier?.CompanyName ?? "Zásobování",
                IsAdult: true,
                LoyaltyPoints: 0,
                s.EnteredAt,
                s.CurrentState);
        }).ToList();

        // 2. Všechny produkty s E-ink cenovkami
        var products = await db.Products.AsNoTracking().OrderBy(p => p.Id).ToListAsync(ct);
        var productDtos = products.Select(p => ProductDto.From(p, pricing)).ToList();

        // 3. Stav POHODA XML synchronizace (Prodejky + Příjemky)
        var orders = await db.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        var deliveries = await db.StockDeliveries
            .AsNoTracking()
            .Include(d => d.Supplier)
            .Include(d => d.Items)
            .OrderByDescending(d => d.DeliveredAt)
            .Take(20)
            .ToListAsync(ct);

        var pohodaDocs = new List<PohodaSyncItemDto>();

        foreach (var o in orders)
        {
            var fileName = $"prodejka_{o.CreatedAt:yyyyMMdd_HHmmss}_{o.Id:N}.xml";
            pohodaDocs.Add(new PohodaSyncItemDto(
                o.Id,
                "Prodejka (pro:prodejka)",
                o.Customer.FullName,
                o.PaidAt ?? o.CreatedAt,
                o.TotalAmount,
                o.Items.Sum(i => i.Quantity),
                o.PohodaXmlExported,
                o.PohodaXmlExported ? fileName : null,
                o.PohodaXmlExported ? "Uloženo v /pohoda-export/ (Windows-1250)" : "Čeká na export"));
        }

        foreach (var d in deliveries)
        {
            var fileName = $"prijemka_{d.DeliveredAt:yyyyMMdd_HHmmss}_{d.Id:N}.xml";
            pohodaDocs.Add(new PohodaSyncItemDto(
                d.Id,
                "Příjemka (pri:prijemka)",
                d.Supplier.CompanyName,
                d.DeliveredAt,
                null,
                d.Items.Sum(i => i.Quantity),
                d.PohodaSynced,
                d.PohodaSynced ? fileName : null,
                d.PohodaSynced ? "Uloženo v /pohoda-export/ (Windows-1250)" : "Čeká na export"));
        }

        pohodaDocs = pohodaDocs.OrderByDescending(x => x.CreatedAtUtc).ToList();

        return TypedResults.Ok(new AdminDashboardDto(
            pricing.IsNightTime(),
            pricing.NightModeOverride,
            scaleService.CurrentWeightGrams,
            sessionDtos,
            productDtos,
            pohodaDocs));
    }

    private static async Task<IResult> EmergencyUnlockDoor(
        IDoorController doorController,
        CancellationToken ct)
    {
        await doorController.UnlockDoorAsync(durationSeconds: 15, ct);
        await doorController.PlayWelcomeAudioAsync("Nouzové odemknutí dveří z řídicího panelu prodejny.", ct);
        return Results.Ok(new
        {
            unlocked = true,
            durationSeconds = 15,
            message = "Dveře byly nouzově odemčeny přes MQTT na 15 sekund."
        });
    }

    private static async Task<IResult> TareScale(
        IScaleService scaleService,
        CheckoutSessionService checkoutService,
        CancellationToken ct)
    {
        await scaleService.SimulateRawFrameOrWeightAsync(0, ct);
        var state = await checkoutService.GetStateAsync(ct);

        return Results.Ok(new
        {
            tared = true,
            scaleWeightGrams = scaleService.CurrentWeightGrams,
            checkoutStatus = state.Status.ToString(),
            message = "Pokladní váha byla vynulována (Tare -> 0 g)."
        });
    }

    /// <summary>
    /// Přepne celoplošně denní / noční režim v celé prodejně a okamžitě přegeneruje všechny E-ink cenovky (296×128 px).
    /// </summary>
    private static async Task<IResult> ToggleDayNightPricing(
        bool? forceNight,
        CrazyMarketDbContext db,
        PricingService pricing,
        IEslGateway eslGateway,
        IHubContext<StoreHub, IStoreHubClient> hub,
        CancellationToken ct)
    {
        var targetNight = forceNight ?? !pricing.IsNightTime();
        pricing.SetNightModeOverride(targetNight);

        var products = await db.Products.OrderBy(p => p.Id).ToListAsync(ct);
        var updatedDtos = new List<ProductDto>(products.Count);

        foreach (var prod in products)
        {
            var activePrice = pricing.GetActivePrice(prod);
            var eslRes = await eslGateway.RenderAndPushTagAsync(prod, activePrice, ct);

            var dto = ProductDto.From(prod, pricing);
            updatedDtos.Add(dto);

            await hub.Clients.All.ProductChanged(dto);
            await hub.Clients.All.EslTagUpdated(new EslPreviewDto(
                prod.Id, prod.Ean, prod.Name, prod.EslMacAddress,
                eslRes.ActivePrice, eslRes.BannerText, eslRes.PushedToGateway, eslRes.Base64Png));
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            isNightPricingActive = pricing.IsNightTime(),
            regeneratedTagsCount = updatedDtos.Count,
            message = targetNight
                ? $"Přepnuto na NOČNÍ CENÍK – přegenerováno {updatedDtos.Count} E-ink cenovek."
                : $"Přepnuto na DENNÍ CENÍK – přegenerováno {updatedDtos.Count} E-ink cenovek.",
            products = updatedDtos
        });
    }

    private static async Task<Results<Ok, NotFound>> CloseStoreSession(
        Guid sessionId,
        CrazyMarketDbContext db,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var session = await db.StoreSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
            return TypedResults.NotFound();

        session.ExitedAt = timeProvider.GetUtcNow().UtcDateTime;
        if (session.CurrentState is SessionState.Inside or SessionState.AtCheckout)
        {
            session.CurrentState = SessionState.LeftWithoutPurchase;
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok();
    }
}
