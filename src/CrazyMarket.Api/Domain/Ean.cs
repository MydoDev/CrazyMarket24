namespace CrazyMarket.Api.Domain;

/// <summary>Validace a dopočet kontrolní číslice čárových kódů EAN-8 / EAN-13 (GS1 mod 10).</summary>
public static class Ean
{
    /// <summary>True, pokud je řetězec platný EAN-8 nebo EAN-13 se správnou kontrolní číslicí.</summary>
    public static bool IsValid(string? ean)
    {
        if (ean is null || (ean.Length != 8 && ean.Length != 13) || !ean.All(char.IsAsciiDigit))
            return false;

        return ComputeCheckDigit(ean.AsSpan(0, ean.Length - 1)) == ean[^1] - '0';
    }

    /// <summary>K 7 nebo 12 číslicím připojí kontrolní číslici.</summary>
    public static string WithCheckDigit(string digitsWithoutCheck)
    {
        if ((digitsWithoutCheck.Length != 7 && digitsWithoutCheck.Length != 12) || !digitsWithoutCheck.All(char.IsAsciiDigit))
            throw new ArgumentException("Očekáváno 7 nebo 12 číslic.", nameof(digitsWithoutCheck));

        return digitsWithoutCheck + ComputeCheckDigit(digitsWithoutCheck);
    }

    private static int ComputeCheckDigit(ReadOnlySpan<char> payload)
    {
        // Váhy 3,1,3,1… se počítají zprava (číslice nejblíž kontrolní má váhu 3).
        var sum = 0;
        for (var i = 0; i < payload.Length; i++)
        {
            var digit = payload[payload.Length - 1 - i] - '0';
            sum += i % 2 == 0 ? digit * 3 : digit;
        }
        return (10 - sum % 10) % 10;
    }
}
