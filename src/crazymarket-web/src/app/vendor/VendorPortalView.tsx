"use client";

import React, { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { QRCodeSVG } from "qrcode.react";
import {
  Truck,
  Barcode,
  Camera,
  CameraOff,
  PackagePlus,
  Tag,
  DoorOpen,
  Clock,
  CheckCircle2,
  AlertTriangle,
  ArrowLeft,
  Sparkles,
  FileCode2,
  Building2,
  UserCog,
  RefreshCw,
  ClipboardList,
  Trash2,
} from "lucide-react";
import { api } from "@/lib/api";
import type {
  EslPreviewDto,
  GeneratedEntryToken,
  ProductDto,
  StockDeliveryDto,
  SupplierDto,
} from "@/lib/types";

export interface DeliveryBatchItem {
  product: ProductDto;
  quantity: number;
  expirationDate: string | null;
}

export interface VendorPortalProps {
  defaultRoleFilter?: "ExternalVendor" | "InternalStaff";
}

export default function VendorPortalPage() {
  return <VendorPortalView />;
}

export function VendorPortalView({
  defaultRoleFilter,
}: VendorPortalProps = {}) {
  const [suppliers, setSuppliers] = useState<SupplierDto[]>([]);
  const [selectedSupplierId, setSelectedSupplierId] = useState<string>("");
  const [products, setProducts] = useState<ProductDto[]>([]);
  const [totpToken, setTotpToken] = useState<GeneratedEntryToken | null>(null);
  const [secondsLeft, setSecondsLeft] = useState<number>(30);

  // Stav pro otevření dveří v zásobovacím režimu
  const [doorFeedback, setDoorFeedback] = useState<string | null>(null);
  const [feedbackError, setFeedbackError] = useState<string | null>(null);

  // Stav pro ExternalVendor (Naskladnění & ActionPrice sleva před expirací)
  const [restockModalProduct, setRestockModalProduct] =
    useState<ProductDto | null>(null);
  const [restockQty, setRestockQty] = useState<number>(24);
  const [restockExpDate, setRestockExpDate] = useState<string>("");
  const [actionPriceInputs, setActionPriceInputs] = useState<
    Record<number, string>
  >({});
  const [latestEslPreview, setLatestEslPreview] =
    useState<EslPreviewDto | null>(null);
  const [lastDeliveryReceipt, setLastDeliveryReceipt] =
    useState<StockDeliveryDto | null>(null);
  const [saving, setSaving] = useState<boolean>(false);

  // Souhrnný dodací list (košík závozu) – 1 POHODA XML Příjemka pro celý závoz
  const [deliveryBatch, setDeliveryBatch] = useState<DeliveryBatchItem[]>([]);

  // Stav pro InternalStaff / Admin (Staff Skener: html5-qrcode kamera + Bluetooth HID)
  const [scannedProduct, setScannedProduct] = useState<ProductDto | null>(null);
  const [staffQty, setStaffQty] = useState<number>(24);
  const [manualEanInput, setManualEanInput] = useState<string>("");
  const [cameraActive, setCameraActive] = useState<boolean>(false);
  const html5QrScannerRef = useRef<{ stop: () => Promise<void>; clear: () => void } | null>(null);
  const hidBufferRef = useRef<string>("");
  const lastHidTimeRef = useRef<number>(0);

  const selectedSupplier =
    suppliers.find((s) => s.id === selectedSupplierId) || suppliers[0] || null;

  const isStaffOrAdmin =
    selectedSupplier?.role === "InternalStaff" ||
    selectedSupplier?.role === "Admin";

  const loadSuppliers = useCallback(async () => {
    try {
      const list = await api.getSuppliers();
      setSuppliers(list);
      setSelectedSupplierId((prev) => {
        if (prev) return prev;
        if (defaultRoleFilter === "InternalStaff") {
          const staff = list.find((s) => s.role === "InternalStaff");
          if (staff) return staff.id;
        }
        return list[0]?.id || "";
      });
    } catch (e) {
      console.error("Chyba při načítání dodavatelů:", e);
    }
  }, [defaultRoleFilter]);

  const loadScopedData = useCallback(async (supplierId: string) => {
    if (!supplierId) return;
    try {
      const [scopedProducts, token] = await Promise.all([
        api.getSupplierProducts(supplierId),
        api.getSupplierTotp(supplierId),
      ]);
      setProducts(scopedProducts);
      setTotpToken(token);
      setSecondsLeft(Math.max(1, token.remainingSeconds));

      const priceMap: Record<number, string> = {};
      for (const p of scopedProducts) {
        priceMap[p.id] =
          p.actionPrice !== null
            ? p.actionPrice.toFixed(2)
            : Math.max(1, +(p.basePrice * 0.8).toFixed(1)).toFixed(2);
      }
      setActionPriceInputs(priceMap);
    } catch (e) {
      console.error("Chyba při načítání dat dodavatele:", e);
    }
  }, []);

  useEffect(() => {
    loadSuppliers();
  }, [loadSuppliers]);

  useEffect(() => {
    if (!selectedSupplierId) return;
    setLatestEslPreview(null);
    setScannedProduct(null);
    setDeliveryBatch([]);
    loadScopedData(selectedSupplierId);
  }, [selectedSupplierId, loadScopedData]);

  // 30s odpočet TOTP QR kódu zásobování
  useEffect(() => {
    if (!selectedSupplierId) return;
    const timer = setInterval(() => {
      setSecondsLeft((prev) => {
        if (prev <= 1) {
          api
            .getSupplierTotp(selectedSupplierId)
            .then((t) => setTotpToken(t))
            .catch(() => {});
          return 30;
        }
        return prev - 1;
      });
    }, 1000);
    return () => clearInterval(timer);
  }, [selectedSupplierId]);

  // Vyhledání produktu po pípnutí EAN (kamera nebo Bluetooth HID skener)
  const handleStaffEanScanned = useCallback(
    async (ean: string) => {
      const clean = ean.trim();
      if (!clean) return;
      setFeedbackError(null);
      setLastDeliveryReceipt(null);

      const localMatch = products.find((p) => p.ean === clean);
      if (localMatch) {
        setScannedProduct(localMatch);
        setStaffQty(24);
        return;
      }

      try {
        const remoteProd = await api.getProductByEan(clean);
        setScannedProduct(remoteProd);
        setStaffQty(24);
      } catch {
        setFeedbackError(`Produkt s EAN '${clean}' nebyl v databázi nalezen.`);
      }
    },
    [products]
  );

  // Globální Bluetooth HID Barcode Scanner listener pro roli InternalStaff / Admin
  useEffect(() => {
    if (!isStaffOrAdmin) return;

    const onKeyDown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      if (
        target &&
        (target.tagName === "INPUT" ||
          target.tagName === "TEXTAREA" ||
          target.isContentEditable)
      ) {
        return;
      }

      const now = Date.now();
      if (now - lastHidTimeRef.current > 1200) {
        hidBufferRef.current = "";
      }
      lastHidTimeRef.current = now;

      if (e.key === "Enter") {
        const code = hidBufferRef.current.trim();
        hidBufferRef.current = "";
        if (code.length >= 6) {
          e.preventDefault();
          handleStaffEanScanned(code);
        }
        return;
      }

      if (e.key.length === 1 && !e.ctrlKey && !e.altKey && !e.metaKey) {
        hidBufferRef.current += e.key;
      }
    };

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [isStaffOrAdmin, handleStaffEanScanned]);

  // Kamera čtečky čárových kódů (html5-qrcode)
  const toggleCameraScanner = async () => {
    if (cameraActive) {
      if (html5QrScannerRef.current) {
        await html5QrScannerRef.current.stop().catch(() => {});
        html5QrScannerRef.current.clear();
        html5QrScannerRef.current = null;
      }
      setCameraActive(false);
      return;
    }

    setCameraActive(true);
    setFeedbackError(null);

    setTimeout(async () => {
      try {
        const { Html5Qrcode } = await import("html5-qrcode");
        const scanner = new Html5Qrcode("staff-camera-reader");
        html5QrScannerRef.current = scanner;

        await scanner.start(
          { facingMode: "environment" },
          { fps: 10, qrbox: { width: 240, height: 120 } },
          (decodedText) => {
            handleStaffEanScanned(decodedText);
            scanner.stop().catch(() => {});
            setCameraActive(false);
          },
          () => {}
        );
      } catch {
        setCameraActive(false);
        setFeedbackError(
          "Kamera není dostupná nebo nebylo uděleno oprávnění. Použijte Bluetooth HID skener nebo rychlý výběr produktu níže."
        );
      }
    }, 150);
  };

  const handleOpenSupplyDoor = async () => {
    if (!totpToken) return;
    setDoorFeedback(null);
    setFeedbackError(null);
    try {
      const res = await api.scanDoorEntry(totpToken.qrPayload);
      setDoorFeedback(
        `${res.welcomeMessage} (Zásobovací režim aktivní – dveře odemčeny na 15 s)`
      );
    } catch (err) {
      setFeedbackError(
        err instanceof Error ? err.message : "Chyba při otevírání dveří."
      );
    }
  };

  // Přidání položky na souhrnný dodací list (sloučí kusy, pokud už produkt se stejnou expirací v seznamu je)
  const handleAddToBatch = (
    product: ProductDto,
    quantity: number,
    expirationDate: string | null = null
  ) => {
    if (quantity <= 0) return;
    setFeedbackError(null);
    setLastDeliveryReceipt(null);

    const normalizedExp = expirationDate?.trim() ? expirationDate.trim() : null;

    setDeliveryBatch((prev) => {
      const existingIndex = prev.findIndex(
        (item) =>
          item.product.id === product.id &&
          item.expirationDate === normalizedExp
      );
      if (existingIndex >= 0) {
        const updated = [...prev];
        updated[existingIndex] = {
          ...updated[existingIndex],
          quantity: updated[existingIndex].quantity + quantity,
        };
        return updated;
      }
      return [...prev, { product, quantity, expirationDate: normalizedExp }];
    });

    setRestockModalProduct(null);
    setRestockExpDate("");
    setScannedProduct(null);
  };

  const handleUpdateBatchQty = (index: number, newQty: number) => {
    if (newQty <= 0) {
      handleRemoveFromBatch(index);
      return;
    }
    setDeliveryBatch((prev) =>
      prev.map((item, i) => (i === index ? { ...item, quantity: newQty } : item))
    );
  };

  const handleRemoveFromBatch = (index: number) => {
    setDeliveryBatch((prev) => prev.filter((_, i) => i !== index));
  };

  // Odeslání celého souhrnného dodacího listu najednou -> 1 DB transakce a 1 souhrnná POHODA XML Příjemka
  const handleSubmitBatchDelivery = async () => {
    if (!selectedSupplier || deliveryBatch.length === 0) return;
    setSaving(true);
    setFeedbackError(null);
    try {
      const delivery = await api.createStockDelivery(
        selectedSupplier.id,
        deliveryBatch.map((item) => ({
          productId: item.product.id,
          quantity: item.quantity,
          expirationDate: item.expirationDate,
        }))
      );
      setLastDeliveryReceipt(delivery);
      setDeliveryBatch([]);
      setRestockModalProduct(null);
      setScannedProduct(null);
      await loadScopedData(selectedSupplier.id);
    } catch (err) {
      setFeedbackError(
        err instanceof Error
          ? err.message
          : "Nepodařilo se uložit souhrnný závoz."
      );
    } finally {
      setSaving(false);
    }
  };

  const handleSaveRestock = async (product: ProductDto, quantity: number) => {
    if (!selectedSupplier || quantity <= 0) return;
    setSaving(true);
    setFeedbackError(null);
    try {
      const delivery = await api.createStockDelivery(selectedSupplier.id, [
        {
          productId: product.id,
          quantity,
          expirationDate: restockExpDate || null,
        },
      ]);
      setLastDeliveryReceipt(delivery);
      setRestockModalProduct(null);
      setScannedProduct(null);
      await loadScopedData(selectedSupplier.id);
    } catch (err) {
      setFeedbackError(
        err instanceof Error ? err.message : "Nepodařilo se uložit závoz."
      );
    } finally {
      setSaving(false);
    }
  };

  const handleApplyActionPrice = async (
    product: ProductDto,
    enableAction: boolean
  ) => {
    if (!selectedSupplier) return;
    setSaving(true);
    setFeedbackError(null);
    try {
      const rawVal = parseFloat(actionPriceInputs[product.id] || "0");
      const priceToSend = enableAction ? rawVal : null;
      const res = await api.setSupplierActionPrice(
        selectedSupplier.id,
        product.id,
        priceToSend,
        3
      );
      setLatestEslPreview(res.eslPreview);
      await loadScopedData(selectedSupplier.id);
    } catch (err) {
      setFeedbackError(
        err instanceof Error ? err.message : "Nelze nastavit akční cenu."
      );
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="min-h-screen bg-[#070b12] text-slate-100 py-6 px-4 flex flex-col items-center">
      <div className="w-full max-w-3xl space-y-6">
        {/* Horní navigace */}
        <div className="flex items-center justify-between">
          <Link
            href="/"
            className="inline-flex items-center gap-1.5 text-xs text-slate-400 hover:text-emerald-400"
          >
            <ArrowLeft className="w-4 h-4" />
            Rozcestník
          </Link>
          <div className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-emerald-500/10 border border-emerald-500/30 text-emerald-400 text-xs font-semibold">
            <Truck className="w-3.5 h-3.5" />
            Zásobovací Portál & Staff Skener
          </div>
          <Link
            href="/admin"
            className="text-xs font-medium text-emerald-400 hover:underline"
          >
            Admin Dashboard →
          </Link>
        </div>

        {/* 1. VÝBĚR ÚČTU A ROLE (ExternalVendor vs. InternalStaff / Admin) */}
        <div className="rounded-3xl bg-slate-900/90 border border-slate-800 p-5 space-y-3">
          <div className="flex items-center justify-between">
            <span className="text-xs font-bold uppercase tracking-wider text-slate-400 flex items-center gap-1.5">
              <UserCog className="w-4 h-4 text-emerald-400" />
              Přihlášený zásobovací účet (Role-Based Access)
            </span>
            {selectedSupplier && (
              <span
                className={`text-xs font-bold px-2.5 py-0.5 rounded-full border ${
                  selectedSupplier.role === "ExternalVendor"
                    ? "bg-amber-500/15 border-amber-500/40 text-amber-300"
                    : "bg-emerald-500/15 border-emerald-500/40 text-emerald-300"
                }`}
              >
                Role: {selectedSupplier.role}
              </span>
            )}
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5">
            {suppliers.map((s) => {
              const active = s.id === selectedSupplier?.id;
              return (
                <button
                  key={s.id}
                  type="button"
                  onClick={() => {
                    setSelectedSupplierId(s.id);
                    setDoorFeedback(null);
                    setFeedbackError(null);
                    setLastDeliveryReceipt(null);
                  }}
                  className={`text-left p-3.5 rounded-2xl border transition-all ${
                    active
                      ? "bg-emerald-500/15 border-emerald-500 text-white shadow-lg shadow-emerald-500/10"
                      : "bg-slate-950/70 border-slate-800 text-slate-400 hover:border-slate-700"
                  }`}
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-bold text-sm truncate">
                      {s.companyName}
                    </span>
                    <span
                      className={`text-[10px] font-bold px-2 py-0.5 rounded shrink-0 ${
                        s.role === "ExternalVendor"
                          ? "bg-amber-500/20 text-amber-300"
                          : "bg-emerald-500/20 text-emerald-300"
                      }`}
                    >
                      {s.role === "ExternalVendor"
                        ? "Externí dodavatel"
                        : s.role === "InternalStaff"
                        ? "Brigádník (Staff)"
                        : "Majitel (Admin)"}
                    </span>
                  </div>
                  <div className="text-[11px] text-slate-400 mt-1 flex items-center justify-between">
                    <span>IČO: {s.ico}</span>
                    <span>
                      {s.canManageAllProducts
                        ? "Všechny produkty v DB"
                        : "Pouze vlastní produkty"}
                    </span>
                  </div>
                </button>
              );
            })}
          </div>
        </div>

        {/* 2. SPOLEČNÁ SEKCE: VSTUPNÍ TOTP QR KÓD PRO ZÁSOBOVACÍ REŽIM */}
        {selectedSupplier && (
          <div className="rounded-3xl bg-slate-900/90 border border-slate-800 p-5 grid grid-cols-1 md:grid-cols-12 gap-5 items-center">
            <div className="md:col-span-4 flex flex-col items-center bg-white rounded-2xl p-4 text-slate-900">
              {totpToken ? (
                <QRCodeSVG value={totpToken.qrPayload} size={140} level="M" />
              ) : (
                <div className="w-[140px] h-[140px] flex items-center justify-center text-xs text-slate-400">
                  Načítám QR...
                </div>
              )}
              {totpToken && (
                <div className="mt-2 font-mono font-black text-lg tracking-widest">
                  {totpToken.totpCode}
                </div>
              )}
            </div>

            <div className="md:col-span-8 space-y-3">
              <div className="flex items-center justify-between">
                <div>
                  <span className="text-xs font-mono uppercase text-emerald-400 font-bold">
                    Vstup do prodejny v zásobovacím režimu
                  </span>
                  <h2 className="text-lg font-bold text-white">
                    {selectedSupplier.companyName}
                  </h2>
                </div>
                <span className="text-xs font-mono text-emerald-400 flex items-center gap-1">
                  <Clock className="w-3.5 h-3.5" /> {secondsLeft} s
                </span>
              </div>

              <p className="text-xs text-slate-400 leading-relaxed">
                Pípnutí tohoto TOTP QR kódu u dveří přepne prodejnu do{" "}
                <strong className="text-slate-200">Zásobovacího režimu</strong>{" "}
                (odemkne dveře na 15 s a nehlásí váhové alarmy).
              </p>

              <button
                type="button"
                onClick={handleOpenSupplyDoor}
                className="w-full py-3 px-4 rounded-xl bg-emerald-500 hover:bg-emerald-400 text-slate-950 font-bold text-xs flex items-center justify-center gap-2 shadow-lg shadow-emerald-500/20"
              >
                <DoorOpen className="w-4 h-4" />
                Otevřít dveře v zásobovacím režimu (Simulovat pípnutí)
              </button>

              {doorFeedback && (
                <div className="rounded-xl bg-emerald-500/15 border border-emerald-500/40 p-2.5 text-xs text-emerald-200 flex items-center gap-2">
                  <CheckCircle2 className="w-4 h-4 text-emerald-400 shrink-0" />
                  <span>{doorFeedback}</span>
                </div>
              )}
            </div>
          </div>
        )}

        {/* POTVRZENÍ VYGENEROVANÉ POHODA XML PŘÍJEMKY */}
        {lastDeliveryReceipt && (
          <div className="rounded-2xl bg-emerald-500/15 border-2 border-emerald-500/60 p-4 space-y-1.5">
            <div className="flex items-center justify-between">
              <span className="text-sm font-bold text-emerald-300 flex items-center gap-2">
                <FileCode2 className="w-4 h-4" />
                Naskladněno (+{lastDeliveryReceipt.totalItemsCount} ks) • POHODA
                XML Příjemka vygenerována!
              </span>
              <button
                type="button"
                onClick={() => setLastDeliveryReceipt(null)}
                className="text-xs text-emerald-300 hover:underline"
              >
                Skrýt
              </button>
            </div>
            <div className="text-xs font-mono text-slate-300 break-all">
              Soubor (Windows-1250): {lastDeliveryReceipt.pohodaXmlPath}
            </div>
          </div>
        )}

        {feedbackError && (
          <div className="rounded-2xl bg-rose-500/15 border border-rose-500/50 p-4 text-xs text-rose-200 flex items-center gap-2">
            <AlertTriangle className="w-4 h-4 text-rose-400 shrink-0" />
            <span>{feedbackError}</span>
          </div>
        )}

        {/* NÁHLED PRÁVĚ VYGENEROVANÉ 1-BIT E-INK CENOVKY (po zapnutí ActionPrice) */}
        {latestEslPreview && (
          <div className="rounded-3xl bg-slate-900 border-2 border-emerald-500/60 p-5 space-y-3">
            <div className="flex items-center justify-between">
              <div>
                <span className="text-xs font-mono uppercase text-emerald-400 font-bold">
                  EslImageGenerator • 1-bit PNG (296×128 px)
                </span>
                <h3 className="text-base font-bold text-white">
                  Černobílý E-ink štítek okamžitě překreslen: {latestEslPreview.name}
                </h3>
              </div>
              <button
                type="button"
                onClick={() => setLatestEslPreview(null)}
                className="text-xs text-slate-400 hover:text-white"
              >
                Zavřít ✕
              </button>
            </div>
            <div className="flex flex-col sm:flex-row items-center gap-5 bg-slate-950 rounded-2xl p-4 border border-slate-800">
              <img
                src={`data:image/png;base64,${latestEslPreview.base64Png}`}
                alt={latestEslPreview.name}
                width={296}
                height={128}
                className="rounded border border-slate-700 bg-white shrink-0"
                style={{ imageRendering: "pixelated" }}
              />
              <div className="text-xs text-slate-300 space-y-1.5">
                <div>
                  MAC cenovky:{" "}
                  <code className="text-emerald-400 font-mono">
                    {latestEslPreview.eslMacAddress || "N/A"}
                  </code>
                </div>
                <div>
                  Nová aktivní cena:{" "}
                  <strong className="text-white">
                    {latestEslPreview.activePrice.toFixed(2)} Kč
                  </strong>
                </div>
                <div>
                  Horní inverzní pruh:{" "}
                  <strong className="text-emerald-400">
                    {latestEslPreview.bannerText || "Standardní denní"}
                  </strong>
                </div>
              </div>
            </div>
          </div>
        )}

        {/* 3A. REŽIM PRO INTERNAL STAFF / ADMIN: ČTEČKA ČÁROVÝCH KÓDŮ (Makro závoz) */}
        {isStaffOrAdmin && (
          <div className="rounded-3xl bg-slate-900/90 border border-emerald-500/40 p-6 space-y-5">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <span className="text-xs font-mono uppercase text-emerald-400 font-bold">
                  Globální zásobování • Brigádník / Majitel
                </span>
                <h2 className="text-xl font-extrabold text-white flex items-center gap-2">
                  <Barcode className="w-6 h-6 text-emerald-400" />
                  Staff EAN Skener (Kamera mobilu & Bluetooth HID)
                </h2>
              </div>

              <button
                type="button"
                onClick={toggleCameraScanner}
                className={`px-4 py-2.5 rounded-xl font-bold text-xs flex items-center gap-2 transition-colors ${
                  cameraActive
                    ? "bg-rose-500 text-white"
                    : "bg-emerald-500 hover:bg-emerald-400 text-slate-950"
                }`}
              >
                {cameraActive ? (
                  <>
                    <CameraOff className="w-4 h-4" /> Vypnout kameru
                  </>
                ) : (
                  <>
                    <Camera className="w-4 h-4" /> Spustit kameru (html5-qrcode)
                  </>
                )}
              </button>
            </div>

            {cameraActive && (
              <div
                id="staff-camera-reader"
                className="w-full rounded-2xl overflow-hidden border border-emerald-500/40 bg-black"
              />
            )}

            {/* Ruční pípnutí / zadání EAN nebo rychlý výběr produktu (např. Red Bull 250 ml) */}
            <div className="flex gap-2">
              <input
                type="text"
                value={manualEanInput}
                onChange={(e) => setManualEanInput(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter") {
                    handleStaffEanScanned(manualEanInput);
                  }
                }}
                placeholder="Pípněte Bluetooth čtečkou nebo zadejte EAN (např. 2990000000019 pro Red Bull)..."
                className="flex-1 rounded-xl bg-slate-950 border border-slate-800 px-4 py-2.5 text-sm text-white placeholder:text-slate-500 focus:outline-none focus:border-emerald-500"
              />
              <button
                type="button"
                onClick={() => handleStaffEanScanned(manualEanInput)}
                className="px-4 py-2.5 rounded-xl bg-slate-800 hover:bg-slate-700 text-xs font-bold text-emerald-400"
              >
                Vyhledat EAN
              </button>
            </div>

            {/* Rychlá tlačítka pro okamžitou simulaci pípnutí krabice z Makra */}
            <div className="flex flex-wrap gap-1.5">
              {products.map((p) => (
                <button
                  key={p.id}
                  type="button"
                  onClick={() => {
                    setScannedProduct(p);
                    setStaffQty(24);
                  }}
                  className={`px-2.5 py-1.5 rounded-lg text-xs border transition-colors ${
                    scannedProduct?.id === p.id
                      ? "bg-emerald-500 text-slate-950 border-emerald-400 font-bold"
                      : "bg-slate-950 border-slate-800 text-slate-300 hover:border-emerald-500/40"
                  }`}
                >
                  📦 {p.name}
                </button>
              ))}
            </div>

            {/* Karta naskenovaného produktu (např. RedBull 250ml -> +24 ks -> Uložit) */}
            {scannedProduct && (
              <div className="rounded-2xl bg-slate-950 border-2 border-emerald-500 p-5 space-y-4">
                <div className="flex items-start justify-between gap-4">
                  <div>
                    <span className="text-[11px] font-mono text-emerald-400">
                      EAN: {scannedProduct.ean} • Skladem:{" "}
                      {scannedProduct.stockCount} ks
                    </span>
                    <h3 className="text-xl font-black text-white mt-0.5">
                      {scannedProduct.name}
                    </h3>
                  </div>
                  <span className="text-lg font-mono font-bold text-emerald-400">
                    {scannedProduct.activePrice.toFixed(2)} Kč
                  </span>
                </div>

                <div className="flex flex-wrap items-center gap-3">
                  <span className="text-xs text-slate-400 font-semibold">
                    Počet kusů k naskladnění:
                  </span>
                  {[6, 12, 24, 48].map((preset) => (
                    <button
                      key={preset}
                      type="button"
                      onClick={() => setStaffQty(preset)}
                      className={`px-3 py-1.5 rounded-lg text-xs font-bold border ${
                        staffQty === preset
                          ? "bg-emerald-500 text-slate-950 border-emerald-400"
                          : "bg-slate-900 border-slate-800 text-slate-300"
                      }`}
                    >
                      +{preset} ks
                    </button>
                  ))}
                  <input
                    type="number"
                    min={1}
                    value={staffQty}
                    onChange={(e) =>
                      setStaffQty(Math.max(1, parseInt(e.target.value, 10) || 1))
                    }
                    className="w-24 rounded-lg bg-slate-900 border border-slate-700 px-3 py-1.5 text-sm font-mono font-bold text-white"
                  />
                </div>

                <div className="flex flex-col sm:flex-row gap-2.5">
                  <button
                    type="button"
                    disabled={saving}
                    onClick={() => handleAddToBatch(scannedProduct, staffQty)}
                    className="flex-1 py-3.5 px-5 rounded-xl bg-emerald-500 hover:bg-emerald-400 disabled:opacity-50 text-slate-950 font-black text-sm flex items-center justify-center gap-2 shadow-lg"
                  >
                    <ClipboardList className="w-5 h-5" />
                    Přidat na dodací list (+{staffQty} ks)
                  </button>
                  <button
                    type="button"
                    disabled={saving}
                    onClick={() => handleSaveRestock(scannedProduct, staffQty)}
                    className="py-3.5 px-4 rounded-xl bg-slate-800 hover:bg-slate-700 disabled:opacity-50 text-slate-200 font-bold text-xs flex items-center justify-center gap-1.5"
                  >
                    <PackagePlus className="w-4 h-4 text-emerald-400" />
                    Rychle vystavit jen 1 položku
                  </button>
                </div>
              </div>
            )}
          </div>
        )}

        {/* SOUHRNNÝ DODACÍ LIST ZÁVOZU (1 DB transakce + 1 souhrnná POHODA XML Příjemka pro celý závoz) */}
        {deliveryBatch.length > 0 && (
          <div className="rounded-3xl bg-slate-900 border-2 border-emerald-500 p-6 space-y-4 shadow-xl shadow-emerald-500/10">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <span className="text-xs font-mono uppercase text-emerald-400 font-bold">
                  Optimalizovaný hromadný závoz • 1 souhrnná POHODA XML Příjemka
                </span>
                <h2 className="text-lg font-extrabold text-white flex items-center gap-2 mt-0.5">
                  <ClipboardList className="w-5 h-5 text-emerald-400" />
                  Rozpracovaný dodací list ({deliveryBatch.length}{" "}
                  {deliveryBatch.length === 1
                    ? "položka"
                    : deliveryBatch.length < 5
                    ? "položky"
                    : "položek"}
                  , celkem{" "}
                  {deliveryBatch.reduce((acc, i) => acc + i.quantity, 0)} ks)
                </h2>
              </div>
              <button
                type="button"
                onClick={() => setDeliveryBatch([])}
                className="text-xs text-rose-400 hover:underline flex items-center gap-1"
              >
                <Trash2 className="w-3.5 h-3.5" /> Vyčistit seznam
              </button>
            </div>

            <div className="space-y-2">
              {deliveryBatch.map((item, idx) => (
                <div
                  key={`${item.product.id}-${item.expirationDate || "noexp"}`}
                  className="rounded-2xl bg-slate-950 border border-slate-800 px-4 py-3 flex flex-wrap items-center justify-between gap-3"
                >
                  <div>
                    <div className="font-bold text-sm text-white">
                      {item.product.name}
                    </div>
                    <div className="text-[11px] font-mono text-slate-400">
                      EAN: {item.product.ean}
                      {item.expirationDate
                        ? ` • Expirace šarže: ${item.expirationDate}`
                        : ""}
                    </div>
                  </div>

                  <div className="flex items-center gap-2">
                    <button
                      type="button"
                      onClick={() =>
                        handleUpdateBatchQty(idx, item.quantity - 1)
                      }
                      className="w-7 h-7 rounded-lg bg-slate-800 hover:bg-slate-700 text-white font-bold text-sm"
                    >
                      −
                    </button>
                    <input
                      type="number"
                      min={1}
                      value={item.quantity}
                      onChange={(e) =>
                        handleUpdateBatchQty(
                          idx,
                          Math.max(1, parseInt(e.target.value, 10) || 1)
                        )
                      }
                      className="w-20 rounded-lg bg-slate-900 border border-slate-700 px-2.5 py-1 text-center text-sm font-mono font-bold text-emerald-400"
                    />
                    <span className="text-xs text-slate-400">ks</span>
                    <button
                      type="button"
                      onClick={() =>
                        handleUpdateBatchQty(idx, item.quantity + 1)
                      }
                      className="w-7 h-7 rounded-lg bg-slate-800 hover:bg-slate-700 text-white font-bold text-sm"
                    >
                      +
                    </button>
                    <button
                      type="button"
                      onClick={() => handleRemoveFromBatch(idx)}
                      className="ml-1 p-1.5 rounded-lg text-slate-500 hover:text-rose-400 hover:bg-slate-900"
                      title="Odebrat z dodacího listu"
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </div>
                </div>
              ))}
            </div>

            <button
              type="button"
              disabled={saving}
              onClick={handleSubmitBatchDelivery}
              className="w-full py-4 px-5 rounded-2xl bg-emerald-500 hover:bg-emerald-400 disabled:opacity-50 text-slate-950 font-black text-sm flex items-center justify-center gap-2 shadow-lg shadow-emerald-500/20"
            >
              <FileCode2 className="w-5 h-5" />
              {saving
                ? "Ukládám celý závoz a generuji 1 souhrnnou POHODA XML Příjemku..."
                : `Uzavřít celý závoz (+${deliveryBatch.reduce(
                    (acc, i) => acc + i.quantity,
                    0
                  )} ks) & Vystavit 1 POHODA Příjemku`}
            </button>
          </div>
        )}

        {/* 3B. SEZNAM POLOŽEK PODLE ROLE (ExternalVendor vidí jen své, Staff/Admin všechny) */}
        <div className="rounded-3xl bg-slate-900/90 border border-slate-800 p-6 space-y-4">
          <div className="flex items-center justify-between">
            <h2 className="text-lg font-bold text-white flex items-center gap-2">
              <Building2 className="w-5 h-5 text-emerald-400" />
              {isStaffOrAdmin
                ? `Všechny produkty v prodejně (${products.length})`
                : `Moje dodávané položky (${products.length})`}
            </h2>
            <button
              type="button"
              onClick={() =>
                selectedSupplier && loadScopedData(selectedSupplier.id)
              }
              className="text-xs text-slate-400 hover:text-emerald-400 flex items-center gap-1"
            >
              <RefreshCw className="w-3.5 h-3.5" /> Obnovit
            </button>
          </div>

          <div className="space-y-3">
            {products.map((p) => {
              const hasAction = p.activePriceTier === "Action";
              const canEditDynamicPrice =
                isStaffOrAdmin || selectedSupplier?.allowDynamicPricing;

              return (
                <div
                  key={p.id}
                  className="rounded-2xl bg-slate-950/80 border border-slate-800 p-4 space-y-3"
                >
                  <div className="flex flex-wrap items-center justify-between gap-3">
                    <div>
                      <div className="flex items-center gap-2">
                        <span className="font-bold text-base text-white">
                          {p.name}
                        </span>
                        {hasAction && (
                          <span className="px-2 py-0.5 rounded bg-emerald-500 text-slate-950 text-[10px] font-black uppercase">
                            AKCE {p.actionPrice?.toFixed(2)} Kč
                          </span>
                        )}
                      </div>
                      <div className="text-xs text-slate-400 font-mono mt-0.5">
                        EAN: {p.ean} • Skladem:{" "}
                        <strong className="text-emerald-400">
                          {p.stockCount} ks
                        </strong>{" "}
                        • Běžná cena: {p.basePrice.toFixed(2)} Kč
                      </div>
                    </div>

                    <div className="flex items-center gap-2">
                      <button
                        type="button"
                        onClick={() => handleAddToBatch(p, 20)}
                        className="px-3 py-2 rounded-xl bg-emerald-500/15 hover:bg-emerald-500/25 border border-emerald-500/40 text-emerald-300 font-bold text-xs flex items-center gap-1.5"
                        title="Rychle přidat +20 ks na společný dodací list"
                      >
                        <ClipboardList className="w-3.5 h-3.5" />
                        +20 ks do závozu
                      </button>
                      <button
                        type="button"
                        onClick={() => {
                          setRestockModalProduct(p);
                          setRestockQty(20);
                        }}
                        className="px-3.5 py-2 rounded-xl bg-emerald-500 hover:bg-emerald-400 text-slate-950 font-bold text-xs flex items-center gap-1.5"
                      >
                        <PackagePlus className="w-4 h-4" />
                        Naskladnit
                      </button>
                    </div>
                  </div>

                  {/* Dynamické přeceňování (ActionPrice – Sleva před expirací) */}
                  {canEditDynamicPrice && (
                    <div className="pt-3 border-t border-slate-800/80 flex flex-wrap items-center justify-between gap-3">
                      <div className="flex items-center gap-2 text-xs text-slate-300">
                        <Tag className="w-3.5 h-3.5 text-emerald-400" />
                        <span>Sleva před expirací (ActionPrice):</span>
                        <input
                          type="number"
                          step="0.1"
                          value={actionPriceInputs[p.id] || ""}
                          onChange={(e) =>
                            setActionPriceInputs((prev) => ({
                              ...prev,
                              [p.id]: e.target.value,
                            }))
                          }
                          className="w-24 rounded-lg bg-slate-900 border border-slate-700 px-2.5 py-1 text-xs font-mono text-white"
                        />
                        <span>Kč</span>
                      </div>

                      <div className="flex items-center gap-2">
                        <button
                          type="button"
                          disabled={saving}
                          onClick={() => handleApplyActionPrice(p, true)}
                          className="px-3 py-1.5 rounded-lg bg-emerald-500/20 hover:bg-emerald-500/30 border border-emerald-500/40 text-emerald-300 text-xs font-bold flex items-center gap-1"
                        >
                          <Sparkles className="w-3.5 h-3.5" />
                          Zapnout akci & Překreslit E-ink
                        </button>
                        {p.actionPrice !== null && (
                          <button
                            type="button"
                            disabled={saving}
                            onClick={() => handleApplyActionPrice(p, false)}
                            className="px-2.5 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 text-xs"
                          >
                            Zrušit akci
                          </button>
                        )}
                      </div>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      </div>

      {/* MODÁLNÍ OKNO PRO NASKLADNĚNÍ POLOŽKY (ExternalVendor) */}
      {restockModalProduct && (
        <div className="fixed inset-0 z-50 bg-slate-950/80 backdrop-blur-sm flex items-center justify-center p-4">
          <div className="max-w-md w-full rounded-3xl bg-slate-900 border border-emerald-500/50 p-6 space-y-4 shadow-2xl">
            <h3 className="text-lg font-bold text-white">
              Naskladnit: {restockModalProduct.name}
            </h3>
            <div className="space-y-3 text-xs">
              <div>
                <label className="block text-slate-400 mb-1">
                  Dodané množství (ks):
                </label>
                <input
                  type="number"
                  min={1}
                  value={restockQty}
                  onChange={(e) =>
                    setRestockQty(Math.max(1, parseInt(e.target.value, 10) || 1))
                  }
                  className="w-full rounded-xl bg-slate-950 border border-slate-800 px-3.5 py-2.5 text-base font-mono font-bold text-white"
                />
              </div>
              <div>
                <label className="block text-slate-400 mb-1">
                  Datum expirace šarže (volitelné):
                </label>
                <input
                  type="date"
                  value={restockExpDate}
                  onChange={(e) => setRestockExpDate(e.target.value)}
                  className="w-full rounded-xl bg-slate-950 border border-slate-800 px-3.5 py-2 text-sm text-white"
                />
              </div>
            </div>
            <div className="flex flex-col gap-2 pt-2">
              <button
                type="button"
                disabled={saving}
                onClick={() =>
                  handleAddToBatch(
                    restockModalProduct,
                    restockQty,
                    restockExpDate
                  )
                }
                className="w-full py-3 rounded-xl bg-emerald-500 hover:bg-emerald-400 text-slate-950 text-xs font-black flex items-center justify-center gap-1.5"
              >
                <ClipboardList className="w-4 h-4" />
                Přidat na dodací list (+{restockQty} ks)
              </button>
              <div className="flex gap-2">
                <button
                  type="button"
                  onClick={() => setRestockModalProduct(null)}
                  className="flex-1 py-2.5 rounded-xl bg-slate-800 text-slate-300 text-xs font-bold"
                >
                  Zrušit
                </button>
                <button
                  type="button"
                  disabled={saving}
                  onClick={() =>
                    handleSaveRestock(restockModalProduct, restockQty)
                  }
                  className="flex-1 py-2.5 rounded-xl bg-slate-800 hover:bg-slate-700 text-emerald-300 border border-slate-700 text-xs font-bold"
                >
                  {saving ? "Ukládám..." : `Rychle vystavit jen tuto 1 položku`}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
