using CrazyMarket.Api.Abstractions;
using CrazyMarket.Api.Services;

namespace CrazyMarket.Api.Endpoints;

/// <summary>
/// Vývojové endpointy (dostupné výhradně v <c>IsDevelopment()</c>), kterými lze ze Swaggeru
/// vyvolat ustálení fyzické RS232 váhy a potvrzení/zamítnutí platby na platebním terminálu.
/// </summary>
public static class DevTestController
{
    public static RouteGroupBuilder MapDevTestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dev").WithTags("Dev Hardware Triggers (Development Only)");

        group.MapPost("/scale/set-weight", async (
            int grams,
            IScaleService scaleService,
            CheckoutSessionService checkout,
            CancellationToken ct) =>
        {
            await scaleService.SimulateRawFrameOrWeightAsync(grams, ct);
            var checkoutState = await checkout.GetStateAsync(ct);

            return Results.Ok(new
            {
                scaleWeightGrams = scaleService.CurrentWeightGrams,
                checkoutStatus = checkoutState.Status.ToString(),
                isWeightVerified = checkoutState.IsWeightVerified,
                expectedWeightGrams = checkoutState.ExpectedWeightGrams,
                toleranceGrams = checkoutState.TotalToleranceGrams,
                statusMessage = checkoutState.StatusMessage
            });
        }).WithName("DevScaleSetWeight");

        group.MapPost("/terminal/resolve", async (
            bool approved,
            IPaymentTerminal paymentTerminal) =>
        {
            var resolved = await paymentTerminal.ResolvePendingPaymentAsync(approved);
            if (!resolved)
            {
                return Results.Conflict(new
                {
                    resolved = false,
                    message = "Na platebním terminálu momentálně nečeká žádná rozpracovaná platba."
                });
            }

            return Results.Ok(new
            {
                resolved = true,
                approved,
                message = approved
                    ? "Čekající platba na terminálu byla schválena (APPROVED)."
                    : "Čekající platba na terminálu byla zamítnuta (DECLINED)."
            });
        }).WithName("DevTerminalResolve");

        return group;
    }
}
