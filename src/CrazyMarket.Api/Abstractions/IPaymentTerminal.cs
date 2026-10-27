namespace CrazyMarket.Api.Abstractions;

public record PaymentTerminalResult(
    bool Approved,
    string? TransactionReference,
    string Message);

/// <summary>Produkční rozhraní platebního terminálu na samoobslužné pokladně.</summary>
public interface IPaymentTerminal
{
    Task<PaymentTerminalResult> ProcessPaymentAsync(decimal amount, Guid orderId, CancellationToken ct = default);

    /// <summary>Dokončí čekající platbu na terminálu (callback z terminálu nebo z DevTestController).</summary>
    Task<bool> ResolvePendingPaymentAsync(bool approved, string? reference = null);

    bool HasPendingPayment { get; }
}
