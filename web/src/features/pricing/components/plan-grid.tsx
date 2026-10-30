"use client";

import Link from "next/link";
import { useState } from "react";
import { cn } from "@/shared/cn";
import { formatMoney, formatNumber } from "@/shared/format";
import { buttonClass } from "@/shared/ui/button";
import { Ledger } from "@/shared/ui/ledger";
import type { Plan } from "../api";

type Interval = "monthly" | "annual";

/**
 * Comparação de planos em tabela (lado a lado no desktop) e em extratos empilhados no celular.
 * A alternância mensal/anual muda só a linha de preço.
 */
export function PlanGrid({ plans, hrefTemplate, ctaLabel = "Começar o teste" }: {
  plans: Plan[];
  /** URL do botão com `{plan}` e `{interval}` (funções não atravessam para o cliente). */
  hrefTemplate: string;
  ctaLabel?: string;
}) {
  const [interval, setInterval] = useState<Interval>("monthly");
  const price = (plan: Plan) => (interval === "monthly" ? plan.monthlyPriceCents : plan.annualPriceCents);
  const href = (plan: Plan) => hrefTemplate.replace("{plan}", encodeURIComponent(plan.code)).replace("{interval}", interval);
  const savings = (plan: Plan) => plan.monthlyPriceCents * 12 - plan.annualPriceCents;

  return (
    <div className="space-y-6">
      <fieldset className="flex items-center gap-3">
        <legend className="sr-only">Cobrança</legend>
        <span className="text-meta text-ink-soft" aria-hidden>
          Cobrança
        </span>
        <div className="inline-flex rounded-control border border-line-strong bg-paper p-0.5">
          {(["monthly", "annual"] as const).map((value) => (
            <label
              key={value}
              className={cn(
                "cursor-pointer rounded-[4px] px-3 py-1.5 text-meta font-medium transition-colors has-focus-visible:outline-2 has-focus-visible:outline-ledger-600",
                interval === value ? "bg-ink text-paper" : "text-ink-soft hover:text-ink",
              )}
            >
              <input type="radio" name="billing-interval" className="sr-only" checked={interval === value} onChange={() => setInterval(value)} />
              {value === "monthly" ? "Mensal" : "Anual"}
            </label>
          ))}
        </div>
      </fieldset>

      {/* Desktop: tabela comparativa. */}
      <table className="hidden w-full border-collapse text-left md:table">
        <caption className="sr-only">Comparação dos planos</caption>
        <thead>
          <tr className="border-b-2 border-ink">
            <td className="w-1/4 pb-3" />
            {plans.map((plan) => (
              <th key={plan.code} scope="col" className="pb-3 text-title font-semibold">
                {plan.name}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="numeric">
          <tr className="border-b border-line">
            <th scope="row" className="py-4 font-normal text-ink-soft">
              Preço
            </th>
            {plans.map((plan) => (
              <td key={plan.code} className="py-4">
                <span className="text-title font-semibold">{formatMoney(price(plan))}</span>
                <span className="text-meta text-ink-faint"> por {interval === "monthly" ? "mês" : "ano"}</span>
                {interval === "annual" && savings(plan) > 0 && (
                  <span className="block text-meta text-ledger-700">economia de {formatMoney(savings(plan))}</span>
                )}
              </td>
            ))}
          </tr>
          <tr className="border-b border-line">
            <th scope="row" className="py-4 font-normal text-ink-soft">
              Requisições por mês
            </th>
            {plans.map((plan) => (
              <td key={plan.code} className="py-4">{formatNumber(plan.requestQuotaMonthly)}</td>
            ))}
          </tr>
          <tr className="border-b border-line">
            <th scope="row" className="py-4 font-normal text-ink-soft">
              Usuários
            </th>
            {plans.map((plan) => (
              <td key={plan.code} className="py-4">até {formatNumber(plan.maxSeats)}</td>
            ))}
          </tr>
          <tr>
            <td className="pt-6" />
            {plans.map((plan, index) => (
              <td key={plan.code} className="pt-6 pr-6">
                <Link href={href(plan)} className={buttonClass({ variant: index === 1 ? "primary" : "secondary" })}>
                  {ctaLabel}
                </Link>
              </td>
            ))}
          </tr>
        </tbody>
      </table>

      {/* Celular: um extrato por plano. */}
      <ul className="space-y-8 md:hidden">
        {plans.map((plan, index) => (
          <li key={plan.code} className="border-t-2 border-ink pt-4">
            <h3 className="text-title font-semibold">{plan.name}</h3>
            <Ledger
              className="mt-2"
              rows={[
                {
                  label: interval === "monthly" ? "Por mês" : "Por ano",
                  value: formatMoney(price(plan)),
                  detail: interval === "annual" && savings(plan) > 0 ? "Economia de " + formatMoney(savings(plan)) + " por ano" : undefined,
                },
                { label: "Requisições por mês", value: formatNumber(plan.requestQuotaMonthly) },
                { label: "Usuários", value: "até " + formatNumber(plan.maxSeats) },
              ]}
            />
            <Link href={href(plan)} className={buttonClass({ variant: index === 1 ? "primary" : "secondary" }, "mt-4 w-full")}>
              {ctaLabel} no {plan.name}
            </Link>
          </li>
        ))}
      </ul>
    </div>
  );
}
