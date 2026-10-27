using CrazyMarket.Api.Domain.Entities;
using CrazyMarket.Api.Domain.Enums;
using CrazyMarket.Api.Services;

namespace CrazyMarket.Api.Contracts;

public record ScanEntryRequest(string QrCode);

public record StoreSessionDto(
    Guid SessionId,
    AccessActorType ActorType,
    Guid ActorId,
    string DisplayName,
    bool IsAdult,
    int LoyaltyPoints,
    DateTime EnteredAtUtc,
    SessionState CurrentState);

public record ScanEntryResponse(
    bool Granted,
    string WelcomeMessage,
    StoreSessionDto Session);

public record SupplierDto(
    Guid Id,
    string CompanyName,
    string Ico,
    SupplierRole Role,
    bool AllowDynamicPricing,
    bool CanManageAllProducts,
    DateTime CreatedAt)
{
    public static SupplierDto From(Supplier s) =>
        new(s.Id, s.CompanyName, s.Ico, s.Role, s.AllowDynamicPricing, s.CanManageAllProducts, s.CreatedAt);
}

public record SetActionPriceRequest(
    decimal? ActionPrice,
    int? ValidDays);

public record SetActionPriceResponse(
    ProductDto Product,
    EslPreviewDto EslPreview);

public record CreateDeliveryItemRequest(
    int ProductId,
    int Quantity,
    DateOnly? ExpirationDate);

public record CreateStockDeliveryRequest(
    Guid SupplierId,
    List<CreateDeliveryItemRequest> Items);

public record StockDeliveryDto(
    Guid Id,
    Guid SupplierId,
    string SupplierName,
    SupplierRole SupplierRole,
    DateTime DeliveredAt,
    bool PohodaSynced,
    DateTime? PohodaSyncedAt,
    string? PohodaXmlPath,
    int TotalItemsCount);

public record EslPreviewDto(
    int ProductId,
    string Ean,
    string Name,
    string? EslMacAddress,
    decimal ActivePrice,
    string? BannerText,
    bool PushedToGateway,
    string Base64Png);

public record PohodaSyncItemDto(
    Guid DocumentId,
    string DocumentType,
    string PartnerOrCustomerName,
    DateTime CreatedAtUtc,
    decimal? TotalAmount,
    int ItemsCount,
    bool ExportedToDisk,
    string? FileName,
    string StatusText);

public record AdminDashboardDto(
    bool IsNightPricingActive,
    bool? NightModeOverride,
    int CurrentScaleWeightGrams,
    List<StoreSessionDto> ActiveSessions,
    List<ProductDto> Products,
    List<PohodaSyncItemDto> PohodaDocuments);
