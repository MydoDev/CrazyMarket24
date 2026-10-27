namespace CrazyMarket.Api.Abstractions;

/// <summary>Ovládání el. zámku vstupních dveří a uvítacího hlasového výstupu (TTS).</summary>
public interface IDoorController
{
    Task UnlockDoorAsync(int durationSeconds = 5, CancellationToken ct = default);
    Task PlayWelcomeAudioAsync(string message, CancellationToken ct = default);
}
