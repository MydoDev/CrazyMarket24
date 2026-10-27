using CrazyMarket.Api.Services;

namespace CrazyMarket.Api.Contracts;

public enum CheckoutStatus
{
    /// <summary>Pokladna je volná, čeká na prvního zákazníka nebo první sken.</summary>
    Idle,

    /// <summary>Zákazník je identifikován a váha souhlasí – lze skenovat další zboží nebo přejít k platbě.</summary>
    Ready,

    /// <summary>Položka byla naskenována, čeká se na položení zboží na kontrolní váhu (skenování i platba jsou zablokovány).</summary>
    WaitingForWeightVerification,

    /// <summary>Probíhá platba na platebním terminálu.</summary>
    Paying,

    /// <summary>Nákup byl úspěšně zaplacen a dveře pro odchod odemčeny.</summary>
    Completed
}

public record KioskScanRequest(string ScannedCode);

public record ApplyPointsRequest(int PointsToRedeem);

public record CheckoutCartItemDto(
    int ProductId,
    string Ean,
    string Name,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    int UnitWeightGrams,
    int UnitToleranceGrams,
    int BonusPointsPerUnit,
    bool IsAgeRestricted);

public record CheckoutStateDto(
    Guid CheckoutSessionId,
    CheckoutStatus Status,
    Guid? CustomerId,
    string? CustomerName,
    bool CustomerIsAdult,
    int CustomerAvailablePoints,
    Guid? StoreSessionId,
    List<CheckoutCartItemDto> Items,
    int ExpectedWeightGrams,
    int TotalToleranceGrams,
    int CurrentScaleWeightGrams,
    bool IsWeightVerified,
    decimal SubtotalAmount,
    int RedeemedPoints,
    decimal PointsDiscountAmount,
    decimal FinalAmountToPay,
    int TotalBonusPointsToEarn,
    bool IsNightPricing,
    string StatusMessage);

public record KioskCustomerIdentifiedEvent(
    Guid CustomerId,
    string FullName,
    bool IsAdult,
    int LoyaltyPoints,
    bool AutoAssigned);

public record KioskErrorEvent(
    string Code,
    string Message,
    DateTime OccurredAtUtc);

public record ScaleWeightEvent(
    int WeightGrams,
    string RawFrame,
    DateTime TimestampUtc);

public record PaymentStatusEvent(
    Guid OrderId,
    decimal Amount,
    string Status,
    string? Reference,
    string Message);

public record CompletedOrderDto(
    Guid OrderId,
    Guid CustomerId,
    string CustomerName,
    decimal SubtotalAmount,
    int RedeemedPoints,
    decimal FinalPaidAmount,
    int EarnedPoints,
    int NewLoyaltyPointsBalance,
    string? PaymentReference,
    string PohodaXmlPath,
    DateTime PaidAtUtc);
