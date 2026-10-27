namespace CrazyMarket.Api.Domain.Enums;

/// <summary>
/// Role účtu pro zásobování a správu prodejny:
/// - <see cref="ExternalVendor"/>: externí dodavatel (vidí a naskladňuje jen produkty se svým SupplierId),
/// - <see cref="InternalStaff"/>: interní personál / DPP brigádník (může naskladnit jakýkoliv produkt v DB),
/// - <see cref="Admin"/>: majitel / správce prodejny (plný přístup k naskladnění, přeceňování i správě).
/// </summary>
public enum SupplierRole
{
    ExternalVendor,
    InternalStaff,
    Admin
}
