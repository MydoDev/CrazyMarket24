using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using CrazyMarket.Api.Abstractions;
using CrazyMarket.Api.Domain.Entities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace CrazyMarket.Api.Services;

/// <summary>
/// Produkční generátor 1-bitových černobílých E-ink cenovek (296×128 px) pomocí SixLabors.ImageSharp.
/// Vykreslí horní inverzní pruh ("AKCE" / "NOČNÍ CENA"), název produktu, skutečný EAN čárový kód a cenu.
/// Uloží Base64 do <see cref="Product.LastEslImageBase64"/> a odešle multipart POST na E-ink bránu.
/// </summary>
public sealed class EslImageGenerator(
    HttpClient httpClient,
    PricingService pricingService,
    IConfiguration configuration,
    ILogger<EslImageGenerator> logger) : IEslGateway
{
    public const int Width = 296;
    public const int Height = 128;

    private const byte White = 255;
    private const byte Black = 0;

    // GS1 EAN-13 kódovací tabulky (L, G, R) a parita podle první číslice.
    private static readonly string[] LeftOdd =
        ["0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011"];
    private static readonly string[] LeftEven =
        ["0100111", "0110011", "0011011", "0100001", "0011101", "0111001", "0000101", "0010001", "0001001", "0010111"];
    private static readonly string[] RightCodes =
        ["1110010", "1100110", "1101100", "1000010", "1011100", "1001110", "1010000", "1000100", "1001000", "1110100"];
    private static readonly string[] FirstDigitParity =
        ["LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG", "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL"];

    public async Task<EslRenderResult> RenderAndPushTagAsync(Product product, decimal activePrice, CancellationToken ct = default)
    {
        var (_, evaluatedTier) = pricingService.EvaluatePrice(product);

        // Určení, zda zobrazit horní černý pruh s inverzním nápisem:
        string? bannerText = null;
        if (product.ActionPrice is > 0m && activePrice == product.ActionPrice.Value && evaluatedTier == ActivePriceTier.Action)
        {
            bannerText = "AKCE";
        }
        else if (evaluatedTier == ActivePriceTier.Night || (activePrice == product.NightPrice && activePrice != product.BasePrice))
        {
            bannerText = "NOCNI CENA";
        }

        byte[] pngBytes;
        using (var image = new Image<L8>(Width, Height, new L8(White)))
        {
            RenderTagLayout(image, product, activePrice, bannerText);

            using var ms = new MemoryStream();
            var encoder = new PngEncoder
            {
                ColorType = PngColorType.Grayscale,
                BitDepth = PngBitDepth.Bit1
            };
            await image.SaveAsPngAsync(ms, encoder, ct);
            pngBytes = ms.ToArray();
        }

        var base64 = Convert.ToBase64String(pngBytes);
        product.LastEslImageBase64 = base64;

        var pushed = await PushToGatewaySafeAsync(product, pngBytes, ct);
        var displayBanner = bannerText == "NOCNI CENA" ? "NOČNÍ CENA" : bannerText;

        return new EslRenderResult(base64, pngBytes, activePrice, displayBanner, pushed);
    }

    private static void RenderTagLayout(Image<L8> img, Product product, decimal activePrice, string? bannerText)
    {
        // Vnější 1px černý rámeček
        DrawRectBorder(img, 0, 0, Width, Height, Black);

        var topOffset = 4;

        // 1. Horní inverzní pruh ("AKCE" / "NOČNÍ CENA")
        if (bannerText is not null)
        {
            FillRect(img, 0, 0, Width, 22, Black);
            var label = bannerText == "NOCNI CENA" ? "NOCNI CENA" : bannerText;
            var textW = MeasureTextWidth(label, scale: 2);
            var textX = (Width - textW) / 2;
            DrawText(img, label, textX, 4, scale: 2, color: White);

            // Diakritický háček nad Č a čárka nad Í pro „NOČNÍ CENA“
            if (bannerText == "NOCNI CENA")
            {
                // 3. znak je 'C' -> Č, 5. znak je 'I' -> Í
                var cX = textX + 2 * 12;
                SetPixelBlock(img, cX + 2, 1, 2, 2, White);
                SetPixelBlock(img, cX + 6, 1, 2, 2, White);
                SetPixelBlock(img, cX + 4, 2, 2, 2, White);

                var iX = textX + 4 * 12;
                SetPixelBlock(img, iX + 4, 1, 2, 2, White);
                SetPixelBlock(img, iX + 2, 2, 2, 1, White);
            }

            if (product.IsAgeRestricted)
            {
                DrawText(img, "18+", Width - 42, 4, scale: 2, color: White);
            }

            topOffset = 26;
        }
        else if (product.IsAgeRestricted)
        {
            FillRect(img, Width - 44, 4, 40, 18, Black);
            DrawText(img, "18+", Width - 40, 6, scale: 2, color: White);
        }

        // 2. Název produktu (zalamování na max 2 řádky, normalizace diakritiky pro 5x7 font)
        var normalizedName = NormalizeAscii(product.Name).ToUpperInvariant();
        var maxCharsPerLine = product.IsAgeRestricted && bannerText is null ? 19 : 23;
        var lines = WrapText(normalizedName, maxCharsPerLine, maxLines: 2);

        for (var i = 0; i < lines.Count; i++)
        {
            DrawText(img, lines[i], 8, topOffset + i * 18, scale: 2, color: Black);
        }

        // Vodorovná dělící linka
        var dividerY = 64;
        FillRect(img, 6, dividerY, Width - 12, 1, Black);

        // 3. Levá spodní část: čárový kód EAN + textové číslo EAN
        DrawBarcode(img, product.Ean, x: 8, y: dividerY + 6, barHeight: 38);
        DrawText(img, product.Ean, 12, dividerY + 47, scale: 1, color: Black);

        // 4. Pravá spodní část: velká cena v Kč
        var priceWhole = (int)decimal.Truncate(activePrice);
        var priceDecimals = (int)((activePrice - priceWhole) * 100);
        var wholeText = priceWhole.ToString(CultureInfo.InvariantCulture);
        var decText = $",{priceDecimals:00}";

        var wholeWidth = MeasureTextWidth(wholeText, scale: 4);
        var decWidth = MeasureTextWidth(decText, scale: 2);
        var kcWidth = MeasureTextWidth("KC", scale: 2);

        var rightMargin = Width - 8;
        var priceStartX = Math.Max(118, rightMargin - (wholeWidth + decWidth + 4));

        DrawText(img, wholeText, priceStartX, dividerY + 10, scale: 4, color: Black);
        DrawText(img, decText, priceStartX + wholeWidth + 2, dividerY + 10, scale: 2, color: Black);
        DrawText(img, "KC", priceStartX + wholeWidth + 6, dividerY + 28, scale: 2, color: Black);

        // Pokud je v akci, vypíšeme původní přeškrtnutou cenu
        if (bannerText == "AKCE")
        {
            var orig = $"{product.BasePrice:0.00} KC";
            var origW = MeasureTextWidth(orig, scale: 1);
            var origX = rightMargin - origW;
            var origY = dividerY + 48;
            DrawText(img, orig, origX, origY, scale: 1, color: Black);
            FillRect(img, origX - 1, origY + 3, origW + 2, 1, Black);
        }
    }

    private async Task<bool> PushToGatewaySafeAsync(Product product, byte[] pngBytes, CancellationToken ct)
    {
        var gatewayUrl = configuration.GetValue<string>("EslGateway:Url");
        if (string.IsNullOrWhiteSpace(gatewayUrl))
            return false;

        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(product.EslMacAddress ?? "UNASSIGNED"), "mac");
            content.Add(new StringContent(product.Ean), "ean");

            var imageContent = new ByteArrayContent(pngBytes);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(imageContent, "image", $"{product.Ean}.png");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var response = await httpClient.PostAsync(gatewayUrl, content, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("E-ink cenovka {Mac} ({Ean}) odeslána na bránu {Url}.",
                    product.EslMacAddress, product.Ean, gatewayUrl);
                return true;
            }

            logger.LogWarning("E-ink brána {Url} vrátila HTTP {StatusCode} pro {Mac}.",
                gatewayUrl, (int)response.StatusCode, product.EslMacAddress);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "E-ink brána {Url} není dostupná ({Message}). PNG náhled pro {Ean} je uložen v DB (LastEslImageBase64).",
                gatewayUrl, ex.Message, product.Ean);
            return false;
        }
    }

    private static void DrawBarcode(Image<L8> img, string ean, int x, int y, int barHeight)
    {
        var bits = EncodeEan13Bits(ean);
        for (var i = 0; i < bits.Length; i++)
        {
            if (bits[i] == '1')
            {
                // Vodící čáry (začátek, střed, konec) jsou o 4 px delší
                var isGuard = i < 3 || (i >= 45 && i < 50) || i >= 92;
                FillRect(img, x + i, y, 1, isGuard ? barHeight + 4 : barHeight, Black);
            }
        }
    }

    private static string EncodeEan13Bits(string ean)
    {
        var padded = (ean ?? string.Empty).PadLeft(13, '0');
        if (padded.Length > 13)
            padded = padded[..13];

        var sb = new StringBuilder(95);
        sb.Append("101"); // Start guard

        var firstDigit = char.IsAsciiDigit(padded[0]) ? padded[0] - '0' : 0;
        var parity = FirstDigitParity[firstDigit];

        for (var i = 1; i <= 6; i++)
        {
            var d = char.IsAsciiDigit(padded[i]) ? padded[i] - '0' : 0;
            sb.Append(parity[i - 1] == 'L' ? LeftOdd[d] : LeftEven[d]);
        }

        sb.Append("01010"); // Center guard

        for (var i = 7; i <= 12; i++)
        {
            var d = char.IsAsciiDigit(padded[i]) ? padded[i] - '0' : 0;
            sb.Append(RightCodes[d]);
        }

        sb.Append("101"); // End guard
        return sb.ToString();
    }

    private static List<string> WrapText(string text, int maxCharsPerLine, int maxLines)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = new StringBuilder();

        foreach (var word in words)
        {
            if (current.Length == 0)
            {
                current.Append(word.Length > maxCharsPerLine ? word[..maxCharsPerLine] : word);
            }
            else if (current.Length + 1 + word.Length <= maxCharsPerLine)
            {
                current.Append(' ').Append(word);
            }
            else
            {
                lines.Add(current.ToString());
                if (lines.Count >= maxLines)
                    return lines;
                current.Clear();
                current.Append(word.Length > maxCharsPerLine ? word[..maxCharsPerLine] : word);
            }
        }

        if (current.Length > 0 && lines.Count < maxLines)
            lines.Add(current.ToString());

        return lines;
    }

    private static string NormalizeAscii(string input)
    {
        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static int MeasureTextWidth(string text, int scale) =>
        text.Length == 0 ? 0 : text.Length * (6 * scale) - scale;

    private static void DrawText(Image<L8> img, string text, int x, int y, int scale, byte color)
    {
        var cursorX = x;
        foreach (var ch in text)
        {
            var glyph = GetGlyph5x7(char.ToUpperInvariant(ch));
            for (var row = 0; row < 7; row++)
            {
                var rowBits = glyph[row];
                for (var col = 0; col < 5; col++)
                {
                    if (((rowBits >> (4 - col)) & 1) == 1)
                    {
                        SetPixelBlock(img, cursorX + col * scale, y + row * scale, scale, scale, color);
                    }
                }
            }
            cursorX += 6 * scale;
        }
    }

    private static void DrawRectBorder(Image<L8> img, int x, int y, int w, int h, byte color)
    {
        FillRect(img, x, y, w, 1, color);
        FillRect(img, x, y + h - 1, w, 1, color);
        FillRect(img, x, y, 1, h, color);
        FillRect(img, x + w - 1, y, 1, h, color);
    }

    private static void FillRect(Image<L8> img, int x, int y, int w, int h, byte color) =>
        SetPixelBlock(img, x, y, w, h, color);

    private static void SetPixelBlock(Image<L8> img, int x, int y, int w, int h, byte color)
    {
        var xEnd = Math.Min(img.Width, x + w);
        var yEnd = Math.Min(img.Height, y + h);
        var xStart = Math.Max(0, x);
        var yStart = Math.Max(0, y);

        for (var py = yStart; py < yEnd; py++)
        {
            for (var px = xStart; px < xEnd; px++)
            {
                img[px, py] = new L8(color);
            }
        }
    }

    /// <summary>Vestavěný 5×7 pixelový font pro ostré 1-bit E-ink vykreslení bez závislosti na OS fontech.</summary>
    private static byte[] GetGlyph5x7(char c) => c switch
    {
        'A' => [0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001],
        'B' => [0b11110, 0b10001, 0b10001, 0b11110, 0b10001, 0b10001, 0b11110],
        'C' => [0b01110, 0b10001, 0b10000, 0b10000, 0b10000, 0b10001, 0b01110],
        'D' => [0b11100, 0b10010, 0b10001, 0b10001, 0b10001, 0b10010, 0b11100],
        'E' => [0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b11111],
        'F' => [0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b10000],
        'G' => [0b01110, 0b10001, 0b10000, 0b10111, 0b10001, 0b10001, 0b01110],
        'H' => [0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001],
        'I' => [0b01110, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110],
        'J' => [0b00111, 0b00010, 0b00010, 0b00010, 0b10010, 0b10010, 0b01100],
        'K' => [0b10001, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010, 0b10001],
        'L' => [0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b11111],
        'M' => [0b10001, 0b11011, 0b10101, 0b10101, 0b10001, 0b10001, 0b10001],
        'N' => [0b10001, 0b11001, 0b10101, 0b10011, 0b10001, 0b10001, 0b10001],
        'O' => [0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110],
        'P' => [0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000, 0b10000],
        'Q' => [0b01110, 0b10001, 0b10001, 0b10001, 0b10101, 0b10010, 0b01101],
        'R' => [0b11110, 0b10001, 0b10001, 0b11110, 0b10100, 0b10010, 0b10001],
        'S' => [0b01111, 0b10000, 0b10000, 0b01110, 0b00001, 0b00001, 0b11110],
        'T' => [0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100],
        'U' => [0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110],
        'V' => [0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01010, 0b00100],
        'W' => [0b10001, 0b10001, 0b10001, 0b10101, 0b10101, 0b10101, 0b01010],
        'X' => [0b10001, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001, 0b10001],
        'Y' => [0b10001, 0b10001, 0b01010, 0b00100, 0b00100, 0b00100, 0b00100],
        'Z' => [0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b10000, 0b11111],
        '0' => [0b01110, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b01110],
        '1' => [0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110],
        '2' => [0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111],
        '3' => [0b11110, 0b00001, 0b00001, 0b01110, 0b00001, 0b00001, 0b11110],
        '4' => [0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010],
        '5' => [0b11111, 0b10000, 0b11110, 0b00001, 0b00001, 0b10001, 0b01110],
        '6' => [0b00110, 0b01000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110],
        '7' => [0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000],
        '8' => [0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110],
        '9' => [0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00010, 0b01100],
        '+' => [0b00000, 0b00100, 0b00100, 0b11111, 0b00100, 0b00100, 0b00000],
        '-' => [0b00000, 0b00000, 0b00000, 0b11111, 0b00000, 0b00000, 0b00000],
        '.' => [0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b01100, 0b01100],
        ',' => [0b00000, 0b00000, 0b00000, 0b00000, 0b00110, 0b00100, 0b01000],
        '%' => [0b11001, 0b11010, 0b00100, 0b00100, 0b01011, 0b10011, 0b00000],
        '°' => [0b00110, 0b01001, 0b01001, 0b00110, 0b00000, 0b00000, 0b00000],
        '/' => [0b00001, 0b00010, 0b00100, 0b00100, 0b01000, 0b10000, 0b00000],
        ':' => [0b00000, 0b01100, 0b01100, 0b00000, 0b01100, 0b01100, 0b00000],
        _ => [0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000],
    };
}
