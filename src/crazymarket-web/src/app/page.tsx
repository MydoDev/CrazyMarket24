import Link from "next/link";
import {
  Smartphone,
  Monitor,
  Truck,
  LayoutDashboard,
  ShieldCheck,
  ArrowRight,
  Sparkles,
} from "lucide-react";

export default function HomePage() {
  const cards = [
    {
      href: "/pass",
      badge: "/pass • Mobilní PWA",
      title: "Zákaznický Pass & Peněženka",
      desc: "Dynamický TOTP QR kód pro vstup u dveří i přihlášení na pokladně, BankID OIDC identita (18+), věrnostní body a historie účtenek.",
      cta: "Otevřít zákaznický Pass",
      icon: Smartphone,
    },
    {
      href: "/kiosk",
      badge: "/kiosk • 15.6\" Touch Terminal",
      title: "Samoobslužná Pokladna",
      desc: "Globální HID čtečka čárových kódů, kontrolní RS232 váhový semafor, dynamická identifikace zákazníka, uplatnění bodů a karetní terminál.",
      cta: "Spustit pokladní terminál",
      icon: Monitor,
    },
    {
      href: "/vendor",
      badge: "/vendor & /staff • Zásobování",
      title: "Dodavatelský Portál & Staff Skener",
      desc: "TOTP vstup v zásobovacím režimu, naskladnění vlastních položek farmářem, slevy před expirací (ActionPrice + E-ink) a EAN skener pro brigádníky.",
      cta: "Otevřít zásobovací portál",
      icon: Truck,
    },
    {
      href: "/admin",
      badge: "/admin • Control Center",
      title: "Manažerský Ovládací Panel",
      desc: "Živý přehled osob v prodejně, nouzové MQTT odemknutí dveří, Tare váhy, galerie 1-bit E-ink cenovek (296×128 px), přepínač Den/Noc a POHODA XML.",
      cta: "Otevřít Admin Dashboard",
      icon: LayoutDashboard,
    },
  ];

  return (
    <main className="min-h-screen flex flex-col justify-center items-center p-6 bg-gradient-to-b from-[#070b12] via-[#0b1320] to-[#070b12]">
      <div className="max-w-5xl w-full space-y-10">
        <div className="text-center space-y-3">
          <div className="inline-flex items-center gap-2 px-3.5 py-1.5 rounded-full bg-emerald-500/10 border border-emerald-500/30 text-emerald-400 text-xs font-semibold tracking-wider uppercase">
            <Sparkles className="w-3.5 h-3.5" />
            Autonomous Retail System 24/7 • Production Ready
          </div>
          <h1 className="text-4xl md:text-5xl font-extrabold tracking-tight text-white">
            CrazyMarket <span className="text-emerald-400">24</span>
          </h1>
          <p className="text-slate-400 max-w-2xl mx-auto text-sm md:text-base">
            Kompletní řídicí ekosystém autonomní bezobslužné prodejny. Vyberte
            zákaznickou peněženku, pokladní kiosek, zásobovací skener nebo
            manažerský velín.
          </p>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
          {cards.map((card) => {
            const Icon = card.icon;
            return (
              <Link
                key={card.href}
                href={card.href}
                className="group relative overflow-hidden rounded-2xl bg-slate-900/80 border border-slate-800 hover:border-emerald-500/60 p-7 transition-all duration-200 hover:shadow-2xl hover:shadow-emerald-500/10 flex flex-col justify-between"
              >
                <div className="space-y-4">
                  <div className="w-12 h-12 rounded-xl bg-emerald-500/15 border border-emerald-500/30 flex items-center justify-center text-emerald-400">
                    <Icon className="w-6 h-6" />
                  </div>
                  <div>
                    <span className="text-xs font-mono uppercase tracking-wider text-emerald-400">
                      {card.badge}
                    </span>
                    <h2 className="text-2xl font-bold text-white mt-1">
                      {card.title}
                    </h2>
                    <p className="text-slate-400 text-sm mt-2 leading-relaxed">
                      {card.desc}
                    </p>
                  </div>
                </div>
                <div className="mt-6 pt-4 border-t border-slate-800/80 flex items-center justify-between text-sm font-medium text-emerald-400">
                  <span>{card.cta}</span>
                  <ArrowRight className="w-4 h-4 group-hover:translate-x-1 transition-transform" />
                </div>
              </Link>
            );
          })}
        </div>

        <div className="rounded-xl bg-slate-900/50 border border-slate-800/80 px-5 py-3.5 flex flex-wrap items-center justify-between gap-4 text-xs text-slate-400">
          <div className="flex items-center gap-2">
            <ShieldCheck className="w-4 h-4 text-emerald-400" />
            <span>
              Backend API:{" "}
              <code className="text-slate-200 font-mono">
                {process.env.NEXT_PUBLIC_API_URL || "http://localhost:5080"}
              </code>
            </span>
          </div>
          <span>
            SignalR StoreHub • 1-bit E-ink (296×128) • POHODA XML (Windows-1250)
          </span>
        </div>
      </div>
    </main>
  );
}
