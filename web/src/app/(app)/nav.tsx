"use client";

import { BarChart3, CreditCard, FlaskConical, KeyRound, LayoutDashboard, Receipt, ScrollText, Users } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "@/shared/cn";

/** Agrupado pelo que a pessoa vem resolver: dinheiro, uso do produto e a própria conta. */
const groups = [
  {
    label: "Cobrança",
    items: [
      { href: "/overview", label: "Visão geral", icon: LayoutDashboard, adminOnly: false },
      { href: "/billing", label: "Assinatura", icon: CreditCard, adminOnly: false, matches: ["/billing", "/billing/change-plan", "/billing/payment-method"] },
      { href: "/billing/invoices", label: "Faturas", icon: Receipt, adminOnly: true },
    ],
  },
  {
    label: "Produto",
    items: [
      { href: "/usage", label: "Uso", icon: BarChart3, adminOnly: false },
      { href: "/playground", label: "Playground", icon: FlaskConical, adminOnly: true },
      { href: "/api-keys", label: "Chaves de API", icon: KeyRound, adminOnly: true },
    ],
  },
  {
    label: "Conta",
    items: [
      { href: "/team", label: "Equipe", icon: Users, adminOnly: true },
      { href: "/audit", label: "Auditoria", icon: ScrollText, adminOnly: true },
    ],
  },
];

/** Itens filtrados pelo papel; o atual recebe `aria-current="page"` e a régua verde à esquerda. */
export function NavLinks({ isAdmin, onNavigate }: { isAdmin: boolean; onNavigate?: () => void }) {
  const pathname = usePathname();
  return (
    <div className="space-y-6">
      {groups.map((group) => {
        const items = group.items.filter((item) => isAdmin || !item.adminOnly);
        if (items.length === 0) {
          return null;
        }

        return (
          <div key={group.label}>
            <p className="mb-1.5 px-3 text-meta text-ink-faint">{group.label}</p>
            <ul>
              {items.map(({ href, label, icon: Icon, matches }) => {
                const active = matches ? matches.includes(pathname) : pathname === href || pathname.startsWith(href + "/");
                return (
                  <li key={href}>
                    <Link
                      href={href}
                      onClick={onNavigate}
                      aria-current={active ? "page" : undefined}
                      className={cn(
                        "flex items-center gap-2.5 border-l-2 px-3 py-1.5 text-body transition-colors",
                        active ? "border-ledger-600 font-semibold text-ink" : "border-transparent text-ink-soft hover:border-line-strong hover:text-ink",
                      )}
                    >
                      <Icon className={cn("size-4", active ? "text-ledger-600" : "text-ink-faint")} aria-hidden />
                      {label}
                    </Link>
                  </li>
                );
              })}
            </ul>
          </div>
        );
      })}
    </div>
  );
}
