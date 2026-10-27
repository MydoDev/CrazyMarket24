namespace CrazyMarket.Api.Abstractions;

/// <summary>
/// Produkční rozhraní kontrolní váhy na samoobslužné pokladně (RS-232 / USB CDC).
/// Poskytuje okamžitou hmotnost v gramech a událost <see cref="OnWeightStabilized"/> po ustálení váhy.
/// </summary>
public interface IScaleService
{
    /// <summary>Událost vyvolaná po ustálení váhy (debounce 400 ms).</summary>
    event Func<int, Task>? OnWeightStabilized;

    /// <summary>Poslední ustálená hmotnost na váze v gramech.</summary>
    int CurrentWeightGrams { get; }

    Task<int> GetCurrentWeightGramsAsync(CancellationToken ct = default);

    /// <summary>Vývojový vstup pro simulaci přijetí rámce z váhy (pouze pro DevTestController).</summary>
    Task SimulateRawFrameOrWeightAsync(int grams, CancellationToken ct = default);
}
