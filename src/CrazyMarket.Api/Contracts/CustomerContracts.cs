using CrazyMarket.Api.Domain.Entities;

namespace CrazyMarket.Api.Contracts;

/// <summary>Zákazník pro přehledy – nikdy nevrací TOTP tajemství.</summary>
public record CustomerDto(
    Guid Id,
    string FullName,
    DateOnly BirthDate,
    bool IsAdult,
    bool IsAdultToday,
    int LoyaltyPoints,
    bool IsBanned,
    DateTime CreatedAt)
{
    public static CustomerDto From(Customer c, DateOnly today) =>
        new(c.Id, c.FullName, c.BirthDate, c.IsAdult, c.IsAdultOn(today), c.LoyaltyPoints, c.IsBanned, c.CreatedAt);
}

public record CustomerOrderItemHistoryDto(
    string Ean,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public record CustomerOrderHistoryDto(
    Guid Id,
    DateTime CreatedAt,
    DateTime? PaidAt,
    decimal TotalAmount,
    bool IsNightPricing,
    string PaymentStatus,
    string? PaymentReference,
    bool PohodaXmlExported,
    List<CustomerOrderItemHistoryDto> Items);
