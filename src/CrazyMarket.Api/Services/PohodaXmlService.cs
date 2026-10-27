using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CrazyMarket.Api.Abstractions;
using CrazyMarket.Api.Domain.Entities;

namespace CrazyMarket.Api.Services;

/// <summary>
/// Produkční generátor a odesílatel XML dokladů pro Stormware POHODA mServer v kódování Windows-1250.
/// Podporuje export prodejky (<c>pro:prodejka</c> pro <see cref="Order"/>) a příjemky (<c>pri:prijemka</c> pro <see cref="StockDelivery"/>).
/// Každý vygenerovaný doklad vždy uloží do lokální složky <c>/pohoda-export/</c> a odešle na <c>Pohoda:MServerUrl</c>.
/// </summary>
public sealed class PohodaXmlService : IPohodaClient
{
    private static readonly XNamespace DatNs = "http://www.stormware.cz/schema/version_2/data.xsd";
    private static readonly XNamespace ProNs = "http://www.stormware.cz/schema/version_2/prodejka.xsd";
    private static readonly XNamespace PriNs = "http://www.stormware.cz/schema/version_2/prijemka.xsd";
    private static readonly XNamespace TypNs = "http://www.stormware.cz/schema/version_2/type.xsd";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<PohodaXmlService> _logger;
    private readonly Encoding _win1250;

    static PohodaXmlService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public PohodaXmlService(
        HttpClient httpClient,
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<PohodaXmlService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _env = env;
        _logger = logger;
        _win1250 = Encoding.GetEncoding(1250);
    }

    public async Task<PohodaExportResult> ExportOrderToPohodaXmlAsync(Order order, CancellationToken ct = default)
    {
        var ico = _configuration.GetValue<string>("Pohoda:Ico") ?? "12345678";
        var docDate = (order.PaidAt ?? order.CreatedAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var packId = $"CM24-ORD-{order.Id:N}"[..28];

        var doc = new XDocument(
            new XDeclaration("1.0", "Windows-1250", null),
            new XElement(DatNs + "dataPack",
                new XAttribute("version", "2.0"),
                new XAttribute("id", packId),
                new XAttribute("ico", ico),
                new XAttribute("application", "CrazyMarket24"),
                new XAttribute("note", $"Export prodejky CrazyMarket 24 ({order.Id})"),
                new XAttribute(XNamespace.Xmlns + "dat", DatNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "pro", ProNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "typ", TypNs.NamespaceName),
                new XElement(DatNs + "dataPackItem",
                    new XAttribute("version", "2.0"),
                    new XAttribute("id", $"PRO-{order.Id.ToString("N")[..12]}"),
                    new XElement(ProNs + "prodejka",
                        new XAttribute("version", "2.0"),
                        new XElement(ProNs + "prodejkaHeader",
                            new XElement(ProNs + "prodejkaType", "saleVoucher"),
                            new XElement(ProNs + "date", docDate),
                            new XElement(ProNs + "accounting",
                                new XElement(TypNs + "ids", "3Fv")),
                            new XElement(ProNs + "text", $"Samoobslužný nákup CrazyMarket 24 {(order.IsNightPricing ? "(noční tarif)" : "(denní tarif)")}"),
                            new XElement(ProNs + "paymentType",
                                new XElement(TypNs + "paymentType", "creditcard")),
                            new XElement(ProNs + "note", $"OrderId={order.Id}; PaymentRef={order.PaymentReference ?? "N/A"}")
                        ),
                        new XElement(ProNs + "prodejkaDetail",
                            order.Items.Select(item => new XElement(ProNs + "prodejkaItem",
                                new XElement(ProNs + "text", item.ProductName),
                                new XElement(ProNs + "quantity", item.Quantity.ToString(CultureInfo.InvariantCulture)),
                                new XElement(ProNs + "unit", "ks"),
                                new XElement(ProNs + "payVAT", "true"),
                                new XElement(ProNs + "rateVAT", "high"),
                                new XElement(ProNs + "homeCurrency",
                                    new XElement(TypNs + "unitPrice", item.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture))),
                                new XElement(ProNs + "stockItem",
                                    new XElement(TypNs + "stockItem",
                                        !string.IsNullOrWhiteSpace(item.Product?.PohodaId)
                                            ? new XElement(TypNs + "ids", item.Product.PohodaId)
                                            : new XElement(TypNs + "EAN", item.Ean)))
                            ))
                        ),
                        new XElement(ProNs + "prodejkaSummary",
                            new XElement(ProNs + "roundingDocument", "none")
                        )
                    )
                )
            )
        );

        var fileName = $"prodejka_{order.CreatedAt:yyyyMMdd_HHmmss}_{order.Id:N}.xml";
        return await SaveAndSendAsync(doc, fileName, ct);
    }

    public async Task<PohodaExportResult> ExportStockDeliveryToPohodaXmlAsync(StockDelivery delivery, CancellationToken ct = default)
    {
        var ico = _configuration.GetValue<string>("Pohoda:Ico") ?? "12345678";
        var docDate = delivery.DeliveredAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var packId = $"CM24-PRI-{delivery.Id:N}"[..28];

        var doc = new XDocument(
            new XDeclaration("1.0", "Windows-1250", null),
            new XElement(DatNs + "dataPack",
                new XAttribute("version", "2.0"),
                new XAttribute("id", packId),
                new XAttribute("ico", ico),
                new XAttribute("application", "CrazyMarket24"),
                new XAttribute("note", $"Export příjemky od dodavatele {delivery.Supplier.CompanyName}"),
                new XAttribute(XNamespace.Xmlns + "dat", DatNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "pri", PriNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "typ", TypNs.NamespaceName),
                new XElement(DatNs + "dataPackItem",
                    new XAttribute("version", "2.0"),
                    new XAttribute("id", $"PRI-{delivery.Id.ToString("N")[..12]}"),
                    new XElement(PriNs + "prijemka",
                        new XAttribute("version", "2.0"),
                        new XElement(PriNs + "prijemkaHeader",
                            new XElement(PriNs + "date", docDate),
                            new XElement(PriNs + "text", $"Závoz zboží: {delivery.Supplier.CompanyName}"),
                            new XElement(PriNs + "partnerIdentity",
                                new XElement(TypNs + "address",
                                    new XElement(TypNs + "company", delivery.Supplier.CompanyName),
                                    new XElement(TypNs + "ico", delivery.Supplier.Ico))),
                            new XElement(PriNs + "note", $"DeliveryId={delivery.Id}")
                        ),
                        new XElement(PriNs + "prijemkaDetail",
                            delivery.Items.Select(item => new XElement(PriNs + "prijemkaItem",
                                new XElement(PriNs + "text", item.Product.Name),
                                new XElement(PriNs + "quantity", item.Quantity.ToString(CultureInfo.InvariantCulture)),
                                new XElement(PriNs + "unit", "ks"),
                                new XElement(PriNs + "payVAT", "false"),
                                new XElement(PriNs + "rateVAT", "high"),
                                item.ExpirationDate is not null
                                    ? new XElement(PriNs + "expirationDate", item.ExpirationDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                                    : null,
                                new XElement(PriNs + "stockItem",
                                    new XElement(TypNs + "stockItem",
                                        !string.IsNullOrWhiteSpace(item.Product.PohodaId)
                                            ? new XElement(TypNs + "ids", item.Product.PohodaId)
                                            : new XElement(TypNs + "EAN", item.Product.Ean)))
                            ))
                        )
                    )
                )
            )
        );

        var fileName = $"prijemka_{delivery.DeliveredAt:yyyyMMdd_HHmmss}_{delivery.Id:N}.xml";
        return await SaveAndSendAsync(doc, fileName, ct);
    }

    private async Task<PohodaExportResult> SaveAndSendAsync(XDocument doc, string fileName, CancellationToken ct)
    {
        byte[] xmlBytes;
        using (var ms = new MemoryStream())
        {
            var settings = new XmlWriterSettings
            {
                Encoding = _win1250,
                Indent = true,
                Async = true
            };

            await using (var writer = XmlWriter.Create(ms, settings))
            {
                await doc.SaveAsync(writer, ct);
            }

            xmlBytes = ms.ToArray();
        }

        var xmlString = _win1250.GetString(xmlBytes);

        // Vždy uložíme do lokální složky /pohoda-export/ v kořeni repozitáře (nebo dle konfigurace)
        var configuredDir = _configuration.GetValue<string>("Pohoda:ExportDirectory");
        var exportDir = !string.IsNullOrWhiteSpace(configuredDir)
            ? Path.GetFullPath(Path.Combine(_env.ContentRootPath, configuredDir))
            : Path.GetFullPath(Path.Combine(_env.ContentRootPath, "..", "..", "pohoda-export"));

        Directory.CreateDirectory(exportDir);
        var fullPath = Path.Combine(exportDir, fileName);
        await File.WriteAllBytesAsync(fullPath, xmlBytes, ct);
        _logger.LogInformation("POHODA XML uloženo (Windows-1250): {FilePath}", fullPath);

        // Pokus o odeslání na živý POHODA mServer
        var mServerUrl = _configuration.GetValue<string>("Pohoda:MServerUrl");
        var stwAuth = _configuration.GetValue<string>("Pohoda:StwAuthorization") ?? "Basic YWRtaW46";

        if (string.IsNullOrWhiteSpace(mServerUrl))
        {
            return new PohodaExportResult(fullPath, xmlString, SentToMServer: false, MServerResponse: null);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, mServerUrl);
            request.Headers.TryAddWithoutValidation("STW-Authorization", stwAuth);
            request.Content = new ByteArrayContent(xmlBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/xml")
            {
                CharSet = "Windows-1250"
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(4));

            var response = await _httpClient.SendAsync(request, cts.Token);
            var responseBytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
            var responseText = _win1250.GetString(responseBytes);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("POHODA mServer ({Url}) přijal doklad {FileName}.", mServerUrl, fileName);
                return new PohodaExportResult(fullPath, xmlString, SentToMServer: true, responseText);
            }

            _logger.LogWarning("POHODA mServer ({Url}) vrátil HTTP {Status} pro {FileName}.",
                mServerUrl, (int)response.StatusCode, fileName);
            return new PohodaExportResult(fullPath, xmlString, SentToMServer: false, responseText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "POHODA mServer ({Url}) není dostupný ({Message}). XML doklad je bezpečně uložen v {FilePath}.",
                mServerUrl, ex.Message, fullPath);
            return new PohodaExportResult(fullPath, xmlString, SentToMServer: false, MServerResponse: ex.Message);
        }
    }
}
