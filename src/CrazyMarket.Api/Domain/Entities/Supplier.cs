using CrazyMarket.Api.Domain.Enums;

namespace CrazyMarket.Api.Domain.Entities;

/// <summary>
/// Dodavatel zboží nebo interní zásobovací účet (brigádník / majitel).
/// Má vlastní TOTP klíč pro vstup do prodejny v zásobovacím režimu a přiřazenou roli <see cref="SupplierRole"/>.
/// </summary>
public class Supplier
{
    public Guid Id { get; set; }

    public required string CompanyName { get; set; }

    /// <summary>IČO dodavatele nebo interní identifikátor. Unikátní.</summary>
    public required string Ico { get; set; }

    /// <summary>Role určující rozsah oprávnění při zásobování (ExternalVendor vs. InternalStaff / Admin).</summary>
    public SupplierRole Role { get; set; } = SupplierRole.ExternalVendor;

    /// <summary>Base32 sdílené tajemství pro TOTP QR kód zásobování.</summary>
    public required string TotpSecret { get; set; }

    /// <summary>Zda dodavatel povoluje automatické dynamické přeceňování (např. slevu před expirací).</summary>
    public bool AllowDynamicPricing { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<StockDelivery> Deliveries { get; set; } = new List<StockDelivery>();
    public ICollection<StoreSession> Sessions { get; set; } = new List<StoreSession>();

    /// <summary>Zda účet může naskladňovat libovolné produkty v celé prodejně.</summary>
    public bool CanManageAllProducts => Role is SupplierRole.InternalStaff or SupplierRole.Admin;
}
