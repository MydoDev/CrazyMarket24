namespace CrazyMarket.Api.Domain.Enums;

/// <summary>Stav platby objednávky (odpověď platebního terminálu).</summary>
public enum PaymentStatus
{
    /// <summary>Košík se plní, platba ještě nebyla zahájena.</summary>
    Draft,

    /// <summary>Platba odeslána na terminál, čeká se na odpověď.</summary>
    Pending,

    Approved,
    Declined,
    Cancelled
}
