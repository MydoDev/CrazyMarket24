using System.Text.Json;
using CrazyMarket.Api.Contracts;
using CrazyMarket.Api.Services;

namespace CrazyMarket.Api.Endpoints;

public static class KioskEndpoints
{
    public static RouteGroupBuilder MapKioskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/kiosk").WithTags("Kiosk Self-Checkout");

        group.MapGet("/state", async (CheckoutSessionService checkout, CancellationToken ct) =>
            Results.Ok(await checkout.GetStateAsync(ct)))
            .WithName("GetKioskCheckoutState");

        group.MapPost("/scan", ScanCode)
            .Accepts<KioskScanRequest>("application/json", "text/plain")
            .WithName("KioskScanCode");

        group.MapPost("/scan-ean", ScanCode)
            .Accepts<KioskScanRequest>("application/json", "text/plain")
            .WithName("KioskScanEanCode");

        group.MapPost("/apply-points", ApplyPoints)
            .WithName("KioskApplyPoints");

        group.MapPost("/pay", Pay)
            .WithName("KioskPay");

        group.MapPost("/reset", async (CheckoutSessionService checkout, CancellationToken ct) =>
            Results.Ok(await checkout.ResetSessionAsync(ct)))
            .WithName("KioskResetSession");

        return group;
    }

    private static async Task<IResult> ScanCode(
        HttpContext httpContext,
        CheckoutSessionService checkout,
        CancellationToken ct)
    {
        var scannedCode = await ExtractScannedCodeAsync(httpContext.Request, ct);
        var result = await checkout.ProcessScanAsync(scannedCode, ct);
        return Results.Json(result, statusCode: result.HttpStatusCode);
    }

    private static async Task<IResult> ApplyPoints(
        HttpContext httpContext,
        CheckoutSessionService checkout,
        int? points,
        CancellationToken ct)
    {
        var requestedPoints = points;
        if (requestedPoints is null && httpContext.Request.ContentLength is > 0)
        {
            try
            {
                var body = await httpContext.Request.ReadFromJsonAsync<ApplyPointsRequest>(cancellationToken: ct);
                requestedPoints = body?.PointsToRedeem;
            }
            catch
            {
                // Pokud tělo není JSON, uplatníme maximum dostupných bodů.
            }
        }

        var result = await checkout.ApplyPointsAsync(requestedPoints, ct);
        return Results.Json(result, statusCode: result.HttpStatusCode);
    }

    private static async Task<IResult> Pay(
        CheckoutSessionService checkout,
        CancellationToken ct)
    {
        var result = await checkout.PayAsync(ct);
        return Results.Json(result, statusCode: result.HttpStatusCode);
    }

    private static async Task<string?> ExtractScannedCodeAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.Query.TryGetValue("scannedCode", out var queryCode) && !string.IsNullOrWhiteSpace(queryCode))
            return queryCode.ToString();

        if (request.Query.TryGetValue("ean", out var queryEan) && !string.IsNullOrWhiteSpace(queryEan))
            return queryEan.ToString();

        using var reader = new StreamReader(request.Body);
        var body = (await reader.ReadToEndAsync(ct)).Trim();
        if (string.IsNullOrEmpty(body))
            return null;

        if (body.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("scannedCode", out var scProp) && scProp.ValueKind == JsonValueKind.String)
                    return scProp.GetString();
                if (doc.RootElement.TryGetProperty("ScannedCode", out var scProp2) && scProp2.ValueKind == JsonValueKind.String)
                    return scProp2.GetString();
                if (doc.RootElement.TryGetProperty("ean", out var eanProp) && eanProp.ValueKind == JsonValueKind.String)
                    return eanProp.GetString();
                if (doc.RootElement.TryGetProperty("code", out var codeProp) && codeProp.ValueKind == JsonValueKind.String)
                    return codeProp.GetString();
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
