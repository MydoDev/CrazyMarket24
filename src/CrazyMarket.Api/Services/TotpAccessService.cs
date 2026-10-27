using OtpNet;

namespace CrazyMarket.Api.Services;

public enum AccessActorType
{
    Customer,
    Supplier
}

public record ParsedEntryQr(
    AccessActorType ActorType,
    Guid ActorId,
    string TotpCode);

public record GeneratedEntryToken(
    string QrPayload,
    string TotpCode,
    int RemainingSeconds,
    DateTime ExpiresAtUtc);

/// <summary>
/// Generování a ověřování dynamických 6místných TOTP kódů (RFC 6238, 30s okno) pomocí Otp.NET
/// pro vstup zákazníků (<c>CM24:CUSTOMER:{Id}:{TotpCode}</c>) i zásobování (<c>CM24:SUPPLIER:{Id}:{TotpCode}</c>).
/// </summary>
public sealed class TotpAccessService(TimeProvider timeProvider)
{
    public const string Prefix = "CM24";
    public const int StepSeconds = 30;
    public const int TotpSize = 6;

    /// <summary>Vygeneruje aktuální 6místný TOTP kód a celý QR řetězec <c>CM24:{Type}:{Id}:{TotpCode}</c>.</summary>
    public GeneratedEntryToken GenerateToken(AccessActorType actorType, Guid actorId, string base32Secret)
    {
        var secretBytes = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(secretBytes, step: StepSeconds, totpSize: TotpSize);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var code = totp.ComputeTotp(utcNow);
        var remaining = totp.RemainingSeconds(utcNow);

        var typeSegment = actorType == AccessActorType.Supplier ? "SUPPLIER" : "CUSTOMER";
        var payload = $"{Prefix}:{typeSegment}:{actorId}:{code}";

        return new GeneratedEntryToken(
            QrPayload: payload,
            TotpCode: code,
            RemainingSeconds: remaining,
            ExpiresAtUtc: utcNow.AddSeconds(remaining));
    }

    /// <summary>Ověří platnost 6místného TOTP kódu s tolerancí ±1 časového okna (30 s) kvůli drobnému posunu hodin telefonu.</summary>
    public bool VerifyCode(string base32Secret, string totpCode)
    {
        if (string.IsNullOrWhiteSpace(base32Secret) || string.IsNullOrWhiteSpace(totpCode))
            return false;

        try
        {
            var secretBytes = Base32Encoding.ToBytes(base32Secret);
            var totp = new Totp(secretBytes, step: StepSeconds, totpSize: TotpSize);
            var utcNow = timeProvider.GetUtcNow().UtcDateTime;

            return totp.VerifyTotp(utcNow, totpCode.Trim(), out _, new VerificationWindow(previous: 1, future: 1));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Rozparsuje vstupní řetězec ze čtečky u dveří ve formátu <c>CM24:{Type}:{Id}:{TotpCode}</c>.</summary>
    public static bool TryParseEntryQr(string? rawQr, out ParsedEntryQr? parsed, out string? error)
    {
        parsed = null;
        error = null;

        if (string.IsNullOrWhiteSpace(rawQr))
        {
            error = "QR kód je prázdný.";
            return false;
        }

        var parts = rawQr.Trim().Split(':');
        if (parts.Length != 4 || !string.Equals(parts[0], Prefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "Neplatný formát QR kódu. Očekáváno 'CM24:{CUSTOMER|SUPPLIER}:{Guid}:{6místnýKód}'.";
            return false;
        }

        AccessActorType actorType;
        if (parts[1].Equals("CUSTOMER", StringComparison.OrdinalIgnoreCase) ||
            parts[1].Equals("CUST", StringComparison.OrdinalIgnoreCase))
        {
            actorType = AccessActorType.Customer;
        }
        else if (parts[1].Equals("SUPPLIER", StringComparison.OrdinalIgnoreCase) ||
                 parts[1].Equals("SUP", StringComparison.OrdinalIgnoreCase) ||
                 parts[1].Equals("VENDOR", StringComparison.OrdinalIgnoreCase))
        {
            actorType = AccessActorType.Supplier;
        }
        else
        {
            error = $"Neznámý typ subjektu '{parts[1]}'. Povolené hodnoty: CUSTOMER, SUPPLIER.";
            return false;
        }

        if (!Guid.TryParse(parts[2], out var actorId))
        {
            error = $"Neplatné ID subjektu '{parts[2]}' (očekáván GUID).";
            return false;
        }

        var code = parts[3].Trim();
        if (code.Length != TotpSize || !code.All(char.IsAsciiDigit))
        {
            error = "TOTP kód musí obsahovat přesně 6 číslic.";
            return false;
        }

        parsed = new ParsedEntryQr(actorType, actorId, code);
        return true;
    }
}
