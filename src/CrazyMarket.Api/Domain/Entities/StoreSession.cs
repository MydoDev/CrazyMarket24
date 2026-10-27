using CrazyMarket.Api.Domain.Enums;

namespace CrazyMarket.Api.Domain.Entities;

/// <summary>Jedna návštěva v prodejně (zákazník nebo dodavatel při zásobování) – od vstupu přes QR u dveří až po odchod.</summary>
public class StoreSession
{
    public Guid Id { get; set; }

    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public DateTime EnteredAt { get; set; }
    public DateTime? ExitedAt { get; set; }

    public SessionState CurrentState { get; set; } = SessionState.Inside;

    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
