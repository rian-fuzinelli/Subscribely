"use client";

import { Check } from "lucide-react";
import { useState } from "react";
import { cn } from "@/shared/cn";
import { formatMoney, formatNumber } from "@/shared/format";
import { buttonClass } from "@/shared/ui/button";
import Link from "next/link";
import type { Plan } from "../api";

type Interval = "monthly" | "annual";

/** Grade de planos com alternância mensal/anual. O plano do meio é destacado como recomendado. */
export function PlanGrid({
  plans,
  hrefTemplate,
  ctaLabel = "Começar teste grátis",
  current,
}: {
  plans: Plan[];
  /** URL do botão com `{plan}` e `{interval}` (funções não atravessam para o cliente). */
  hrefTemplate: string;
  ctaLabel?: string;
  current?: { code: string; interval: Interval };
}) {
  const [interval, setInterval] = useState<Interval>(current?.interval ?? "monthly");
  const featured = plans.length >= 3 ? plans[1]?.code : undefined;

  return (
    <div className="space-y-8">
      <div className="flex justify-center">
        <div role="radiogroup" aria-label="Intervalo de cobrança" className="inline-flex rounded-xl bg-slate-100 p-1">
          {(["monthly", "annual"] as const).map((value) => (
            <button
              key={value}
              type="button"
              role="radio"
              aria-checked={interval === value}
              onClick={() => setInterval(value)}
              className={cn(
                "rounded-lg px-4 py-2 text-sm font-medium transition-colors",
                interval === value ? "bg-white text-slate-900 shadow-xs" : "text-slate-600 hover:text-slate-900",
              )}
            >
              {value === "monthly" ? "Mensal" : "Anual"}
            </button>
          ))}
        </div>
      </div>

      <ul className="grid gap-6 md:grid-cols-2 lg:grid-cols-3">
        {plans.map((plan) => {
          const price = interval === "monthly" ? plan.monthlyPriceCents : plan.annualPriceCents;
          const savings = plan.monthlyPriceCents * 12 - plan.annualPriceCents;
          const isCurrent = current?.code === plan.code && current.interval === interval;
          const isFeatured = plan.code === featured;
          return (
            <li
              key={plan.code}
              className={cn(
                "relative flex flex-col rounded-2xl border bg-white p-6 shadow-xs",
                isFeatured ? "border-brand-600 ring-1 ring-brand-600" : "border-slate-200",
              )}
            >
              {isFeatured && (
                <span className="absolute -top-3 left-6 rounded-full bg-brand-600 px-3 py-0.5 text-xs font-semibold text-white">
                  Mais escolhido
                </span>
              )}
              <h3 className="text-lg font-semibold text-slate-900">{plan.name}</h3>
              <p className="mt-4 flex items-baseline gap-1">
                <span className="text-3xl font-bold tracking-tight text-slate-900">{formatMoney(price)}</span>
                <span className="text-sm text-slate-500">/{interval === "monthly" ? "mês" : "ano"}</span>
              </p>
              <p className="mt-1 h-5 text-xs text-emerald-700">
                {interval === "annual" && savings > 0 ? "Economize " + formatMoney(savings) + " por ano" : ""}
              </p>
              <ul className="mt-6 flex-1 space-y-3 text-sm text-slate-700">
                <Feature>{formatNumber(plan.requestQuotaMonthly)} requisições por mês</Feature>
                <Feature>Até {formatNumber(plan.maxSeats)} {plan.maxSeats === 1 ? "usuário" : "usuários"}</Feature>
                <Feature>14 dias de teste, sem cartão</Feature>
                <Feature>Cancele quando quiser</Feature>
              </ul>
              {isCurrent ? (
                <span className={buttonClass({ variant: "secondary" }, "mt-8 w-full")} aria-disabled>
                  Plano atual
                </span>
              ) : (
                <Link
                  href={hrefTemplate.replace("{plan}", encodeURIComponent(plan.code)).replace("{interval}", interval)}
                  className={buttonClass({ variant: isFeatured ? "primary" : "secondary" }, "mt-8 w-full")}
                >
                  {ctaLabel}
                </Link>
              )}
            </li>
          );
        })}
      </ul>
    </div>
  );
}

function Feature({ children }: { children: React.ReactNode }) {
  return (
    <li className="flex gap-2">
      <Check className="mt-0.5 size-4 shrink-0 text-brand-600" aria-hidden />
      <span>{children}</span>
    </li>
  );
}
