namespace CrazyMarket.Api.Domain.Entities;

/// <summary>Registrovaný zákazník ověřený přes BankID.</summary>
public class Customer
{
    public Guid Id { get; set; }

    /// <summary>Claim <c>sub</c> z BankID – stabilní identifikátor osoby. Unikátní.</summary>
    public required string BankIdSub { get; set; }

    public required string FullName { get; set; }

    public DateOnly BirthDate { get; set; }

    /// <summary>
    /// Plnoletost ověřená BankID při registraci. Pro kontrolu 18+ na pokladně
    /// používej <see cref="IsAdultOn"/> – zákazník mezitím mohl dovršit 18 let.
    /// </summary>
    public bool IsAdult { get; set; }

    /// <summary>Base32 sdílené tajemství pro TOTP QR kód ve vstupní peněžence.</summary>
    public required string TotpSecret { get; set; }

    /// <summary>Aktuální zůstatek věrnostních bodů zákazníka.</summary>
    public int LoyaltyPoints { get; set; }

    public bool IsBanned { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<StoreSession> Sessions { get; set; } = new List<StoreSession>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();

    /// <summary>Vrátí, zda je zákazník k danému dni plnoletý (18+).</summary>
    public bool IsAdultOn(DateOnly date) => BirthDate.AddYears(18) <= date;
}
