namespace CrazyMarket.Api.Domain.Entities;

/// <summary>
/// Zboží v prodejně. Hmotnost a tolerance slouží kontrolní váze na pokladně,
/// <see cref="NightPrice"/> se uplatní v nočním cenovém okně (22:00–05:00) a
/// <see cref="ActionPrice"/> má přednost, pokud je v platnosti <see cref="ActionValidUntil"/>.
/// </summary>
public class Product
{
    public const int DefaultToleranceGrams = 15;

    public int Id { get; set; }

    /// <summary>ID skladové karty v POHODĚ (null = zatím nesynchronizováno).</summary>
    public string? PohodaId { get; set; }

    /// <summary>Dodavatel produktu (volitelné).</summary>
    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>EAN-8 / EAN-13 včetně kontrolní číslice. Unikátní.</summary>
    public required string Ean { get; set; }

    public required string Name { get; set; }

    /// <summary>Denní cena s DPH v Kč.</summary>
    public decimal BasePrice { get; set; }

    /// <summary>Noční cena s DPH v Kč (zahrnuje noční přirážku).</summary>
    public decimal NightPrice { get; set; }

    /// <summary>Akční cena s DPH v Kč (má přednost před denní i noční cenou, pokud je platná).</summary>
    public decimal? ActionPrice { get; set; }

    /// <summary>Konec platnosti akční ceny (UTC).</summary>
    public DateTime? ActionValidUntil { get; set; }

    /// <summary>Počet věrnostních bodů, které zákazník získá za nákup 1 kusu.</summary>
    public int BonusPointsReward { get; set; }

    /// <summary>Brutto hmotnost jednoho kusu v gramech (tak, jak ji uvidí váha).</summary>
    public int WeightGrams { get; set; }

    /// <summary>Povolená odchylka hmotnosti na kus v gramech.</summary>
    public int ToleranceGrams { get; set; } = DefaultToleranceGrams;

    public int StockCount { get; set; }

    /// <summary>Prodej pouze osobám 18+ (alkohol, tabák, energetické nápoje dle politiky prodejny).</summary>
    public bool IsAgeRestricted { get; set; }

    /// <summary>MAC adresa přiřazené e-ink cenovky (null = cenovka nepřiřazena).</summary>
    public string? EslMacAddress { get; set; }

    /// <summary>Poslední vykreslený náhled cenovky (PNG 296×128, 1 bit) jako Base64.</summary>
    public string? LastEslImageBase64 { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
