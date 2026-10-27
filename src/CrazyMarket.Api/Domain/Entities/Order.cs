using CrazyMarket.Api.Domain.Enums;

namespace CrazyMarket.Api.Domain.Entities;

/// <summary>Nákup na samoobslužné pokladně.</summary>
public class Order
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    /// <summary>Návštěva, během které nákup vznikl.</summary>
    public Guid? StoreSessionId { get; set; }
    public StoreSession? StoreSession { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }

    /// <summary>Celková částka s DPH v Kč (součet <see cref="OrderItem.LineTotal"/>).</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Ceny v objednávce byly účtovány v nočním okně.</summary>
    public bool IsNightPricing { get; set; }

    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Draft;

    /// <summary>Reference transakce vrácená platebním terminálem.</summary>
    public string? PaymentReference { get; set; }

    // --- Kontrolní váha ---
    public WeightValidationStatus WeightValidation { get; set; } = WeightValidationStatus.NotChecked;
    public int ExpectedWeightGrams { get; set; }
    public int? MeasuredWeightGrams { get; set; }

    // --- POHODA ---
    public bool PohodaXmlExported { get; set; }
    public DateTime? PohodaExportedAt { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}
