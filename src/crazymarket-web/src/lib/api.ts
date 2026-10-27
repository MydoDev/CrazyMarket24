import type {
  AdminDashboardDto,
  CheckoutActionResult,
  CheckoutStateDto,
  CreateDeliveryItemRequest,
  CustomerDto,
  CustomerOrderHistoryDto,
  EslPreviewDto,
  GeneratedEntryToken,
  ProductDto,
  ScanEntryResponse,
  SetActionPriceResponse,
  StockDeliveryDto,
  SupplierDto,
} from "./types";

export const API_BASE_URL =
  process.env.NEXT_PUBLIC_API_URL || "http://localhost:5080";

async function requestJson<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...(init?.headers || {}),
    },
    cache: "no-store",
  });

  const text = await res.text();
  const data = text ? JSON.parse(text) : null;

  if (!res.ok) {
    const errMessage =
      data?.message || data?.detail || data?.title || `HTTP chyba ${res.status}`;
    const err = new Error(errMessage) as Error & {
      status?: number;
      payload?: unknown;
    };
    err.status = res.status;
    err.payload = data;
    throw err;
  }

  return data as T;
}

export const api = {
  // --- Zákazníci & Pass ---
  getCustomers: () => requestJson<CustomerDto[]>("/api/customers"),

  getCustomerOrders: (customerId: string) =>
    requestJson<CustomerOrderHistoryDto[]>(`/api/customers/${customerId}/orders`),

  getCustomerTotp: (customerId: string) =>
    requestJson<GeneratedEntryToken>(`/api/access/totp/customer/${customerId}`),

  getSupplierTotp: (supplierId: string) =>
    requestJson<GeneratedEntryToken>(`/api/access/totp/supplier/${supplierId}`),

  scanDoorEntry: (qrCode: string) =>
    requestJson<ScanEntryResponse>("/api/access/scan-entry", {
      method: "POST",
      body: JSON.stringify({ qrCode }),
    }),

  // --- Produkty & E-ink ---
  getProducts: () => requestJson<ProductDto[]>("/api/products"),

  getProductByEan: (ean: string) =>
    requestJson<ProductDto>(`/api/products/by-ean/${encodeURIComponent(ean)}`),

  renderProductEsl: (productId: number) =>
    requestJson<EslPreviewDto>(`/api/products/${productId}/esl/render`, {
      method: "POST",
    }),

  // --- Dodavatelé, Brigádníci (/vendor, /staff) & Závozy ---
  getSuppliers: () => requestJson<SupplierDto[]>("/api/suppliers"),

  getSupplierProducts: (supplierId: string) =>
    requestJson<ProductDto[]>(`/api/suppliers/${supplierId}/products`),

  setSupplierActionPrice: (
    supplierId: string,
    productId: number,
    actionPrice: number | null,
    validDays: number = 3
  ) =>
    requestJson<SetActionPriceResponse>(
      `/api/suppliers/${supplierId}/products/${productId}/action-price`,
      {
        method: "PUT",
        body: JSON.stringify({ actionPrice, validDays }),
      }
    ),

  createStockDelivery: (
    supplierId: string,
    items: CreateDeliveryItemRequest[]
  ) =>
    requestJson<StockDeliveryDto>("/api/deliveries", {
      method: "POST",
      body: JSON.stringify({ supplierId, items }),
    }),

  getStockDeliveries: () => requestJson<StockDeliveryDto[]>("/api/deliveries"),

  // --- Admin Dashboard (/admin) ---
  getAdminOverview: () =>
    requestJson<AdminDashboardDto>("/api/admin/overview"),

  adminEmergencyUnlockDoor: () =>
    requestJson<{ unlocked: boolean; durationSeconds: number; message: string }>(
      "/api/admin/door/emergency-unlock",
      { method: "POST" }
    ),

  adminTareScale: () =>
    requestJson<{
      tared: boolean;
      scaleWeightGrams: number;
      checkoutStatus: string;
      message: string;
    }>("/api/admin/scale/tare", { method: "POST" }),

  adminToggleDayNightPricing: (forceNight?: boolean) =>
    requestJson<{
      isNightPricingActive: boolean;
      regeneratedTagsCount: number;
      message: string;
      products: ProductDto[];
    }>(
      `/api/admin/pricing/toggle-night${
        forceNight !== undefined ? `?forceNight=${forceNight}` : ""
      }`,
      { method: "POST" }
    ),

  adminCloseSession: (sessionId: string) =>
    fetch(`${API_BASE_URL}/api/admin/sessions/${sessionId}/close`, {
      method: "POST",
    }),

  exportDeliveryToPohoda: (deliveryId: string) =>
    requestJson<{
      filePath: string;
      xmlContent: string;
      sentToMServer: boolean;
    }>(`/api/pohoda/export-delivery/${deliveryId}`, { method: "POST" }),

  exportOrderToPohoda: (orderId: string) =>
    requestJson<{
      filePath: string;
      xmlContent: string;
      sentToMServer: boolean;
    }>(`/api/pohoda/export-order/${orderId}`, { method: "POST" }),

  // --- Pokladna (/kiosk) ---
  getKioskState: () => requestJson<CheckoutStateDto>("/api/kiosk/state"),

  kioskScanEan: async (scannedCode: string): Promise<CheckoutActionResult> => {
    const res = await fetch(`${API_BASE_URL}/api/kiosk/scan-ean`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ scannedCode }),
      cache: "no-store",
    });
    return (await res.json()) as CheckoutActionResult;
  },

  kioskApplyPoints: async (
    pointsToRedeem?: number
  ): Promise<CheckoutActionResult> => {
    const res = await fetch(`${API_BASE_URL}/api/kiosk/apply-points`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(
        pointsToRedeem !== undefined ? { pointsToRedeem } : {}
      ),
      cache: "no-store",
    });
    return (await res.json()) as CheckoutActionResult;
  },

  kioskPay: async (): Promise<CheckoutActionResult> => {
    const res = await fetch(`${API_BASE_URL}/api/kiosk/pay`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      cache: "no-store",
    });
    return (await res.json()) as CheckoutActionResult;
  },

  kioskReset: () =>
    requestJson<CheckoutStateDto>("/api/kiosk/reset", { method: "POST" }),

  // --- Dev Hardware Triggery ---
  devSetScaleWeight: (grams: number) =>
    requestJson<{
      scaleWeightGrams: number;
      checkoutStatus: string;
      isWeightVerified: boolean;
    }>(`/api/dev/scale/set-weight?grams=${Math.max(0, Math.round(grams))}`, {
      method: "POST",
    }),

  devResolveTerminal: (approved: boolean) =>
    requestJson<{ resolved: boolean; approved: boolean; message: string }>(
      `/api/dev/terminal/resolve?approved=${approved}`,
      { method: "POST" }
    ),
};
