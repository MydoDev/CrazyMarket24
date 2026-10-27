export type ActivePriceTier = "Base" | "Night" | "Action";

export type SupplierRole = "ExternalVendor" | "InternalStaff" | "Admin";

export interface ProductDto {
  id: number;
  pohodaId: string | null;
  supplierId: string | null;
  ean: string;
  name: string;
  basePrice: number;
  nightPrice: number;
  actionPrice: number | null;
  actionValidUntil: string | null;
  activePrice: number;
  activePriceTier: ActivePriceTier;
  bonusPointsReward: number;
  weightGrams: number;
  toleranceGrams: number;
  stockCount: number;
  isAgeRestricted: boolean;
  eslMacAddress: string | null;
  hasEslImage: boolean;
  lastEslImageBase64: string | null;
  updatedAt: string;
}

export interface SupplierDto {
  id: string;
  companyName: string;
  ico: string;
  role: SupplierRole;
  allowDynamicPricing: boolean;
  canManageAllProducts: boolean;
  createdAt: string;
}

export interface EslPreviewDto {
  productId: number;
  ean: string;
  name: string;
  eslMacAddress: string | null;
  activePrice: number;
  bannerText: string | null;
  pushedToGateway: boolean;
  base64Png: string;
}

export interface SetActionPriceResponse {
  product: ProductDto;
  eslPreview: EslPreviewDto;
}

export interface CreateDeliveryItemRequest {
  productId: number;
  quantity: number;
  expirationDate?: string | null;
}

export interface StockDeliveryDto {
  id: string;
  supplierId: string;
  supplierName: string;
  supplierRole: SupplierRole;
  deliveredAt: string;
  pohodaSynced: boolean;
  pohodaSyncedAt: string | null;
  pohodaXmlPath: string | null;
  totalItemsCount: number;
}

export interface PohodaSyncItemDto {
  documentId: string;
  documentType: string;
  partnerOrCustomerName: string;
  createdAtUtc: string;
  totalAmount: number | null;
  itemsCount: number;
  exportedToDisk: boolean;
  fileName: string | null;
  statusText: string;
}

export interface AdminDashboardDto {
  isNightPricingActive: boolean;
  nightModeOverride: boolean | null;
  currentScaleWeightGrams: number;
  activeSessions: StoreSessionDto[];
  products: ProductDto[];
  pohodaDocuments: PohodaSyncItemDto[];
}

export interface CustomerDto {
  id: string;
  fullName: string;
  birthDate: string;
  isAdult: boolean;
  isAdultToday: boolean;
  loyaltyPoints: number;
  isBanned: boolean;
  createdAt: string;
}

export interface GeneratedEntryToken {
  qrPayload: string;
  totpCode: string;
  remainingSeconds: number;
  expiresAtUtc: string;
}

export interface StoreSessionDto {
  sessionId: string;
  actorType: "Customer" | "Supplier";
  actorId: string;
  displayName: string;
  isAdult: boolean;
  loyaltyPoints: number;
  enteredAtUtc: string;
  currentState: string;
}

export interface ScanEntryResponse {
  granted: boolean;
  welcomeMessage: string;
  session: StoreSessionDto;
}

export interface CustomerOrderItemHistoryDto {
  ean: string;
  productName: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

export interface CustomerOrderHistoryDto {
  id: string;
  createdAt: string;
  paidAt: string | null;
  totalAmount: number;
  isNightPricing: boolean;
  paymentStatus: string;
  paymentReference: string | null;
  pohodaXmlExported: boolean;
  items: CustomerOrderItemHistoryDto[];
}

export type CheckoutStatus =
  | "Idle"
  | "Ready"
  | "WaitingForWeightVerification"
  | "Paying"
  | "Completed";

export interface CheckoutCartItemDto {
  productId: number;
  ean: string;
  name: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
  unitWeightGrams: number;
  unitToleranceGrams: number;
  bonusPointsPerUnit: number;
  isAgeRestricted: boolean;
}

export interface CheckoutStateDto {
  checkoutSessionId: string;
  status: CheckoutStatus;
  customerId: string | null;
  customerName: string | null;
  customerIsAdult: boolean;
  customerAvailablePoints: number;
  storeSessionId: string | null;
  items: CheckoutCartItemDto[];
  expectedWeightGrams: number;
  totalToleranceGrams: number;
  currentScaleWeightGrams: number;
  isWeightVerified: boolean;
  subtotalAmount: number;
  redeemedPoints: number;
  pointsDiscountAmount: number;
  finalAmountToPay: number;
  totalBonusPointsToEarn: number;
  isNightPricing: boolean;
  statusMessage: string;
}

export interface CompletedOrderDto {
  orderId: string;
  customerId: string;
  customerName: string;
  subtotalAmount: number;
  redeemedPoints: number;
  finalPaidAmount: number;
  earnedPoints: number;
  newLoyaltyPointsBalance: number;
  paymentReference: string | null;
  pohodaXmlPath: string;
  paidAtUtc: string;
}

export interface CheckoutActionResult {
  success: boolean;
  httpStatusCode: number;
  errorCode: string | null;
  message: string;
  state: CheckoutStateDto;
  completedOrder?: CompletedOrderDto | null;
}

export interface KioskCustomerIdentifiedEvent {
  customerId: string;
  fullName: string;
  isAdult: boolean;
  loyaltyPoints: number;
  autoAssigned: boolean;
}

export interface KioskErrorEvent {
  code: string;
  message: string;
  occurredAtUtc: string;
}

export interface ScaleWeightEvent {
  weightGrams: number;
  rawFrame: string;
  timestampUtc: string;
}

export interface PaymentStatusEvent {
  orderId: string;
  amount: number;
  status: string;
  reference: string | null;
  message: string;
}
