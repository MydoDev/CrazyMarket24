namespace CrazyMarket.Api.Domain.Entities;

/// <summary>
/// Řádek objednávky. Název, EAN, cena a hmotnost jsou snapshot v okamžiku nákupu,
/// aby pozdější přecenění produktu nezměnilo historické doklady ani XML pro POHODU.
/// </summary>
public class OrderItem
{
    public int Id { get; set; }

    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public required string Ean { get; set; }
    public required string ProductName { get; set; }

    public int Quantity { get; set; }

    /// <summary>Jednotková cena s DPH, za kterou se položka skutečně prodala (denní/noční).</summary>
    public decimal UnitPrice { get; set; }

    public decimal LineTotal { get; set; }

    public int UnitWeightGrams { get; set; }
    public int UnitToleranceGrams { get; set; }
}
