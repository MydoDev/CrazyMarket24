using CrazyMarket.Api.Contracts;

namespace CrazyMarket.Api.Hubs;

/// <summary>
/// Typovaný kontrakt událostí, které server posílá klientům (/admin, /kiosk, /pass).
/// </summary>
public interface IStoreHubClient
{
    Task ProductChanged(ProductDto product);
    Task ProductDeleted(int productId);
    Task EntryGranted(ScanEntryResponse entry);
    Task EslTagUpdated(EslPreviewDto tag);

    // --- Kiosek & Váha & Terminál (Sprint 3) ---
    Task ScaleWeightUpdated(ScaleWeightEvent weightEvent);
    Task CheckoutStateChanged(CheckoutStateDto state);
    Task KioskCustomerIdentified(KioskCustomerIdentifiedEvent customer);
    Task KioskError(KioskErrorEvent error);
    Task PaymentStatusChanged(PaymentStatusEvent payment);
    Task OrderCompleted(CompletedOrderDto order);
}
