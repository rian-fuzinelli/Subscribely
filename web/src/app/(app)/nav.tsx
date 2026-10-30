"use client";

import { BarChart3, CreditCard, FlaskConical, KeyRound, LayoutDashboard, Receipt, ScrollText, Users } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "@/shared/cn";

const items = [
  { href: "/overview", label: "Visão geral", icon: LayoutDashboard, adminOnly: false },
  { href: "/billing", label: "Assinatura", icon: CreditCard, adminOnly: false, exact: true },
  { href: "/billing/invoices", label: "Faturas", icon: Receipt, adminOnly: true },
  { href: "/usage", label: "Uso", icon: BarChart3, adminOnly: false },
  { href: "/team", label: "Equipe", icon: Users, adminOnly: true },
  { href: "/api-keys", label: "Chaves de API", icon: KeyRound, adminOnly: true },
  { href: "/playground", label: "Playground", icon: FlaskConical, adminOnly: true },
  { href: "/audit", label: "Auditoria", icon: ScrollText, adminOnly: true },
];

/** Itens filtrados pelo papel; o atual recebe `aria-current="page"`. */
export function NavLinks({ isAdmin, onNavigate }: { isAdmin: boolean; onNavigate?: () => void }) {
  const pathname = usePathname();
  return (
    <ul className="space-y-1">
      {items
        .filter((item) => isAdmin || !item.adminOnly)
        .map(({ href, label, icon: Icon, exact }) => {
          const active = exact ? pathname === href || pathname === href + "/change-plan" || pathname === href + "/payment-method" : pathname === href || pathname.startsWith(href + "/");
          return (
            <li key={href}>
              <Link
                href={href}
                onClick={onNavigate}
                aria-current={active ? "page" : undefined}
                className={cn(
                  "flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium",
                  active ? "bg-brand-50 text-brand-800" : "text-slate-700 hover:bg-slate-100 hover:text-slate-900",
                )}
              >
                <Icon className="size-4" aria-hidden />
                {label}
              </Link>
            </li>
          );
        })}
    </ul>
  );
}
