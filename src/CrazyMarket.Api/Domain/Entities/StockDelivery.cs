namespace CrazyMarket.Api.Domain.Entities;

/// <summary>
/// Závoz zboží od dodavatele (podklad pro příjemku v POHODĚ a naskladnění).
/// </summary>
public class StockDelivery
{
    public Guid Id { get; set; }

    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public DateTime DeliveredAt { get; set; }

    /// <summary>Zda byla příjemka úspěšně vyexportována/synchronizována do POHODY.</summary>
    public bool PohodaSynced { get; set; }
    public DateTime? PohodaSyncedAt { get; set; }

    public ICollection<StockDeliveryItem> Items { get; set; } = new List<StockDeliveryItem>();
}

/// <summary>
/// Položka závozu od dodavatele s datem expirace šarže.
/// </summary>
public class StockDeliveryItem
{
    public int Id { get; set; }

    public Guid StockDeliveryId { get; set; }
    public StockDelivery StockDelivery { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int Quantity { get; set; }

    public DateOnly? ExpirationDate { get; set; }
}
