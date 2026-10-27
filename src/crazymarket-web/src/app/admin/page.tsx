"use client";

import React, { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import {
  ShieldAlert,
  Users,
  DoorOpen,
  Scale,
  Sun,
  Moon,
  Tag,
  FileCode2,
  RefreshCw,
  CheckCircle2,
  ArrowLeft,
  Wifi,
  WifiOff,
  LogOut,
  Eye,
} from "lucide-react";
import { api } from "@/lib/api";
import { createStoreHubConnection } from "@/lib/signalr";
import type {
  AdminDashboardDto,
  EslPreviewDto,
  ProductDto,
  ScanEntryResponse,
  ScaleWeightEvent,
} from "@/lib/types";

export default function AdminDashboardPage() {
  const [data, setData] = useState<AdminDashboardDto | null>(null);
  const [hubConnected, setHubConnected] = useState<boolean>(false);
  const [actionToast, setActionToast] = useState<string | null>(null);
  const [busy, setBusy] = useState<boolean>(false);
  const [xmlModalContent, setXmlModalContent] = useState<{
    title: string;
    xml: string;
    path: string;
  } | null>(null);

  const loadDashboard = useCallback(async () => {
    try {
      const overview = await api.getAdminOverview();
      setData(overview);
    } catch (e) {
      console.error("Chyba při načítání Admin Dashboardu:", e);
    }
  }, []);

  useEffect(() => {
    loadDashboard();
  }, [loadDashboard]);

  // Real-time SignalR napojení na StoreHub
  useEffect(() => {
    const conn = createStoreHubConnection("admin");

    conn.on("EntryGranted", (_entry: ScanEntryResponse) => {
      loadDashboard();
    });

    conn.on("OrderCompleted", () => {
      loadDashboard();
    });

    conn.on("ScaleWeightUpdated", (evt: ScaleWeightEvent) => {
      setData((prev) =>
        prev ? { ...prev, currentScaleWeightGrams: evt.weightGrams } : prev
      );
    });

    conn.on("ProductChanged", (updatedProd: ProductDto) => {
      setData((prev) =>
        prev
          ? {
              ...prev,
              products: prev.products.map((p) =>
                p.id === updatedProd.id ? updatedProd : p
              ),
            }
          : prev
      );
    });

    conn.on("EslTagUpdated", (tag: EslPreviewDto) => {
      setData((prev) =>
        prev
          ? {
              ...prev,
              products: prev.products.map((p) =>
                p.id === tag.productId
                  ? {
                      ...p,
                      activePrice: tag.activePrice,
                      lastEslImageBase64: tag.base64Png,
                      hasEslImage: true,
                    }
                  : p
              ),
            }
          : prev
      );
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
  }, [loadDashboard]);

  const handleEmergencyUnlock = async () => {
    setBusy(true);
    try {
      const res = await api.adminEmergencyUnlockDoor();
      setActionToast(res.message);
    } finally {
      setBusy(false);
    }
  };

  const handleTareScale = async () => {
    setBusy(true);
    try {
      const res = await api.adminTareScale();
      setActionToast(res.message);
      await loadDashboard();
    } finally {
      setBusy(false);
    }
  };

  const handleToggleDayNight = async () => {
    setBusy(true);
    try {
      const res = await api.adminToggleDayNightPricing();
      setActionToast(res.message);
      await loadDashboard();
    } finally {
      setBusy(false);
    }
  };

  const handleRenderSingleEsl = async (productId: number) => {
    setBusy(true);
    try {
      const preview = await api.renderProductEsl(productId);
      setActionToast(
        `E-ink štítek pro '${preview.name}' (${preview.activePrice.toFixed(
          2
        )} Kč) byl vyrenderován a odeslán.`
      );
      await loadDashboard();
    } finally {
      setBusy(false);
    }
  };

  const handleCloseSession = async (sessionId: string) => {
    await api.adminCloseSession(sessionId);
    await loadDashboard();
  };

  const handlePreviewOrExportPohodaXml = async (
    docId: string,
    docType: string
  ) => {
    setBusy(true);
    try {
      const res = docType.startsWith("Prodejka")
        ? await api.exportOrderToPohoda(docId)
        : await api.exportDeliveryToPohoda(docId);

      setXmlModalContent({
        title: `${docType} • ${docId}`,
        xml: res.xmlContent,
        path: res.filePath,
      });
      await loadDashboard();
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="min-h-screen bg-[#060a10] text-slate-100 p-6 space-y-6">
      <div className="max-w-7xl mx-auto space-y-6">
        {/* HORNÍ LIŠTA MANAŽERSKÉHO PANELU */}
        <header className="rounded-3xl bg-slate-900/95 border border-slate-800 px-6 py-4 flex flex-wrap items-center justify-between gap-4">
          <div className="flex items-center gap-4">
            <Link
              href="/"
              className="inline-flex items-center gap-1.5 text-xs text-slate-400 hover:text-emerald-400"
            >
              <ArrowLeft className="w-4 h-4" />
              Rozcestník
            </Link>
            <div>
              <h1 className="text-xl font-extrabold text-white">
                CrazyMarket <span className="text-emerald-400">24</span> • Admin
                Control Center
              </h1>
              <p className="text-xs text-slate-400">
                Živý dohled prodejny, MQTT dveře, RS232 váha, 1-bit E-ink
                cenovky (296×128 px) a POHODA XML
              </p>
            </div>
          </div>

          <div className="flex items-center gap-3">
            <div
              className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-xs font-semibold border ${
                hubConnected
                  ? "bg-emerald-500/10 border-emerald-500/30 text-emerald-400"
                  : "bg-amber-500/10 border-amber-500/30 text-amber-400"
              }`}
            >
              {hubConnected ? (
                <>
                  <Wifi className="w-3.5 h-3.5" /> SignalR Live
                </>
              ) : (
                <>
                  <WifiOff className="w-3.5 h-3.5" /> Reconnecting
                </>
              )}
            </div>

            <button
              type="button"
              onClick={loadDashboard}
              className="px-3.5 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-xs font-bold text-slate-200 flex items-center gap-1.5"
            >
              <RefreshCw className="w-3.5 h-3.5" /> Obnovit
            </button>
          </div>
        </header>

        {actionToast && (
          <div className="rounded-2xl bg-emerald-500/15 border border-emerald-500/50 px-5 py-3.5 flex items-center justify-between text-xs text-emerald-200">
            <div className="flex items-center gap-2 font-semibold">
              <CheckCircle2 className="w-4 h-4 text-emerald-400 shrink-0" />
              <span>{actionToast}</span>
            </div>
            <button
              type="button"
              onClick={() => setActionToast(null)}
              className="text-emerald-300 hover:underline"
            >
              Zavřít
            </button>
          </div>
        )}

        {/* 1. ŽIVÝ DOHLED PRODEJNY & HARDWAROVÉ AKCE */}
        <section className="grid grid-cols-1 lg:grid-cols-12 gap-6">
          {/* Osoby právě uvnitř prodejny (7/12) */}
          <div className="lg:col-span-7 rounded-3xl bg-slate-900/90 border border-slate-800 p-6 space-y-4">
            <div className="flex items-center justify-between">
              <h2 className="text-lg font-bold text-white flex items-center gap-2">
                <Users className="w-5 h-5 text-emerald-400" />
                Kdo je právě v prodejně (StoreSession)
              </h2>
              <span className="px-3 py-1 rounded-full bg-emerald-500/15 border border-emerald-500/30 text-emerald-400 text-xs font-bold">
                {data?.activeSessions.length ?? 0} aktivních osob
              </span>
            </div>

            {!data || data.activeSessions.length === 0 ? (
              <div className="rounded-2xl bg-slate-950/60 border border-slate-800/80 p-8 text-center text-xs text-slate-500">
                V prodejně se momentálně nikdo nenachází.
              </div>
            ) : (
              <div className="space-y-2.5">
                {data.activeSessions.map((s) => (
                  <div
                    key={s.sessionId}
                    className="rounded-2xl bg-slate-950/90 border border-slate-800 px-4 py-3 flex items-center justify-between gap-4"
                  >
                    <div className="space-y-0.5">
                      <div className="flex items-center gap-2">
                        <span className="font-bold text-sm text-white">
                          {s.displayName}
                        </span>
                        <span
                          className={`text-[10px] font-bold px-2 py-0.5 rounded ${
                            s.actorType === "Supplier"
                              ? "bg-amber-500/20 text-amber-300"
                              : "bg-emerald-500/20 text-emerald-300"
                          }`}
                        >
                          {s.actorType === "Supplier"
                            ? "ZÁSOBOVÁNÍ"
                            : s.isAdult
                            ? "Zákazník 18+"
                            : "Mladistvý <18"}
                        </span>
                        <span className="text-[11px] font-mono px-2 py-0.5 rounded bg-slate-800 text-slate-300">
                          {s.currentState}
                        </span>
                      </div>
                      <div className="text-xs text-slate-400 font-mono">
                        Vstup:{" "}
                        {new Date(s.enteredAtUtc).toLocaleTimeString("cs-CZ")} •
                        Body: {s.loyaltyPoints}
                      </div>
                    </div>

                    <button
                      type="button"
                      onClick={() => handleCloseSession(s.sessionId)}
                      className="px-3 py-1.5 rounded-xl bg-slate-800 hover:bg-rose-500/20 hover:text-rose-300 text-xs font-semibold text-slate-300 flex items-center gap-1 transition-colors"
                    >
                      <LogOut className="w-3.5 h-3.5" /> Ukončit relaci
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Rychlé hardwarové ovládání prodejny (5/12) */}
          <div className="lg:col-span-5 rounded-3xl bg-slate-900/90 border border-slate-800 p-6 flex flex-col justify-between space-y-4">
            <div>
              <h2 className="text-lg font-bold text-white flex items-center gap-2">
                <ShieldAlert className="w-5 h-5 text-emerald-400" />
                Vzdálené ovládání HW prodejny
              </h2>
              <p className="text-xs text-slate-400 mt-1">
                Okamžité zásahy obsluhy přes MQTT broker a sériovou váhu pokladny
              </p>
            </div>

            <div className="grid grid-cols-2 gap-3 bg-slate-950/80 rounded-2xl p-4 border border-slate-800">
              <div>
                <div className="text-[11px] text-slate-400">
                  Váha pokladny (RS232)
                </div>
                <div className="text-2xl font-mono font-black text-white">
                  {data?.currentScaleWeightGrams ?? 0} g
                </div>
              </div>
              <div>
                <div className="text-[11px] text-slate-400">Aktivní ceník</div>
                <div className="text-base font-bold text-emerald-400 flex items-center gap-1.5 mt-1">
                  {data?.isNightPricingActive ? (
                    <>
                      <Moon className="w-4 h-4 text-indigo-400" /> Noční ceník
                    </>
                  ) : (
                    <>
                      <Sun className="w-4 h-4 text-emerald-400" /> Denní ceník
                    </>
                  )}
                </div>
              </div>
            </div>

            <div className="space-y-2.5">
              <button
                type="button"
                disabled={busy}
                onClick={handleEmergencyUnlock}
                className="w-full py-3.5 px-4 rounded-xl bg-rose-500 hover:bg-rose-400 disabled:opacity-50 text-slate-950 font-black text-xs flex items-center justify-center gap-2 shadow-lg shadow-rose-500/20"
              >
                <DoorOpen className="w-4 h-4" />
                Nouzově odemknout dveře (MQTT • 15 s)
              </button>

              <button
                type="button"
                disabled={busy}
                onClick={handleTareScale}
                className="w-full py-3.5 px-4 rounded-xl bg-amber-500 hover:bg-amber-400 disabled:opacity-50 text-slate-950 font-black text-xs flex items-center justify-center gap-2 shadow-lg shadow-amber-500/20"
              >
                <Scale className="w-4 h-4" />
                Vynulovat váhu pokladny (Tare → 0 g)
              </button>
            </div>
          </div>
        </section>

        {/* 2. GALERIE E-INK CENOVEK (296x128 px, 1-bit PNG) + CELOPLOŠNÝ PŘEPÍNAČ DEN / NOC */}
        <section className="rounded-3xl bg-slate-900/90 border border-slate-800 p-6 space-y-5">
          <div className="flex flex-wrap items-center justify-between gap-4">
            <div>
              <h2 className="text-xl font-extrabold text-white flex items-center gap-2">
                <Tag className="w-5 h-5 text-emerald-400" />
                Galerie E-ink cenovek (LastEslImageBase64 • 296×128 px • 1-bit)
              </h2>
              <p className="text-xs text-slate-400 mt-0.5">
                Živé černobílé bitmapy vygenerované na backendu přes
                SixLabors.ImageSharp s inverzním pruhem AKCE / NOČNÍ CENA
              </p>
            </div>

            <button
              type="button"
              disabled={busy}
              onClick={handleToggleDayNight}
              className={`px-5 py-3 rounded-2xl font-black text-xs flex items-center gap-2 shadow-xl transition-all ${
                data?.isNightPricingActive
                  ? "bg-emerald-500 hover:bg-emerald-400 text-slate-950"
                  : "bg-indigo-500 hover:bg-indigo-400 text-white"
              }`}
            >
              {data?.isNightPricingActive ? (
                <>
                  <Sun className="w-4 h-4" />
                  Přepnout celoplošně na DENNÍ CENÍK (Překreslit všech 10
                  cenovek)
                </>
              ) : (
                <>
                  <Moon className="w-4 h-4" />
                  Přepnout celoplošně Den / Noc → NOČNÍ CENÍK
                </>
              )}
            </button>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-5">
            {data?.products.map((prod) => (
              <div
                key={prod.id}
                className="rounded-2xl bg-slate-950/90 border border-slate-800 p-4 flex flex-col justify-between space-y-3"
              >
                <div className="flex items-start justify-between gap-2">
                  <div>
                    <span className="font-bold text-sm text-white">
                      {prod.name}
                    </span>
                    <div className="text-[11px] font-mono text-slate-400">
                      MAC: {prod.eslMacAddress || "N/A"} • Skladem:{" "}
                      <strong className="text-emerald-400">
                        {prod.stockCount} ks
                      </strong>
                    </div>
                  </div>
                  <span
                    className={`text-[10px] font-black px-2 py-0.5 rounded uppercase ${
                      prod.activePriceTier === "Action"
                        ? "bg-emerald-500 text-slate-950"
                        : prod.activePriceTier === "Night"
                        ? "bg-indigo-500/30 text-indigo-300 border border-indigo-400/40"
                        : "bg-slate-800 text-slate-300"
                    }`}
                  >
                    {prod.activePriceTier === "Action"
                      ? "AKCE"
                      : prod.activePriceTier === "Night"
                      ? "NOČNÍ CENA"
                      : "DENNÍ"}
                  </span>
                </div>

                {/* Skutečný 1-bit 296x128 PNG obrázek z Product.LastEslImageBase64 */}
                <div className="flex items-center justify-center bg-slate-900 rounded-xl p-3 border border-slate-800">
                  {prod.lastEslImageBase64 ? (
                    <img
                      src={`data:image/png;base64,${prod.lastEslImageBase64}`}
                      alt={`ESL ${prod.name}`}
                      width={296}
                      height={128}
                      className="rounded border border-slate-700 bg-white"
                      style={{ imageRendering: "pixelated" }}
                    />
                  ) : (
                    <div className="w-[296px] h-[128px] flex items-center justify-center text-xs text-slate-500">
                      Náhled ještě nebyl vygenerován
                    </div>
                  )}
                </div>

                <div className="flex items-center justify-between text-xs pt-1">
                  <span className="text-slate-400 font-mono">
                    Denní: {prod.basePrice.toFixed(2)} • Noční:{" "}
                    {prod.nightPrice.toFixed(2)} Kč
                  </span>
                  <button
                    type="button"
                    disabled={busy}
                    onClick={() => handleRenderSingleEsl(prod.id)}
                    className="px-2.5 py-1 rounded-lg bg-slate-800 hover:bg-slate-700 text-emerald-400 font-semibold text-[11px]"
                  >
                    Překreslit štítek
                  </button>
                </div>
              </div>
            ))}
          </div>
        </section>

        {/* 3. STAV POHODA XML SYNCHRONIZACE (Prodejky a Příjemky v Windows-1250) */}
        <section className="rounded-3xl bg-slate-900/90 border border-slate-800 p-6 space-y-4">
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-xl font-extrabold text-white flex items-center gap-2">
                <FileCode2 className="w-5 h-5 text-emerald-400" />
                Stav POHODA XML synchronizace (Windows-1250)
              </h2>
              <p className="text-xs text-slate-400">
                Vygenerované prodejky (<code className="text-slate-300">pro:prodejka</code>) a příjemky (
                <code className="text-slate-300">pri:prijemka</code>) ve složce{" "}
                <code className="text-emerald-400">/pohoda-export/</code>
              </p>
            </div>
            <span className="text-xs font-mono text-slate-400">
              {data?.pohodaDocuments.length ?? 0} dokladů
            </span>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse text-xs">
              <thead>
                <tr className="border-b border-slate-800 text-slate-400 uppercase text-[11px]">
                  <th className="py-3 px-3">Typ dokladu</th>
                  <th className="py-3 px-3">Zákazník / Dodavatel</th>
                  <th className="py-3 px-3">Datum a čas</th>
                  <th className="py-3 px-3">Položky / Částka</th>
                  <th className="py-3 px-3">Stav synchronizace</th>
                  <th className="py-3 px-3 text-right">Akce</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/70">
                {data?.pohodaDocuments.map((doc) => (
                  <tr key={doc.documentId} className="hover:bg-slate-950/60">
                    <td className="py-3 px-3 font-bold text-white">
                      {doc.documentType}
                    </td>
                    <td className="py-3 px-3 text-slate-300">
                      {doc.partnerOrCustomerName}
                    </td>
                    <td className="py-3 px-3 font-mono text-slate-400">
                      {new Date(doc.createdAtUtc).toLocaleString("cs-CZ")}
                    </td>
                    <td className="py-3 px-3 font-mono text-slate-200">
                      {doc.itemsCount} ks
                      {doc.totalAmount !== null
                        ? ` • ${doc.totalAmount.toFixed(2)} Kč`
                        : ""}
                    </td>
                    <td className="py-3 px-3">
                      <span
                        className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-full text-[11px] font-semibold ${
                          doc.exportedToDisk
                            ? "bg-emerald-500/15 text-emerald-300 border border-emerald-500/30"
                            : "bg-amber-500/15 text-amber-300 border border-amber-500/30"
                        }`}
                      >
                        {doc.statusText}
                      </span>
                    </td>
                    <td className="py-3 px-3 text-right">
                      <button
                        type="button"
                        disabled={busy}
                        onClick={() =>
                          handlePreviewOrExportPohodaXml(
                            doc.documentId,
                            doc.documentType
                          )
                        }
                        className="inline-flex items-center gap-1 px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-emerald-400 font-semibold"
                      >
                        <Eye className="w-3.5 h-3.5" /> XML Náhled / Export
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      </div>

      {/* MODÁLNÍ OKNO PRO NÁHLED VYGENEROVANÉHO POHODA XML */}
      {xmlModalContent && (
        <div className="fixed inset-0 z-50 bg-slate-950/85 backdrop-blur-sm flex items-center justify-center p-4">
          <div className="max-w-3xl w-full rounded-3xl bg-slate-900 border border-emerald-500/50 p-6 space-y-4 shadow-2xl">
            <div className="flex items-center justify-between">
              <div>
                <span className="text-xs font-mono text-emerald-400">
                  {xmlModalContent.path}
                </span>
                <h3 className="text-lg font-bold text-white">
                  {xmlModalContent.title}
                </h3>
              </div>
              <button
                type="button"
                onClick={() => setXmlModalContent(null)}
                className="px-3 py-1 rounded-lg bg-slate-800 text-slate-300 text-xs"
              >
                Zavřít ✕
              </button>
            </div>
            <pre className="max-h-96 overflow-auto rounded-2xl bg-slate-950 border border-slate-800 p-4 text-xs font-mono text-emerald-300 whitespace-pre-wrap">
              {xmlModalContent.xml}
            </pre>
          </div>
        </div>
      )}
    </div>
  );
}
