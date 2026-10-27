namespace CrazyMarket.Api.Domain.Enums;

/// <summary>Životní cyklus návštěvy zákazníka v prodejně.</summary>
public enum SessionState
{
    /// <summary>Zákazník prošel dveřmi a nakupuje.</summary>
    Inside,

    /// <summary>Zákazník zahájil placení na kiosku.</summary>
    AtCheckout,

    /// <summary>Nákup zaplacen, zákazník může odejít.</summary>
    Paid,

    /// <summary>Zákazník odešel bez nákupu.</summary>
    LeftWithoutPurchase,

    /// <summary>Podezřelá situace (nesouhlas váhy, odchod bez zaplacení…), vyžaduje obsluhu.</summary>
    SecurityAlert
}
