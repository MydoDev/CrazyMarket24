using System.Text.Json;
using CrazyMarket.Api.Abstractions;
using CrazyMarket.Api.Contracts;
using CrazyMarket.Api.Data;
using CrazyMarket.Api.Domain.Entities;
using CrazyMarket.Api.Domain.Enums;
using CrazyMarket.Api.Hubs;
using CrazyMarket.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CrazyMarket.Api.Endpoints;

public static class AccessEndpoints
{
    public static RouteGroupBuilder MapAccessEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/access").WithTags("Access & TOTP");

        group.MapPost("/scan-entry", ScanEntry)
            .Accepts<ScanEntryRequest>("application/json", "text/plain")
            .WithName("ScanEntryQrCode");

        group.MapGet("/totp/customer/{customerId:guid}", GetCustomerTotpToken)
            .WithName("GetCustomerTotpToken");

        group.MapGet("/totp/supplier/{supplierId:guid}", GetSupplierTotpToken)
            .WithName("GetSupplierTotpToken");

        group.MapGet("/sessions/active", GetActiveSessions)
            .WithName("GetActiveStoreSessions");

        return group;
    }

    /// <summary>
    /// Zpracuje naskenovaný QR kód u dveří ve formátu <c>CM24:{CUSTOMER|SUPPLIER}:{Id}:{TotpCode}</c>.
    /// Přijímá buď JSON <c>{"qrCode":"CM24:..."}</c>, JSON string <c>"CM24:..."</c>, nebo prostý text <c>CM24:...</c>.
    /// Ověří TOTP přes Otp.NET, založí <see cref="StoreSession"/>, odemkne dveře přes MQTT a notifikuje kiosek přes SignalR.
    /// </summary>
    private static async Task<Results<Ok<ScanEntryResponse>, BadRequest<ProblemDetails>, ProblemHttpResult, NotFound<ProblemDetails>>> ScanEntry(
        HttpContext httpContext,
        CrazyMarketDbContext db,
        TotpAccessService totpService,
        IDoorController doorController,
        IHubContext<StoreHub, IStoreHubClient> hub,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var rawQr = await ExtractQrStringAsync(httpContext.Request, ct);

        if (!TotpAccessService.TryParseEntryQr(rawQr, out var parsed, out var parseError) || parsed is null)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Neplatný QR kód",
                Detail = parseError,
                Status = StatusCodes.Status400BadRequest
            });
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().Date);

        if (parsed.ActorType == AccessActorType.Customer)
        {
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == parsed.ActorId, ct);
            if (customer is null)
            {
                return TypedResults.NotFound(new ProblemDetails
                {
                    Title = "Zákazník nenalezen",
                    Detail = $"Zákazník s ID {parsed.ActorId} neexistuje.",
                    Status = StatusCodes.Status404NotFound
                });
            }

            if (customer.IsBanned)
            {
                return TypedResults.Problem(
                    title: "Vstup zamítnut",
                    detail: "Zákazník má zablokovaný přístup do prodejny.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            if (!totpService.VerifyCode(customer.TotpSecret, parsed.TotpCode))
            {
                return TypedResults.Problem(
                    title: "Neplatný nebo expirovaný TOTP kód",
                    detail: "Ověření 6místného TOTP kódu selhalo.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var session = new StoreSession
            {
                Id = Guid.NewGuid(),
                CustomerId = customer.Id,
                EnteredAt = utcNow,
                CurrentState = SessionState.Inside
            };

            db.StoreSessions.Add(session);
            await db.SaveChangesAsync(ct);

            var welcome = $"Vítejte v CrazyMarket 24, {customer.FullName}!";
            await doorController.UnlockDoorAsync(durationSeconds: 5, ct);
            await doorController.PlayWelcomeAudioAsync(welcome, ct);

            var sessionDto = new StoreSessionDto(
                session.Id,
                AccessActorType.Customer,
                customer.Id,
                customer.FullName,
                customer.IsAdultOn(today),
                customer.LoyaltyPoints,
                session.EnteredAt,
                session.CurrentState);

            var response = new ScanEntryResponse(Granted: true, welcome, sessionDto);
            await hub.Clients.All.EntryGranted(response);
            return TypedResults.Ok(response);
        }
        else
        {
            var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == parsed.ActorId, ct);
            if (supplier is null)
            {
                return TypedResults.NotFound(new ProblemDetails
                {
                    Title = "Dodavatel nenalezen",
                    Detail = $"Dodavatel s ID {parsed.ActorId} neexistuje.",
                    Status = StatusCodes.Status404NotFound
                });
            }

            if (!totpService.VerifyCode(supplier.TotpSecret, parsed.TotpCode))
            {
                return TypedResults.Problem(
                    title: "Neplatný nebo expirovaný TOTP kód",
                    detail: "Ověření 6místného TOTP kódu dodavatele selhalo.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var session = new StoreSession
            {
                Id = Guid.NewGuid(),
                SupplierId = supplier.Id,
                EnteredAt = utcNow,
                CurrentState = SessionState.Inside
            };

            db.StoreSessions.Add(session);
            await db.SaveChangesAsync(ct);

            var welcome = $"Vstup zásobování povolen: {supplier.CompanyName}.";
            await doorController.UnlockDoorAsync(durationSeconds: 15, ct);
            await doorController.PlayWelcomeAudioAsync(welcome, ct);

            var sessionDto = new StoreSessionDto(
                session.Id,
                AccessActorType.Supplier,
                supplier.Id,
                supplier.CompanyName,
                IsAdult: true,
                LoyaltyPoints: 0,
                session.EnteredAt,
                session.CurrentState);

            var response = new ScanEntryResponse(Granted: true, welcome, sessionDto);
            await hub.Clients.All.EntryGranted(response);
            return TypedResults.Ok(response);
        }
    }

    private static async Task<Results<Ok<GeneratedEntryToken>, NotFound>> GetCustomerTotpToken(
        Guid customerId, CrazyMarketDbContext db, TotpAccessService totpService, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null)
            return TypedResults.NotFound();

        return TypedResults.Ok(totpService.GenerateToken(AccessActorType.Customer, customer.Id, customer.TotpSecret));
    }

    private static async Task<Results<Ok<GeneratedEntryToken>, NotFound>> GetSupplierTotpToken(
        Guid supplierId, CrazyMarketDbContext db, TotpAccessService totpService, CancellationToken ct)
    {
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId, ct);
        if (supplier is null)
            return TypedResults.NotFound();

        return TypedResults.Ok(totpService.GenerateToken(AccessActorType.Supplier, supplier.Id, supplier.TotpSecret));
    }

    private static async Task<Ok<List<StoreSessionDto>>> GetActiveSessions(
        CrazyMarketDbContext db, TimeProvider timeProvider, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().Date);
        var sessions = await db.StoreSessions
            .AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.Supplier)
            .Where(s => s.ExitedAt == null)
            .OrderByDescending(s => s.EnteredAt)
            .ToListAsync(ct);

        var result = sessions.Select(s =>
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
                s.Supplier?.CompanyName ?? "Dodavatel",
                IsAdult: true,
                LoyaltyPoints: 0,
                s.EnteredAt,
                s.CurrentState);
        }).ToList();

        return TypedResults.Ok(result);
    }

    private static async Task<string?> ExtractQrStringAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.Query.TryGetValue("qrCode", out var queryQr) && !string.IsNullOrWhiteSpace(queryQr))
            return queryQr.ToString();

        using var reader = new StreamReader(request.Body);
        var body = (await reader.ReadToEndAsync(ct)).Trim();
        if (string.IsNullOrEmpty(body))
            return null;

        if (body.StartsWith('{'))
        {
            try
            {
                var dto = JsonSerializer.Deserialize<ScanEntryRequest>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return dto?.QrCode;
            }
            catch
            {
                return null;
            }
        }

        if (body.StartsWith('"') && body.EndsWith('"') && body.Length >= 2)
        {
            return body[1..^1];
        }

        return body;
    }
}
