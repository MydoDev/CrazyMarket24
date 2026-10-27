using System.Net.Http.Json;
using CrazyMarket.Api.Abstractions;
using CrazyMarket.Api.Contracts;
using CrazyMarket.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CrazyMarket.Api.Services;

/// <summary>
/// Produkční služba platebního terminálu.
/// Pokud terminál na síti (<c>PaymentTerminal:Url</c>) neodpoví synchronně nebo čeká na přiložení karty,
/// drží asynchronní <see cref="TaskCompletionSource{TResult}"/> a vyčká na potvrzení z terminálu / <c>POST /api/dev/terminal/resolve</c>.
/// </summary>
public sealed class PaymentTerminalService(
    HttpClient httpClient,
    IConfiguration configuration,
    IHubContext<StoreHub, IStoreHubClient> hub,
    ILogger<PaymentTerminalService> logger) : IPaymentTerminal
{
    private readonly object _sync = new();
    private PendingPaymentContext? _pending;

    public bool HasPendingPayment
    {
        get
        {
            lock (_sync)
                return _pending is not null;
        }
    }

    public async Task<PaymentTerminalResult> ProcessPaymentAsync(decimal amount, Guid orderId, CancellationToken ct = default)
    {
        if (amount <= 0m)
        {
            var zeroRef = $"POINTS-{orderId.ToString("N")[..8].ToUpperInvariant()}";
            return new PaymentTerminalResult(true, zeroRef, "Plně uhrazeno věrnostními body (0 Kč k platbě kartou).");
        }

        TaskCompletionSource<PaymentTerminalResult> tcs;
        lock (_sync)
        {
            _pending?.Tcs.TrySetCanceled();
            tcs = new TaskCompletionSource<PaymentTerminalResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending = new PendingPaymentContext(orderId, amount, tcs);
        }

        logger.LogInformation("Terminál: zahájena platba {Amount:0.00} Kč pro objednávku {OrderId}. Čekám na přiložení karty...", amount, orderId);
        await hub.Clients.All.PaymentStatusChanged(new PaymentStatusEvent(
            orderId, amount, Status: "Pending", Reference: null, Message: "Přiložte platební kartu k terminálu..."));

        // Pokud je v konfiguraci URL fyzického platebního terminálu, zkusíme na něj poslat požadavek
        var terminalUrl = configuration.GetValue<string>("PaymentTerminal:Url");
        if (!string.IsNullOrWhiteSpace(terminalUrl))
        {
            _ = TryPushToPhysicalTerminalAsync(terminalUrl, orderId, amount);
        }

        var timeoutSeconds = configuration.GetValue<int?>("PaymentTerminal:TimeoutSeconds") ?? 120;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        await using var reg = timeoutCts.Token.Register(() =>
        {
            tcs.TrySetResult(new PaymentTerminalResult(false, null, "Časový limit pro přiložení karty vypršel."));
        });

        try
        {
            var result = await tcs.Task;
            await hub.Clients.All.PaymentStatusChanged(new PaymentStatusEvent(
                orderId,
                amount,
                Status: result.Approved ? "Approved" : "Declined",
                Reference: result.TransactionReference,
                Message: result.Message));

            return result;
        }
        finally
        {
            lock (_sync)
            {
                if (_pending?.Tcs == tcs)
                    _pending = null;
            }
        }
    }

    public Task<bool> ResolvePendingPaymentAsync(bool approved, string? reference = null)
    {
        PendingPaymentContext? current;
        lock (_sync)
        {
            current = _pending;
        }

        if (current is null)
        {
            logger.LogWarning("ResolvePendingPaymentAsync zavolán, ale na terminálu neprobíhá žádná čekající platba.");
            return Task.FromResult(false);
        }

        var txRef = reference ?? (approved
            ? $"TERM-{DateTime.UtcNow:yyyyMMddHHmmss}-{current.OrderId.ToString("N")[..6].ToUpperInvariant()}"
            : null);

        var msg = approved
            ? "Platba kartou byla schválena (APPROVED)."
            : "Platba kartou byla zamítnuta (DECLINED).";

        logger.LogInformation("Terminál: platba {OrderId} ({Amount:0.00} Kč) vyhodnocena -> Approved={Approved}, Ref={Ref}",
            current.OrderId, current.Amount, approved, txRef);

        var resolved = current.Tcs.TrySetResult(new PaymentTerminalResult(approved, txRef, msg));
        return Task.FromResult(resolved);
    }

    private async Task TryPushToPhysicalTerminalAsync(string terminalUrl, Guid orderId, decimal amount)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await httpClient.PostAsJsonAsync(terminalUrl, new { orderId, amount, currency = "CZK" }, cts.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Fyzický platební terminál na {Url} není dostupný ({Message}). Čekám na vyhodnocení přes callback / DevTestController.",
                terminalUrl, ex.Message);
        }
    }

    private sealed record PendingPaymentContext(
        Guid OrderId,
        decimal Amount,
        TaskCompletionSource<PaymentTerminalResult> Tcs);
}
