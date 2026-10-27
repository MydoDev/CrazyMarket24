"use client";

import React, { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { QRCodeSVG } from "qrcode.react";
import {
  ShieldCheck,
  Sparkles,
  DoorOpen,
  RefreshCw,
  Award,
  Receipt,
  UserCheck,
  AlertTriangle,
  Clock,
  CheckCircle2,
  ArrowLeft,
  Fingerprint,
} from "lucide-react";
import { api } from "@/lib/api";
import { createStoreHubConnection } from "@/lib/signalr";
import type {
  CompletedOrderDto,
  CustomerDto,
  CustomerOrderHistoryDto,
  GeneratedEntryToken,
} from "@/lib/types";

export default function CustomerPassPage() {
  const [customers, setCustomers] = useState<CustomerDto[]>([]);
  const [selectedId, setSelectedId] = useState<string>("");
  const [token, setToken] = useState<GeneratedEntryToken | null>(null);
  const [secondsLeft, setSecondsLeft] = useState<number>(30);
  const [orders, setOrders] = useState<CustomerOrderHistoryDto[]>([]);
  const [loading, setLoading] = useState<boolean>(true);
  const [doorFeedback, setDoorFeedback] = useState<{
    type: "success" | "error";
    text: string;
  } | null>(null);
  const [scanningDoor, setScanningDoor] = useState<boolean>(false);

  const selectedCustomer =
    customers.find((c) => c.id === selectedId) || customers[0] || null;

  const loadCustomers = useCallback(async () => {
    try {
      const list = await api.getCustomers();
      setCustomers(list);
      setSelectedId((prev) => prev || list[0]?.id || "");
    } catch (e) {
      console.error("Nepodařilo se načíst zákazníky:", e);
    } finally {
      setLoading(false);
    }
  }, []);

  const refreshTotpAndHistory = useCallback(async (customerId: string) => {
    if (!customerId) return;
    try {
      const [newToken, orderHistory] = await Promise.all([
        api.getCustomerTotp(customerId),
        api.getCustomerOrders(customerId),
      ]);
      setToken(newToken);
      setSecondsLeft(Math.max(1, newToken.remainingSeconds));
      setOrders(orderHistory);
    } catch (e) {
      console.error("Chyba při obnově TOTP tokenu:", e);
    }
  }, []);

  useEffect(() => {
    loadCustomers();
  }, [loadCustomers]);

  useEffect(() => {
    if (!selectedId) return;
    refreshTotpAndHistory(selectedId);
  }, [selectedId, refreshTotpAndHistory]);

  // 1s odpočet TOTP kódu s automatickou obnovou při dosažení 0
  useEffect(() => {
    if (!selectedId) return;
    const interval = setInterval(() => {
      setSecondsLeft((prev) => {
        if (prev <= 1) {
          refreshTotpAndHistory(selectedId);
          return 30;
        }
        return prev - 1;
      });
    }, 1000);

    return () => clearInterval(interval);
  }, [selectedId, refreshTotpAndHistory]);

  // Real-time aktualizace věrnostních bodů a účtenek přes SignalR
  useEffect(() => {
    const conn = createStoreHubConnection("kiosk");
    conn.on("OrderCompleted", (completed: CompletedOrderDto) => {
      loadCustomers();
      if (completed.customerId === selectedId) {
        refreshTotpAndHistory(selectedId);
      }
    });

    conn.start().catch(() => {});
    return () => {
      conn.stop().catch(() => {});
    };
  }, [selectedId, loadCustomers, refreshTotpAndHistory]);

  const handleSimulateDoorScan = async () => {
    if (!token) return;
    setScanningDoor(true);
    setDoorFeedback(null);
    try {
      const res = await api.scanDoorEntry(token.qrPayload);
      setDoorFeedback({
        type: "success",
        text: `${res.welcomeMessage} (Dveře odemčeny na 5 s)`,
      });
    } catch (err) {
      setDoorFeedback({
        type: "error",
        text:
          err instanceof Error
            ? err.message
            : "Nepodařilo se ověřit vstupní QR kód.",
      });
    } finally {
      setScanningDoor(false);
    }
  };

  const progressPercent = Math.min(100, Math.max(0, (secondsLeft / 30) * 100));

  return (
    <div className="min-h-screen bg-[#070b12] text-slate-100 flex flex-col items-center py-6 px-4">
      <div className="w-full max-w-md space-y-5">
        {/* Horní navigace */}
        <div className="flex items-center justify-between">
          <Link
            href="/"
            className="inline-flex items-center gap-1.5 text-xs text-slate-400 hover:text-emerald-400 transition-colors"
          >
            <ArrowLeft className="w-4 h-4" />
            Rozcestník
          </Link>
          <div className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full bg-emerald-500/10 border border-emerald-500/30 text-emerald-400 text-[11px] font-semibold">
            <Sparkles className="w-3 h-3" />
            CrazyMarket 24 Pass
          </div>
          <Link
            href="/kiosk"
            className="text-xs font-medium text-emerald-400 hover:underline"
          >
            Pokladna →
          </Link>
        </div>

        {/* Přepínač profilu zákazníka + BankID OIDC */}
        <div className="rounded-2xl bg-slate-900/90 border border-slate-800 p-4 space-y-3">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-slate-400 flex items-center gap-1.5">
              <UserCheck className="w-3.5 h-3.5 text-emerald-400" />
              Aktivní identita (BankID profil)
            </span>
            <a
              href={`/api/auth/bankid?action=authorize&customerId=${selectedId}`}
              className="inline-flex items-center gap-1 text-[11px] px-2.5 py-1 rounded-lg bg-slate-800 hover:bg-slate-700 text-emerald-400 border border-slate-700 transition-colors"
            >
              <Fingerprint className="w-3 h-3" />
              BankID OIDC
            </a>
          </div>

          <div className="grid grid-cols-2 gap-2">
            {customers.map((c) => {
              const active = c.id === selectedCustomer?.id;
              return (
                <button
                  key={c.id}
                  type="button"
                  onClick={() => {
                    setSelectedId(c.id);
                    setDoorFeedback(null);
                  }}
                  className={`text-left p-3 rounded-xl border transition-all ${
                    active
                      ? "bg-emerald-500/15 border-emerald-500 text-white shadow-lg shadow-emerald-500/10"
                      : "bg-slate-950/60 border-slate-800 text-slate-400 hover:border-slate-700"
                  }`}
                >
                  <div className="flex items-center justify-between">
                    <span className="font-semibold text-sm truncate">
                      {c.fullName}
                    </span>
                    <span
                      className={`text-[10px] font-bold px-1.5 py-0.5 rounded ${
                        c.isAdultToday
                          ? "bg-emerald-500/20 text-emerald-300 border border-emerald-500/30"
                          : "bg-amber-500/20 text-amber-300 border border-amber-500/30"
                      }`}
                    >
                      {c.isAdultToday ? "18+" : "16 let"}
                    </span>
                  </div>
                  <div className="text-[11px] text-slate-400 mt-1">
                    {c.loyaltyPoints} věrnostních bodů
                  </div>
                </button>
              );
            })}
          </div>
        </div>

        {/* Hlavní karta s dynamickým TOTP QR kódem */}
        <div className="rounded-3xl bg-gradient-to-b from-slate-900 via-slate-900/95 to-slate-950 border border-emerald-500/30 p-6 shadow-2xl shadow-emerald-950/30 space-y-5">
          {selectedCustomer ? (
            <>
              <div className="flex items-start justify-between">
                <div>
                  <div className="flex items-center gap-2">
                    <h1 className="text-xl font-bold text-white">
                      {selectedCustomer.fullName}
                    </h1>
                    {selectedCustomer.isAdultToday ? (
                      <span className="inline-flex items-center gap-1 text-[11px] font-semibold px-2 py-0.5 rounded-full bg-emerald-500/15 text-emerald-400 border border-emerald-500/30">
                        <ShieldCheck className="w-3 h-3" /> Ověřeno 18+
                      </span>
                    ) : (
                      <span className="inline-flex items-center gap-1 text-[11px] font-semibold px-2 py-0.5 rounded-full bg-amber-500/15 text-amber-300 border border-amber-500/30">
                        <AlertTriangle className="w-3 h-3" /> Mladistvý (&lt;18)
                      </span>
                    )}
                  </div>
                  <p className="text-xs text-slate-400 mt-0.5">
                    Dynamický klíč pro vstup u dveří i identifikaci na pokladně
                  </p>
                </div>
                <button
                  type="button"
                  onClick={() => refreshTotpAndHistory(selectedCustomer.id)}
                  className="p-2 rounded-xl bg-slate-800/80 hover:bg-slate-800 text-slate-300 transition-colors"
                  title="Obnovit TOTP"
                >
                  <RefreshCw className="w-4 h-4" />
                </button>
              </div>

              {/* QR kód na bílém podkladu pro spolehlivé čtení skenerem */}
              <div className="flex flex-col items-center justify-center bg-white rounded-2xl p-5 shadow-inner">
                {token ? (
                  <QRCodeSVG
                    value={token.qrPayload}
                    size={210}
                    level="M"
                    includeMargin={false}
                  />
                ) : (
                  <div className="w-[210px] h-[210px] flex items-center justify-center text-slate-400 text-sm">
                    Generuji TOTP...
                  </div>
                )}

                {token && (
                  <div className="mt-3 text-center">
                    <div className="text-[11px] uppercase tracking-widest text-slate-500 font-semibold">
                      Jednorázový TOTP kód
                    </div>
                    <div className="font-mono text-2xl font-black tracking-[0.25em] text-slate-900 mt-0.5">
                      {token.totpCode.slice(0, 3)} {token.totpCode.slice(3)}
                    </div>
                  </div>
                )}
              </div>

              {/* 30s odpočet + Progress Bar */}
              <div className="space-y-2">
                <div className="flex items-center justify-between text-xs">
                  <span className="text-slate-400 flex items-center gap-1.5">
                    <Clock className="w-3.5 h-3.5 text-emerald-400" />
                    Platnost dynamického QR kódu
                  </span>
                  <span className="font-mono font-bold text-emerald-400">
                    {secondsLeft} s
                  </span>
                </div>
                <div className="w-full h-2 rounded-full bg-slate-800 overflow-hidden">
                  <div
                    className={`h-full transition-all duration-1000 rounded-full ${
                      secondsLeft <= 5 ? "bg-amber-400" : "bg-emerald-500"
                    }`}
                    style={{ width: `${progressPercent}%` }}
                  />
                </div>
                {token && (
                  <p className="text-[10px] font-mono text-slate-500 truncate text-center">
                    {token.qrPayload}
                  </p>
                )}
              </div>

              {/* Tlačítko pro simulaci pípnutí u dveří v dev režimu */}
              <button
                type="button"
                onClick={handleSimulateDoorScan}
                disabled={!token || scanningDoor}
                className="w-full py-3.5 px-4 rounded-xl bg-emerald-500 hover:bg-emerald-400 disabled:opacity-50 text-slate-950 font-bold text-sm flex items-center justify-center gap-2 shadow-lg shadow-emerald-500/20 transition-all active:scale-[0.99]"
              >
                <DoorOpen className="w-5 h-5" />
                {scanningDoor
                  ? "Ověřuji TOTP u dveří..."
                  : "Simulovat pípnutí u dveří"}
              </button>

              {doorFeedback && (
                <div
                  className={`rounded-xl p-3.5 text-xs flex items-start gap-2.5 border ${
                    doorFeedback.type === "success"
                      ? "bg-emerald-500/15 border-emerald-500/40 text-emerald-200"
                      : "bg-rose-500/15 border-rose-500/40 text-rose-200"
                  }`}
                >
                  <CheckCircle2 className="w-4 h-4 shrink-0 mt-0.5 text-emerald-400" />
                  <span>{doorFeedback.text}</span>
                </div>
              )}
            </>
          ) : (
            <div className="text-center py-8 text-sm text-slate-400">
              {loading ? "Načítám profil..." : "Žádný zákazník není k dispozici."}
            </div>
          )}
        </div>

        {/* Věrnostní body */}
        {selectedCustomer && (
          <div className="rounded-2xl bg-slate-900/90 border border-slate-800 p-5 flex items-center justify-between">
            <div className="flex items-center gap-3.5">
              <div className="w-12 h-12 rounded-2xl bg-emerald-500/15 border border-emerald-500/30 flex items-center justify-center text-emerald-400">
                <Award className="w-6 h-6" />
              </div>
              <div>
                <div className="text-xs text-slate-400">
                  Věrnostní konto CrazyMarket
                </div>
                <div className="text-2xl font-extrabold text-white">
                  {selectedCustomer.loyaltyPoints}{" "}
                  <span className="text-sm font-semibold text-emerald-400">
                    bodů (= {selectedCustomer.loyaltyPoints} Kč)
                  </span>
                </div>
              </div>
            </div>
            <span className="text-[11px] px-2.5 py-1 rounded-lg bg-slate-800 text-slate-300 font-medium">
              1 bod = 1 Kč
            </span>
          </div>
        )}

        {/* Historie nákupů */}
        <div className="rounded-2xl bg-slate-900/90 border border-slate-800 p-5 space-y-3">
          <div className="flex items-center justify-between">
            <h2 className="text-sm font-bold text-white flex items-center gap-2">
              <Receipt className="w-4 h-4 text-emerald-400" />
              Historie nákupů a účtenek
            </h2>
            <span className="text-xs text-slate-400">{orders.length} dokladů</span>
          </div>

          {orders.length === 0 ? (
            <p className="text-xs text-slate-500 py-4 text-center">
              Zatím žádné dokončené nákupy. Pípněte u dveří a nakupte na{" "}
              <Link href="/kiosk" className="text-emerald-400 underline">
                /kiosk
              </Link>
              .
            </p>
          ) : (
            <div className="space-y-2.5">
              {orders.map((ord) => (
                <div
                  key={ord.id}
                  className="rounded-xl bg-slate-950/70 border border-slate-800/90 p-3.5 space-y-2"
                >
                  <div className="flex items-center justify-between">
                    <span className="text-xs font-mono text-slate-400">
                      {new Date(ord.paidAt || ord.createdAt).toLocaleString(
                        "cs-CZ"
                      )}
                    </span>
                    <span className="text-sm font-bold text-emerald-400">
                      {ord.totalAmount.toFixed(2)} Kč
                    </span>
                  </div>
                  <div className="space-y-1">
                    {ord.items.map((item, idx) => (
                      <div
                        key={idx}
                        className="flex items-center justify-between text-xs text-slate-300"
                      >
                        <span className="truncate">
                          {item.quantity}× {item.productName}
                        </span>
                        <span className="font-mono text-slate-400">
                          {item.lineTotal.toFixed(2)} Kč
                        </span>
                      </div>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
