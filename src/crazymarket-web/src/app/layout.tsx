import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "CrazyMarket 24 | Autonomní Prodejna 24/7",
  description:
    "Řídicí systém, zákaznická peněženka a samoobslužná pokladna autonomní prodejny CrazyMarket 24.",
  manifest: "/manifest.webmanifest",
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="cs" className="dark">
      <body className="min-h-screen bg-[#070b12] text-slate-100 selection:bg-emerald-500 selection:text-slate-950">
        {children}
      </body>
    </html>
  );
}
