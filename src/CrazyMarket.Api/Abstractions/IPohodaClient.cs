using CrazyMarket.Api.Domain.Entities;

namespace CrazyMarket.Api.Abstractions;

public record PohodaExportResult(
    string FilePath,
    string XmlContent,
    bool SentToMServer,
    string? MServerResponse);

/// <summary>Komunikace s účetním systémem Stormware POHODA (mServer + lokální XML archiv v Windows-1250).</summary>
public interface IPohodaClient
{
    Task<PohodaExportResult> ExportOrderToPohodaXmlAsync(Order order, CancellationToken ct = default);
    Task<PohodaExportResult> ExportStockDeliveryToPohodaXmlAsync(StockDelivery delivery, CancellationToken ct = default);
}
