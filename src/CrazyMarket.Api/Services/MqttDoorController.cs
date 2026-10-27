using System.Text.Json;
using CrazyMarket.Api.Abstractions;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace CrazyMarket.Api.Services;

/// <summary>
/// Produkční ovládání elektronického zámku dveří a TTS reproduktoru přes MQTT broker.
/// Pokud broker neběží nebo je nedostupný, služba zaloguje varování a nespadne.
/// </summary>
public sealed class MqttDoorController : IDoorController, IAsyncDisposable
{
    public const string DoorCmdTopic = "crazymarket/door/cmd";
    public const string AudioTtsTopic = "crazymarket/audio/tts";

    private readonly ILogger<MqttDoorController> _logger;
    private readonly string _host;
    private readonly int _port;
    private readonly IMqttClient _client;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    public MqttDoorController(IConfiguration configuration, ILogger<MqttDoorController> logger)
    {
        _logger = logger;
        _host = configuration.GetValue<string>("Mqtt:Host") ?? "localhost";
        _port = configuration.GetValue<int?>("Mqtt:Port") ?? 1883;

        var factory = new MqttFactory();
        _client = factory.CreateMqttClient();
    }

    public async Task UnlockDoorAsync(int durationSeconds = 5, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            action = "unlock",
            duration = durationSeconds
        });

        await PublishSafeAsync(DoorCmdTopic, payload, ct);
    }

    public async Task PlayWelcomeAudioAsync(string message, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            text = message,
            lang = "cs-CZ"
        });

        await PublishSafeAsync(AudioTtsTopic, payload, ct);
    }

    private async Task PublishSafeAsync(string topic, string payload, CancellationToken ct)
    {
        try
        {
            if (!await EnsureConnectedAsync(ct))
                return;

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _client.PublishAsync(message, ct);
            _logger.LogInformation("MQTT publikováno na {Topic}: {Payload}", topic, payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "MQTT publikování na {Topic} selhalo (broker {Host}:{Port} není dostupný). Payload: {Payload}",
                topic, _host, _port, payload);
        }
    }

    private async Task<bool> EnsureConnectedAsync(CancellationToken ct)
    {
        if (_client.IsConnected)
            return true;

        await _connectLock.WaitAsync(ct);
        try
        {
            if (_client.IsConnected)
                return true;

            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(_host, _port)
                .WithClientId($"crazymarket-api-{Environment.MachineName}-{Guid.NewGuid():N}"[..32])
                .WithTimeout(TimeSpan.FromSeconds(2))
                .WithCleanSession()
                .Build();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

            await _client.ConnectAsync(options, timeoutCts.Token);
            _logger.LogInformation("Připojeno k MQTT brokeru {Host}:{Port}.", _host, _port);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "MQTT broker {Host}:{Port} není dostupný ({Message}). Pokračuji v fallback režimu bez pádu.",
                _host, _port, ex.Message);
            return false;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_client.IsConnected)
                await _client.DisconnectAsync();
        }
        catch
        {
            // Ignorujeme při ukončování aplikace.
        }
        finally
        {
            _client.Dispose();
            _connectLock.Dispose();
        }
    }
}
