namespace CrazyMarket.Api.Domain.Enums;

/// <summary>Výsledek porovnání očekávané hmotnosti košíku s hmotností na kontrolní váze.</summary>
public enum WeightValidationStatus
{
    NotChecked,

    /// <summary>Naměřená hmotnost je v součtové toleranci položek.</summary>
    Passed,

    /// <summary>Hmotnost nesouhlasí – na váze je něco navíc nebo něco chybí.</summary>
    Failed,

    /// <summary>Neshodu ručně schválil personál (vzdáleně z /admin).</summary>
    OverriddenByStaff
}
