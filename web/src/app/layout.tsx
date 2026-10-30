import type { Metadata, Viewport } from "next";
import { Schibsted_Grotesk } from "next/font/google";
import type { ReactNode } from "react";
import "./globals.css";

const schibsted = Schibsted_Grotesk({
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
  variable: "--font-schibsted",
  display: "swap",
});

export const metadata: Metadata = {
  title: { default: "Subscribely", template: "%s · Subscribely" },
  description: "Assinatura, faturas e uso da sua conta no Subscribely.",
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  themeColor: "#0e6b54",
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="pt-BR" className={schibsted.variable}>
      <body className="min-h-dvh">
        <a
          href="#conteudo"
          className="sr-only focus:not-sr-only focus:fixed focus:top-3 focus:left-3 focus:z-50 focus:rounded-control focus:bg-paper focus:px-4 focus:py-2"
        >
          Pular para o conteúdo
        </a>
        {children}
      </body>
    </html>
  );
}
