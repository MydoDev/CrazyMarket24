"use client";

import React, { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import {
  ShoppingBag,
  Scale,
  CreditCard,
  Award,
  Sun,
  Moon,
  ShieldAlert,
  CheckCircle2,
  AlertTriangle,
  Sparkles,
  Barcode,
  UserCheck,
  RotateCcw,
  Plus,
  Wrench,
  Wifi,
  WifiOff,
  QrCode,
} from "lucide-react";
import { api } from "@/lib/api";
import { createStoreHubConnection } from "@/lib/signalr";
import type {
  CheckoutStateDto,
  CompletedOrderDto,
  CustomerDto,
  KioskErrorEvent,
  ProductDto,
  ScaleWeightEvent,
} from "@/lib/types";

export default function SelfCheckoutKioskPage() {
  const [state, setState] = useState<CheckoutStateDto | null>(null);
  const [products, setProducts] = useState<ProductDto[]>([]);
  const [customers, setCustomers] = useState<CustomerDto[]>([]);
  const [hubConnected, setHubConnected] = useState<boolean>(false);
  const [errorBanner, setErrorBanner] = useState<string | null>(null);
  const [completedOrder, setCompletedOrder] =
    useState<CompletedOrderDto | null>(null);
  const [lastScannedItem, setLastScannedItem] = useState<{
    name: string;
    weightGrams: number;
  } | null>(null);

  // Dev Toolbar stav
  const [devPickerOpen, setDevPickerOpen] = useState<boolean>(false);
  const [devBusy, setDevBusy] = useState<boolean>(false);

  // Buffer pro globální HID čtečku čárových kódů
  const barcodeBufferRef = useRef<string>("");
  const lastKeyTimeRef = useRef<number>(0);

  const loadInitialData = useCallback(async () => {
    try {
      const [kioskState, prodList, custList] = await Promise.all([
        api.getKioskState(),
        api.getProducts(),
        api.getCustomers(),
      ]);
      setState(kioskState);
      setProducts(prodList);
      setCustomers(custList);
    } catch (e) {
      console.error("Nepodařilo se načíst počáteční stav pokladny:", e);
    }
  }, []);

  useEffect(() => {
    loadInitialData();
  }, [loadInitialData]);

  // Real-time SignalR spojení na StoreHub
  useEffect(() => {
    const conn = createStoreHubConnection("kiosk");

    conn.on("CheckoutStateChanged", (newState: CheckoutStateDto) => {
      setState(newState);
      if (newState.status === "Ready") {
        setErrorBanner(null);
      }
    });

    conn.on("ScaleWeightUpdated", (evt: ScaleWeightEvent) => {
      setState((prev) =>
        prev
          ? {
              ...prev,
              currentScaleWeightGrams: evt.weightGrams,
            }
          : prev
      );
    });

    conn.on("KioskError", (err: KioskErrorEvent) => {
      setErrorBanner(err.message);
    });

    conn.on("OrderCompleted", (order: CompletedOrderDto) => {
      setCompletedOrder(order);
      setErrorBanner(null);
    });

    conn.onreconnected(() => setHubConnected(true));
    conn.onclose(() => setHubConnected(false));

    conn
      .start()
      .then(() => setHubConnected(true))
      .catch(() => setHubConnected(false));

    return () => {
      conn.stop().catch(() => {});
    };
  }, []);

  const triggerScanCode = useCallback(
    async (code: string) => {
      const trimmed = code.trim();
      if (!trimmed) return;

      setErrorBanner(null);
      setCompletedOrder(null);

      // Najdeme produkt pro zobrazení v modální výzvě váhy
      const matchedProduct = products.find((p) => p.ean === trimmed);
      if (matchedProduct) {
        setLastScannedItem({
          name: matchedProduct.name,
          weightGrams: matchedProduct.weightGrams,
        });
      }

      try {
        const res = await api.kioskScanEan(trimmed);
        if (res.state) {
          setState(res.state);
        }
        if (!res.success) {
          setErrorBanner(res.message);
        }
      } catch (err) {
        setErrorBanner(
          err instanceof Error ? err.message : "Chyba při komunikaci s pokladnou."
        );
      }
    },
    [products]
  );

  // Globální HID Barcode Scanner Listener (window keydown -> číslice/znaky ukončené Enter)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      // Ignorujeme pokud uživatel píše do inputu
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
      if (now - lastKeyTimeRef.current > 1200) {
        barcodeBufferRef.current = "";
      }
      lastKeyTimeRef.current = now;

      if (e.key === "Enter") {
        const code = barcodeBufferRef.current.trim();
        barcodeBufferRef.current = "";
        if (code.length >= 4) {
          e.preventDefault();
          triggerScanCode(code);
        }
        return;
      }

      if (e.key.length === 1 && !e.ctrlKey && !e.altKey && !e.metaKey) {
        barcodeBufferRef.current += e.key;
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [triggerScanCode]);

  const handleToggleLoyaltyPoints = async () => {
    if (!state) return;
    setErrorBanner(null);
    const nextPoints =
      state.redeemedPoints > 0 ? 0 : state.customerAvailablePoints;
    const res = await api.kioskApplyPoints(nextPoints);
    if (res.state) setState(res.state);
    if (!res.success) setErrorBanner(res.message);
  };

  const handleStartPayment = async () => {
    setErrorBanner(null);
    const res = await api.kioskPay();
    if (res.state) setState(res.state);
    if (res.completedOrder) {
      setCompletedOrder(res.completedOrder);
    }
    if (!res.success) {
      setErrorBanner(res.message);
    }
  };

  const handleResetKiosk = async () => {
    setErrorBanner(null);
    setCompletedOrder(null);
    setLastScannedItem(null);
    const fresh = await api.kioskReset();
    setState(fresh);
    await api.devSetScaleWeight(0).catch(() => {});
  };

  // --- Dev Toolbar akce ---
  const handleDevAutoWeight = async () => {
    if (!state) return;
    setDevBusy(true);
    try {
      await api.devSetScaleWeight(state.expectedWeightGrams);
      const updated = await api.getKioskState();
      setState(updated);
    } finally {
      setDevBusy(false);
    }
  };

  const handleDevApproveCard = async () => {
    setDevBusy(true);
    try {
      await api.devResolveTerminal(true);
    } catch (err) {
      setErrorBanner(
        err instanceof Error
          ? err.message
          : "Na terminálu zatím neprobíhá žádná čekající platba (nejprve klikněte na Zaplatit)."
      );
    } finally {
      setDevBusy(false);
    }
  };

  const handleDevIdentifyCustomer = async (customerId: string) => {
    setDevBusy(true);
    setDevPickerOpen(false);
    try {
      const totp = await api.getCustomerTotp(customerId);
      // Zajistíme, že je zákazník evidován v prodejně a následně ho přihlásíme na pokladně
      await api.scanDoorEntry(totp.qrPayload).catch(() => {});
      await triggerScanCode(totp.qrPayload);
    } finally {
      setDevBusy(false);
    }
  };

  const isWaitingForWeight = state?.status === "WaitingForWeightVerification";
  const isPaying = state?.status === "Paying";
  const lastCartItem =
    lastScannedItem ||
    (state && state.items.length > 0
      ? {
          name: state.items[state.items.length - 1].name,
          weightGrams: state.items[state.items.length - 1].unitWeightGrams,
        }
      : null);

  return (
    <div className="min-h-screen bg-[#060a10] text-slate-100 flex flex-col justify-between select-none relative overflow-hidden">
      {/* HORNÍ HLAVIČKA KIOSKU */}
      <header className="bg-slate-900/95 border-b border-slate-800 px-6 py-4 flex items-center justify-between gap-4">
        <div className="flex items-center gap-4">
          <Link
            href="/"
            className="flex items-center gap-2.5 font-extrabold text-xl tracking-tight text-white"
          >
            <span className="w-9 h-9 rounded-xl bg-emerald-500 text-slate-950 flex items-center justify-center font-black">
              24
            </span>
            <span>
              CrazyMarket <span className="text-emerald-400">Kiosk</span>
            </span>
          </Link>

          {/* Indikace denního / nočního tarifu */}
          {state?.isNightPricing ? (
            <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-indigo-500/15 border border-indigo-400/40 text-indigo-300 text-xs font-semibold">
              <Moon className="w-3.5 h-3.5" />
              Noční tarif (22:00–05:00)
            </span>
          ) : (
            <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-emerald-500/15 border border-emerald-500/30 text-emerald-300 text-xs font-semibold">
              <Sun className="w-3.5 h-3.5" />
              Denní ceny aktivní
            </span>
          )}
        </div>

        {/* Informace o přihlášeném zákazníkovi v prodejně */}
        <div className="flex items-center gap-4">
          {state?.customerName ? (
            <div className="flex items-center gap-3 bg-slate-950/90 border border-emerald-500/40 rounded-2xl px-4 py-2">
              <UserCheck className="w-5 h-5 text-emerald-400" />
              <div>
                <div className="flex items-center gap-2">
                  <span className="font-bold text-sm text-white">
                    {state.customerName}
                  </span>
                  <span
                    className={`text-[10px] font-bold px-1.5 py-0.5 rounded ${
                      state.customerIsAdult
                        ? "bg-emerald-500/20 text-emerald-300"
                        : "bg-amber-500/20 text-amber-300"
                    }`}
                  >
                    {state.customerIsAdult ? "18+" : "<18"}
                  </span>
                </div>
                <div className="text-xs text-emerald-400 font-semibold flex items-center gap-1">
                  <Award className="w-3.5 h-3.5" />
                  {state.customerAvailablePoints} věrnostních bodů
                </div>
              </div>
            </div>
          ) : (
            <div className="flex items-center gap-2.5 bg-slate-950/80 border border-slate-800 rounded-2xl px-4 py-2 text-xs text-slate-400">
              <QrCode className="w-4 h-4 text-emerald-400" />
              <span>
                Pípněte zboží (Auto-assign) nebo přiložte svůj zákaznický QR kód
              </span>
            </div>
          )}

          {/* Stav SignalR */}
          <div
            className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-xs font-medium border ${
              hubConnected
                ? "bg-emerald-500/10 border-emerald-500/30 text-emerald-400"
                : "bg-amber-500/10 border-amber-500/30 text-amber-400"
            }`}
          >
            {hubConnected ? (
              <>
                <Wifi className="w-3.5 h-3.5" /> Live
              </>
            ) : (
              <>
                <WifiOff className="w-3.5 h-3.5" /> Offline
              </>
            )}
          </div>

          <button
            type="button"
            onClick={handleResetKiosk}
            className="p-2.5 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-300 transition-colors"
            title="Vynulovat pokladnu"
          >
            <RotateCcw className="w-4 h-4" />
          </button>
        </div>
      </header>

      {/* CHYBOVÁ LIŠTA (např. více lidí v prodejně, zamítnutí 18+, nesoulad váhy) */}
      {errorBanner && (
        <div className="mx-6 mt-4 rounded-2xl bg-rose-500/15 border-2 border-rose-500/60 px-5 py-3.5 flex items-center justify-between gap-4 animate-pulse">
          <div className="flex items-center gap-3 text-rose-200 font-semibold text-sm">
            <ShieldAlert className="w-6 h-6 text-rose-400 shrink-0" />
            <span>{errorBanner}</span>
          </div>
          <button
            type="button"
            onClick={() => setErrorBanner(null)}
            className="text-xs px-3 py-1 rounded-lg bg-rose-500/20 hover:bg-rose-500/30 text-rose-200"
          >
            Skrýt
          </button>
        </div>
      )}

      {/* HLAVNÍ OBSAH POKLADNY (2 sloupce: Košík vlevo | Váhový semafor a Platba vpravo) */}
      <main className="flex-1 grid grid-cols-1 lg:grid-cols-12 gap-6 p-6 overflow-hidden">
        {/* LEVÝ SLOUPEC: Seznam položek v košíku (7/12) */}
        <section className="lg:col-span-7 rounded-3xl bg-slate-900/80 border border-slate-800 flex flex-col overflow-hidden">
          <div className="px-6 py-4 border-b border-slate-800 flex items-center justify-between">
            <div className="flex items-center gap-2.5">
              <ShoppingBag className="w-5 h-5 text-emerald-400" />
              <h2 className="text-lg font-bold text-white">Nákupní košík</h2>
              <span className="text-xs px-2.5 py-0.5 rounded-full bg-slate-800 text-slate-300 font-semibold">
                {state?.items.reduce((s, i) => s + i.quantity, 0) || 0} ks
              </span>
            </div>
            <div className="flex items-center gap-2 text-xs text-slate-400">
              <Barcode className="w-4 h-4 text-emerald-400" />
              <span>HID skener připraven (sleduje klávesnici)</span>
            </div>
          </div>

          <div className="flex-1 overflow-y-auto p-6 space-y-3">
            {!state || state.items.length === 0 ? (
              <div className="h-full min-h-[300px] flex flex-col items-center justify-center text-center space-y-3 text-slate-500">
                <div className="w-16 h-16 rounded-3xl bg-slate-800/60 border border-slate-700/50 flex items-center justify-center text-emerald-400">
                  <Barcode className="w-8 h-8" />
                </div>
                <div className="text-lg font-bold text-slate-300">
                  Košík je zatím prázdný
                </div>
                <p className="text-xs max-w-sm text-slate-400">
                  Pípněte první zboží čtečkou čárových kódů (nebo použijte{" "}
                  <strong className="text-emerald-400">Dev Toolbar</strong>{" "}
                  vpravo dole).
                </p>
              </div>
            ) : (
              state.items.map((item) => {
                const prodMeta = products.find((p) => p.id === item.productId);
                const isAction = prodMeta?.activePriceTier === "Action";

                return (
                  <div
                    key={item.productId}
                    className="rounded-2xl bg-slate-950/80 border border-slate-800/90 px-5 py-4 flex items-center justify-between gap-4"
                  >
                    <div className="space-y-1">
                      <div className="flex items-center gap-2 flex-wrap">
                        <span className="font-bold text-base text-white">
                          {item.name}
                        </span>
                        {isAction && (
                          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md bg-emerald-500 text-slate-950 text-[10px] font-black uppercase">
                            <Sparkles className="w-3 h-3" /> Akční sleva
                          </span>
                        )}
                        {item.isAgeRestricted && (
                          <span className="px-2 py-0.5 rounded-md bg-rose-500/20 border border-rose-500/40 text-rose-300 text-[10px] font-bold">
                            18+
                          </span>
                        )}
                        <span className="px-2 py-0.5 rounded-md bg-emerald-500/10 border border-emerald-500/30 text-emerald-300 text-[11px] font-semibold">
                          +{item.bonusPointsPerUnit * item.quantity} bodů
                        </span>
                      </div>
                      <div className="text-xs text-slate-400 flex items-center gap-3 font-mono">
                        <span>EAN: {item.ean}</span>
                        <span>•</span>
                        <span>
                          Hmotnost: {item.unitWeightGrams * item.quantity} g (±
                          {item.unitToleranceGrams * item.quantity} g)
                        </span>
                      </div>
                    </div>

                    <div className="text-right shrink-0">
                      <div className="text-lg font-extrabold text-white">
                        {item.lineTotal.toFixed(2)} Kč
                      </div>
                      <div className="text-xs text-slate-400">
                        {item.quantity} × {item.unitPrice.toFixed(2)} Kč
                      </div>
                    </div>
                  </div>
                );
              })
            )}
          </div>

          <div className="px-6 py-3.5 bg-slate-950/90 border-t border-slate-800 text-xs text-slate-400 flex items-center justify-between">
            <span>Stav pokladny: {state?.statusMessage || "Připraveno"}</span>
            {state && state.totalBonusPointsToEarn > 0 && (
              <span className="text-emerald-400 font-semibold">
                Za tento nákup získáte +{state.totalBonusPointsToEarn} bodů
              </span>
            )}
          </div>
        </section>

        {/* PRAVÝ SLOUPEC: Váhový Semafor + Souhrn a Platba (5/12) */}
        <section className="lg:col-span-5 flex flex-col justify-between gap-6">
          {/* VÁHOVÝ SEMAFOR (Kritický UI prvek) */}
          <div
            className={`rounded-3xl p-6 border-2 transition-all ${
              !state || state.items.length === 0
                ? "bg-slate-900/80 border-slate-800"
                : state.isWeightVerified
                ? "bg-emerald-950/30 border-emerald-500/70 shadow-xl shadow-emerald-500/10"
                : "bg-amber-950/40 border-amber-500 shadow-xl shadow-amber-500/15"
            }`}
          >
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-3">
                <div
                  className={`w-12 h-12 rounded-2xl flex items-center justify-center ${
                    !state || state.items.length === 0
                      ? "bg-slate-800 text-slate-400"
                      : state.isWeightVerified
                      ? "bg-emerald-500 text-slate-950"
                      : "bg-amber-500 text-slate-950 animate-bounce"
                  }`}
                >
                  <Scale className="w-6 h-6" />
                </div>
                <div>
                  <div className="text-xs uppercase tracking-wider font-bold text-slate-400">
                    Kontrolní RS232 váha
                  </div>
                  <div className="text-lg font-extrabold text-white">
                    {!state || state.items.length === 0
                      ? "Odkládací plocha prázdná"
                      : state.isWeightVerified
                      ? "Hmotnost košíku souhlasí"
                      : "Čekám na položení zboží na váhu!"}
                  </div>
                </div>
              </div>

              {/* Indikační semafor */}
              <div className="flex items-center gap-2">
                <span
                  className={`w-4 h-4 rounded-full ${
                    state && state.items.length > 0 && !state.isWeightVerified
                      ? "bg-amber-400 shadow-lg shadow-amber-400/80"
                      : "bg-slate-800"
                  }`}
                />
                <span
                  className={`w-4 h-4 rounded-full ${
                    state && state.items.length > 0 && state.isWeightVerified
                      ? "bg-emerald-400 shadow-lg shadow-emerald-400/80"
                      : "bg-slate-800"
                  }`}
                />
              </div>
            </div>

            <div className="mt-5 grid grid-cols-2 gap-3">
              <div className="rounded-2xl bg-slate-950/80 border border-slate-800 p-3.5">
                <div className="text-[11px] text-slate-400">
                  Aktuálně na váze
                </div>
                <div className="text-2xl font-mono font-black text-white mt-0.5">
                  {state?.currentScaleWeightGrams ?? 0} g
                </div>
              </div>
              <div className="rounded-2xl bg-slate-950/80 border border-slate-800 p-3.5">
                <div className="text-[11px] text-slate-400">
                  Očekávaná hmotnost
                </div>
                <div className="text-2xl font-mono font-black text-emerald-400 mt-0.5">
                  {state?.expectedWeightGrams ?? 0} g{" "}
                  <span className="text-xs font-normal text-slate-400">
                    (±{state?.totalToleranceGrams ?? 0} g)
                  </span>
                </div>
              </div>
            </div>
          </div>

          {/* SOUHRN CENY, VĚRNOSTNÍ BODY A TLAČÍTKO ZAPLATIT */}
          <div className="rounded-3xl bg-slate-900/90 border border-slate-800 p-6 space-y-5 flex-1 flex flex-col justify-between">
            <div className="space-y-4">
              <div className="flex items-center justify-between text-sm text-slate-400">
                <span>Mezisoučet položek</span>
                <span className="font-mono font-semibold text-slate-200">
                  {(state?.subtotalAmount ?? 0).toFixed(2)} Kč
                </span>
              </div>

              {/* Uplatnění věrnostních bodů */}
              {state && state.customerId && state.customerAvailablePoints > 0 && (
                <div className="rounded-2xl bg-slate-950/80 border border-emerald-500/30 p-3.5 flex items-center justify-between gap-3">
                  <div>
                    <div className="text-xs font-bold text-emerald-400 flex items-center gap-1.5">
                      <Award className="w-4 h-4" />
                      Věrnostní body ({state.customerAvailablePoints} b. k
                      dispozici)
                    </div>
                    <div className="text-[11px] text-slate-400">
                      {state.redeemedPoints > 0
                        ? `Uplatněna sleva -${state.pointsDiscountAmount.toFixed(
                            2
                          )} Kč`
                        : "Klikněte pro uplatnění bodů na tento nákup"}
                    </div>
                  </div>
                  <button
                    type="button"
                    disabled={state.items.length === 0 || isPaying}
                    onClick={handleToggleLoyaltyPoints}
                    className={`px-3.5 py-2 rounded-xl text-xs font-bold transition-all ${
                      state.redeemedPoints > 0
                        ? "bg-slate-800 text-slate-200 hover:bg-slate-700"
                        : "bg-emerald-500 text-slate-950 hover:bg-emerald-400"
                    }`}
                  >
                    {state.redeemedPoints > 0
                      ? "Zrušit body"
                      : "Uplatnit body"}
                  </button>
                </div>
              )}

              <div className="pt-4 border-t border-slate-800 flex items-baseline justify-between">
                <span className="text-base font-bold text-slate-300">
                  Celkem k úhradě
                </span>
                <span className="text-4xl font-black text-emerald-400 font-mono">
                  {(state?.finalAmountToPay ?? 0).toFixed(2)} Kč
                </span>
              </div>
            </div>

            <button
              type="button"
              onClick={handleStartPayment}
              disabled={
                !state ||
                state.items.length === 0 ||
                isWaitingForWeight ||
                isPaying
              }
              className="w-full py-5 px-6 rounded-2xl bg-emerald-500 hover:bg-emerald-400 disabled:bg-slate-800 disabled:text-slate-500 text-slate-950 font-black text-lg flex items-center justify-center gap-3 shadow-xl shadow-emerald-500/20 transition-all active:scale-[0.99]"
            >
              <CreditCard className="w-6 h-6" />
              {isPaying
                ? "Přiložte kartu k terminálu..."
                : isWaitingForWeight
                ? "Nejprve položte zboží na váhu"
                : "Zaplatit kartou"}
            </button>
          </div>
        </section>
      </main>

      {/* KRITICKÝ MODÁLNÍ DIALOG: WaitingForWeightVerification */}
      {isWaitingForWeight && state && (
        <div className="fixed inset-0 z-40 bg-slate-950/85 backdrop-blur-md flex items-center justify-center p-6">
          <div className="max-w-xl w-full rounded-3xl bg-slate-900 border-2 border-amber-500 p-8 text-center space-y-6 shadow-2xl shadow-amber-500/20">
            <div className="w-20 h-20 rounded-3xl bg-amber-500/20 border border-amber-500/40 text-amber-400 flex items-center justify-center mx-auto animate-pulse">
              <Scale className="w-10 h-10" />
            </div>

            <div className="space-y-2">
              <span className="text-xs font-mono uppercase tracking-widest text-amber-400 font-bold">
                Kontrola hmotnosti na pokladní váze
              </span>
              <h2 className="text-2xl md:text-3xl font-black text-white leading-tight">
                Položte{" "}
                <span className="text-emerald-400">
                  {lastCartItem?.name || "naskenované zboží"}
                </span>{" "}
                ({lastCartItem?.weightGrams ?? state.expectedWeightGrams} g) do
                tašky na váze
              </h2>
              <p className="text-sm text-slate-400">
                Další skenování a platba se automaticky odemknou, jakmile se
                váha ustálí.
              </p>
            </div>

            <div className="grid grid-cols-2 gap-4 bg-slate-950/90 rounded-2xl p-4 border border-slate-800">
              <div>
                <div className="text-xs text-slate-400">Naměřeno na váze</div>
                <div className="text-3xl font-mono font-black text-amber-400">
                  {state.currentScaleWeightGrams} g
                </div>
              </div>
              <div>
                <div className="text-xs text-slate-400">
                  Očekáváno celkem (±{state.totalToleranceGrams} g)
                </div>
                <div className="text-3xl font-mono font-black text-emerald-400">
                  {state.expectedWeightGrams} g
                </div>
              </div>
            </div>

            {process.env.NODE_ENV === "development" && (
              <button
                type="button"
                onClick={handleDevAutoWeight}
                disabled={devBusy}
                className="w-full py-3.5 px-5 rounded-xl bg-amber-500 hover:bg-amber-400 text-slate-950 font-extrabold text-sm flex items-center justify-center gap-2 shadow-lg"
              >
                ⚖️ Položit zboží na váhu ({state.expectedWeightGrams} g –
                Auto-gramáž)
              </button>
            )}
          </div>
        </div>
      )}

      {/* MODÁLNÍ POTVRZENÍ DOKONČENÉHO NÁKUPU */}
      {completedOrder && (
        <div className="fixed inset-0 z-40 bg-slate-950/85 backdrop-blur-md flex items-center justify-center p-6">
          <div className="max-w-lg w-full rounded-3xl bg-slate-900 border-2 border-emerald-500 p-8 text-center space-y-5 shadow-2xl shadow-emerald-500/20">
            <div className="w-16 h-16 rounded-2xl bg-emerald-500 text-slate-950 flex items-center justify-center mx-auto">
              <CheckCircle2 className="w-10 h-10" />
            </div>
            <h2 className="text-2xl font-black text-white">
              Platba schválena! Děkujeme za nákup
            </h2>
            <p className="text-sm text-slate-300">
              Zákazník:{" "}
              <strong className="text-white">
                {completedOrder.customerName}
              </strong>{" "}
              • Uhrazeno:{" "}
              <strong className="text-emerald-400">
                {completedOrder.finalPaidAmount.toFixed(2)} Kč
              </strong>
            </p>
            <div className="rounded-2xl bg-slate-950 p-4 border border-slate-800 text-xs text-slate-400 space-y-1">
              <div>
                Získané body:{" "}
                <strong className="text-emerald-400">
                  +{completedOrder.earnedPoints} b.
                </strong>{" "}
                (Nový zůstatek: {completedOrder.newLoyaltyPointsBalance} b.)
              </div>
              <div className="truncate font-mono text-[11px] text-slate-500">
                POHODA XML: {completedOrder.pohodaXmlPath}
              </div>
            </div>
            <button
              type="button"
              onClick={handleResetKiosk}
              className="w-full py-3.5 rounded-xl bg-emerald-500 hover:bg-emerald-400 text-slate-950 font-bold text-sm"
            >
              Nový nákup (Další zákazník)
            </button>
          </div>
        </div>
      )}

      {/* PLOVOUCÍ DEV TOOLBAR V PRAVÉM DOLNÍM ROHU (jen v NODE_ENV === 'development') */}
      {process.env.NODE_ENV === "development" && (
        <div className="fixed bottom-4 right-4 z-50 flex flex-col items-end gap-2">
          {devPickerOpen && (
            <div className="w-96 max-h-96 overflow-y-auto rounded-2xl bg-slate-900/95 backdrop-blur-md border border-emerald-500/40 p-4 shadow-2xl space-y-3">
              <div className="flex items-center justify-between border-b border-slate-800 pb-2">
                <span className="text-xs font-bold uppercase tracking-wider text-emerald-400">
                  Rychlé pípnutí produktu nebo QR zákazníka
                </span>
                <button
                  type="button"
                  onClick={() => setDevPickerOpen(false)}
                  className="text-xs text-slate-400 hover:text-white"
                >
                  Zavřít ✕
                </button>
              </div>

              {/* Rychlá identifikace zákazníka u dveří + pokladny */}
              <div className="space-y-1.5">
                <div className="text-[11px] text-slate-400 font-semibold">
                  1. Přihlásit zákazníka (TOTP QR):
                </div>
                <div className="grid grid-cols-2 gap-1.5">
                  {customers.map((c) => (
                    <button
                      key={c.id}
                      type="button"
                      onClick={() => handleDevIdentifyCustomer(c.id)}
                      className="text-left px-2.5 py-2 rounded-xl bg-slate-950 hover:bg-emerald-500/15 border border-slate-800 hover:border-emerald-500/40 text-xs"
                    >
                      <div className="font-bold text-white truncate">
                        {c.fullName}
                      </div>
                      <div className="text-[10px] text-emerald-400">
                        {c.isAdultToday ? "18+ dospělý" : "16 let (mladistvý)"}
                      </div>
                    </button>
                  ))}
                </div>
              </div>

              {/* Seznam produktů */}
              <div className="space-y-1.5">
                <div className="text-[11px] text-slate-400 font-semibold">
                  2. Pípnout EAN produktu:
                </div>
                {products.map((p) => (
                  <button
                    key={p.id}
                    type="button"
                    onClick={() => {
                      setDevPickerOpen(false);
                      triggerScanCode(p.ean);
                    }}
                    className="w-full text-left px-3 py-2 rounded-xl bg-slate-950 hover:bg-emerald-500/15 border border-slate-800 hover:border-emerald-500/40 flex items-center justify-between text-xs transition-colors"
                  >
                    <div className="truncate pr-2">
                      <span className="font-semibold text-white">{p.name}</span>
                      {p.isAgeRestricted && (
                        <span className="ml-1.5 text-[10px] px-1.5 py-0.5 rounded bg-rose-500/20 text-rose-300 font-bold">
                          18+
                        </span>
                      )}
                      <div className="text-[10px] text-slate-400 font-mono">
                        {p.ean} • {p.weightGrams} g
                      </div>
                    </div>
                    <span className="font-mono font-bold text-emerald-400 shrink-0">
                      {p.activePrice.toFixed(2)} Kč
                    </span>
                  </button>
                ))}
              </div>
            </div>
          )}

          <div className="rounded-2xl bg-slate-900/95 backdrop-blur-md border border-emerald-500/40 p-2.5 shadow-2xl flex items-center gap-2">
            <span className="px-2 py-1 rounded-lg bg-emerald-500/15 text-emerald-400 text-[11px] font-mono font-bold flex items-center gap-1">
              <Wrench className="w-3.5 h-3.5" /> DEV
            </span>

            <button
              type="button"
              onClick={() => setDevPickerOpen((o) => !o)}
              className="px-3 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-xs font-bold text-white flex items-center gap-1.5 transition-colors"
            >
              <Plus className="w-3.5 h-3.5 text-emerald-400" />
              Pípnout produkt ze seznamu
            </button>

            <button
              type="button"
              onClick={handleDevAutoWeight}
              disabled={devBusy || !state}
              className="px-3 py-2 rounded-xl bg-amber-500/20 hover:bg-amber-500/30 border border-amber-500/40 text-amber-300 text-xs font-bold transition-colors"
            >
              ⚖️ Položit zboží na váhu ({state?.expectedWeightGrams ?? 0} g)
            </button>

            <button
              type="button"
              onClick={handleDevApproveCard}
              disabled={devBusy}
              className="px-3 py-2 rounded-xl bg-emerald-500/20 hover:bg-emerald-500/30 border border-emerald-500/40 text-emerald-300 text-xs font-bold transition-colors"
            >
              💳 Přiložit kartu (Approve)
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
