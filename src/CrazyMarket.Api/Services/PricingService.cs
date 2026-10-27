using CrazyMarket.Api.Domain.Entities;

namespace CrazyMarket.Api.Services;

public enum ActivePriceTier
{
    Base,
    Night,
    Action
}

/// <summary>
/// Výpočet aktuálně platné prodejní ceny produktu podle akce a denní/noční doby (22:00–05:00)
/// nebo manuálního celoplošného přepnutí Den / Noc z manažerského panelu (/admin).
/// </summary>
public class PricingService(TimeProvider timeProvider)
{
    private static readonly TimeOnly NightStart = new(22, 0);
    private static readonly TimeOnly NightEnd = new(5, 0);

    private bool? _nightModeOverride;

    /// <summary>Pokud je nastaveno z /admin, vynucuje denní (false) nebo noční (true) ceník.</summary>
    public bool? NightModeOverride => _nightModeOverride;

    public void SetNightModeOverride(bool? forceNight) => _nightModeOverride = forceNight;

    /// <summary>
    /// Vrátí aktivní cenu produktu:
    /// 1. <see cref="Product.ActionPrice"/> (pokud je zadána a <see cref="Product.ActionValidUntil"/> je v budoucnu nebo není omezena),
    /// 2. <see cref="Product.NightPrice"/> v okně 22:00–05:00 lokálního času (nebo při zapnutém nočním režimu),
    /// 3. <see cref="Product.BasePrice"/>.
    /// </summary>
    public decimal GetActivePrice(Product product) => EvaluatePrice(product).Price;

    /// <summary>Vrátí aktivní cenu i typ cenového tarifu (pro vykreslení pruhu na E-ink cenovce a účtenku).</summary>
    public (decimal Price, ActivePriceTier Tier) EvaluatePrice(Product product)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        if (product.ActionPrice is > 0m &&
            (product.ActionValidUntil is null || product.ActionValidUntil.Value > utcNow))
        {
            return (product.ActionPrice.Value, ActivePriceTier.Action);
        }

        if (IsNightTime())
        {
            return (product.NightPrice, ActivePriceTier.Night);
        }

        return (product.BasePrice, ActivePriceTier.Base);
    }

    public bool IsNightTime()
    {
        if (_nightModeOverride is { } forced)
            return forced;

        var localTime = TimeOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        return localTime >= NightStart || localTime < NightEnd;
    }
}
