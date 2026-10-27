using System.Globalization;
using System.IO.Ports;
using System.Text.RegularExpressions;
using CrazyMarket.Api.Abstractions;
using CrazyMarket.Api.Contracts;
using CrazyMarket.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CrazyMarket.Api.Services;

/// <summary>
/// Produkční služba pro čtení pokladní váhy přes sériový port (<see cref="SerialPort"/>) na pozadí.
/// Parsuje standardní váhový protokol (např. <c>ST,GS,+00250g</c>), aplikuje debounce 400 ms pro ustálení
/// a vyvolá <see cref="OnWeightStabilized"/> + notifikuje kiosek přes SignalR <see cref="StoreHub"/>.
/// Pokud fyzický COM port neexistuje, vypíše warning a běží dál (připravena na DEV trigger).
/// </summary>
public sealed partial class SerialPortScaleService(
    IConfiguration configuration,
    IHubContext<StoreHub, IStoreHubClient> hub,
    ILogger<SerialPortScaleService> logger) : BackgroundService, IScaleService
{
    public const int DebounceMs = 400;

    private readonly object _debounceLock = new();
    private CancellationTokenSource? _debounceCts;
    private volatile int _currentWeightGrams;
    private SerialPort? _serialPort;

    public event Func<int, Task>? OnWeightStabilized;

    public int CurrentWeightGrams => _currentWeightGrams;

    public Task<int> GetCurrentWeightGramsAsync(CancellationToken ct = default) =>
        Task.FromResult(_currentWeightGrams);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var portName = configuration.GetValue<string>("Scale:PortName") ?? "COM3";
        var baudRate = configuration.GetValue<int?>("Scale:BaudRate") ?? 9600;

        try
        {
            var availablePorts = SerialPort.GetPortNames();
            if (!availablePorts.Contains(portName, StringComparer.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Fyzický sériový port váhy '{PortName}' (BaudRate {BaudRate}) nebyl nalezen. " +
                    "SerialPortScaleService běží v pohotovostním režimu a čeká na připojení portu nebo DEV trigger.",
                    portName, baudRate);
            }
            else
            {
                _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                {
                    NewLine = "\r\n",
                    ReadTimeout = 1000,
                    WriteTimeout = 1000
                };

                _serialPort.DataReceived += OnSerialDataReceived;
                _serialPort.Open();
                logger.LogInformation("Sériový port váhy '{PortName}' ({BaudRate} Bd) úspěšně otevřen.", portName, baudRate);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Nelze otevřít sériový port váhy '{PortName}' ({Message}). Služba běží dál a čeká na DEV trigger.",
                portName, ex.Message);
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normální ukončení při vypnutí aplikace.
        }
    }

    private void OnSerialDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_serialPort is not { IsOpen: true })
            return;

        try
        {
            while (_serialPort.BytesToRead > 0)
            {
                var line = _serialPort.ReadLine();
                if (TryParseWeightFrame(line, out var grams))
                {
                    ScheduleDebouncedStabilization(grams, rawFrame: line.Trim(), applyDebounce: true);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug("Chyba při čtení rámce ze sériového portu váhy: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Parsuje standardní rámec váhy (např. <c>ST,GS,+00250g</c>, <c>US,GS,+00100g</c> nebo <c>+00250 g</c>).
    /// </summary>
    public static bool TryParseWeightFrame(string? rawFrame, out int grams)
    {
        grams = 0;
        if (string.IsNullOrWhiteSpace(rawFrame))
            return false;

        var match = ScaleProtocolRegex().Match(rawFrame);
        if (!match.Success)
            return false;

        if (!int.TryParse(match.Groups["weight"].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
            return false;

        grams = Math.Max(0, parsed);
        return true;
    }

    public async Task SimulateRawFrameOrWeightAsync(int grams, CancellationToken ct = default)
    {
        var normalized = Math.Max(0, grams);
        var frame = $"ST,GS,{normalized:+00000;-00000;+00000}g";
        logger.LogInformation("Váha (RS232 rámec '{Frame}'): probíhá ustálení ({DebounceMs} ms)...", frame, DebounceMs);

        await Task.Delay(DebounceMs, ct);
        await RaiseStabilizedAsync(normalized, frame);
    }

    private void ScheduleDebouncedStabilization(int grams, string rawFrame, bool applyDebounce)
    {
        CancellationTokenSource cts;
        lock (_debounceLock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            cts = _debounceCts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (applyDebounce)
                    await Task.Delay(DebounceMs, cts.Token);

                await RaiseStabilizedAsync(grams, rawFrame);
            }
            catch (OperationCanceledException)
            {
                // Přišla nová hodnota před uplynutím 400 ms debounce okna.
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Chyba při zpracování ustálené váhy ({Grams} g).", grams);
            }
        });
    }

    private async Task RaiseStabilizedAsync(int grams, string rawFrame)
    {
        _currentWeightGrams = grams;
        logger.LogInformation("Váha ustálena na {Grams} g (rámec: {Frame}).", grams, rawFrame);

        await hub.Clients.All.ScaleWeightUpdated(new ScaleWeightEvent(grams, rawFrame, DateTime.UtcNow));

        var handlers = OnWeightStabilized;
        if (handlers is not null)
        {
            foreach (var handler in handlers.GetInvocationList().Cast<Func<int, Task>>())
            {
                await handler(grams);
            }
        }
    }

    public override void Dispose()
    {
        lock (_debounceLock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
        }

        if (_serialPort is not null)
        {
            try
            {
                if (_serialPort.IsOpen)
                    _serialPort.Close();
            }
            catch
            {
                // Ignorujeme při zavírání.
            }
            _serialPort.Dispose();
        }

        base.Dispose();
    }

    // Podporuje např. "ST,GS,+00250g", "ST,NT,+01200 g", "+00250g" nebo "250"
    [GeneratedRegex(@"(?:(?:ST|US),(?:GS|NT),)?\s*(?<weight>[+-]?\d+)\s*(?:g|G)?", RegexOptions.Compiled)]
    private static partial Regex ScaleProtocolRegex();
}
