using System.Text.RegularExpressions;
using CrazyMarket.Api.Domain.Entities;
using CrazyMarket.Api.Services;

namespace CrazyMarket.Api.Contracts;

/// <summary>Produkt pro API a SignalR včetně aktuálně platné prodejní ceny a 1-bit E-ink náhledu.</summary>
public record ProductDto(
    int Id,
    string? PohodaId,
    Guid? SupplierId,
    string Ean,
    string Name,
    decimal BasePrice,
    decimal NightPrice,
    decimal? ActionPrice,
    DateTime? ActionValidUntil,
    decimal ActivePrice,
    ActivePriceTier ActivePriceTier,
    int BonusPointsReward,
    int WeightGrams,
    int ToleranceGrams,
    int StockCount,
    bool IsAgeRestricted,
    string? EslMacAddress,
    bool HasEslImage,
    string? LastEslImageBase64,
    DateTime UpdatedAt)
{
    public static ProductDto From(Product p, PricingService pricing)
    {
        var (activePrice, tier) = pricing.EvaluatePrice(p);
        return new(
            p.Id,
            p.PohodaId,
            p.SupplierId,
            p.Ean,
            p.Name,
            p.BasePrice,
            p.NightPrice,
            p.ActionPrice,
            p.ActionValidUntil,
            activePrice,
            tier,
            p.BonusPointsReward,
            p.WeightGrams,
            p.ToleranceGrams,
            p.StockCount,
            p.IsAgeRestricted,
            p.EslMacAddress,
            p.LastEslImageBase64 is not null,
            p.LastEslImageBase64,
            p.UpdatedAt);
    }
}

/// <summary>Tělo POST (založení) i PUT (úplná náhrada) produktu.</summary>
public partial record ProductUpsertRequest(
    string Ean,
    string Name,
    decimal BasePrice,
    decimal NightPrice,
    decimal? ActionPrice,
    DateTime? ActionValidUntil,
    int BonusPointsReward,
    int WeightGrams,
    int? ToleranceGrams,
    int StockCount,
    bool IsAgeRestricted,
    Guid? SupplierId,
    string? PohodaId,
    string? EslMacAddress)
{
    private const decimal MaxPrice = 99_999_999.99m;

    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string field, string message) => errors[field] = [message];

        if (!Domain.Ean.IsValid(Ean))
            Add(nameof(Ean), "EAN musí mít 8 nebo 13 číslic a platnou kontrolní číslici.");

        if (string.IsNullOrWhiteSpace(Name))
            Add(nameof(Name), "Název je povinný.");
        else if (Name.Length > 200)
            Add(nameof(Name), "Název může mít nejvýše 200 znaků.");

        if (BasePrice < 0 || BasePrice > MaxPrice || decimal.Round(BasePrice, 2) != BasePrice)
            Add(nameof(BasePrice), "Cena musí být nezáporná s nejvýše 2 desetinnými místy.");

        if (NightPrice < 0 || NightPrice > MaxPrice || decimal.Round(NightPrice, 2) != NightPrice)
            Add(nameof(NightPrice), "Cena musí být nezáporná s nejvýše 2 desetinnými místy.");

        if (ActionPrice is { } ap && (ap <= 0 || ap > MaxPrice || decimal.Round(ap, 2) != ap))
            Add(nameof(ActionPrice), "Akční cena musí být kladná s nejvýše 2 desetinnými místy.");

        if (BonusPointsReward < 0)
            Add(nameof(BonusPointsReward), "Bonusové body nesmí být záporné.");

        if (WeightGrams is <= 0 or > 50_000)
            Add(nameof(WeightGrams), "Hmotnost musí být 1–50 000 g (váha musí kus spolehlivě rozpoznat).");

        if (ToleranceGrams is < 0 or > 5_000)
            Add(nameof(ToleranceGrams), "Tolerance musí být 0–5 000 g.");

        if (StockCount < 0)
            Add(nameof(StockCount), "Stav skladu nesmí být záporný.");

        if (PohodaId is { Length: > 64 })
            Add(nameof(PohodaId), "PohodaId může mít nejvýše 64 znaků.");

        if (!string.IsNullOrEmpty(EslMacAddress) && !MacRegex().IsMatch(EslMacAddress))
            Add(nameof(EslMacAddress), "MAC adresa musí být ve tvaru AA:BB:CC:DD:EE:FF.");

        return errors;
    }

    public void ApplyTo(Product product)
    {
        product.Ean = Ean;
        product.Name = Name.Trim();
        product.BasePrice = BasePrice;
        product.NightPrice = NightPrice;
        product.ActionPrice = ActionPrice;
        product.ActionValidUntil = ActionValidUntil;
        product.BonusPointsReward = BonusPointsReward;
        product.WeightGrams = WeightGrams;
        product.ToleranceGrams = ToleranceGrams ?? Product.DefaultToleranceGrams;
        product.StockCount = StockCount;
        product.IsAgeRestricted = IsAgeRestricted;
        product.SupplierId = SupplierId;
        product.PohodaId = string.IsNullOrWhiteSpace(PohodaId) ? null : PohodaId.Trim();
        product.EslMacAddress = string.IsNullOrWhiteSpace(EslMacAddress) ? null : EslMacAddress.ToUpperInvariant();
    }

    [GeneratedRegex("^[0-9A-Fa-f]{2}(:[0-9A-Fa-f]{2}){5}$")]
    private static partial Regex MacRegex();
}
