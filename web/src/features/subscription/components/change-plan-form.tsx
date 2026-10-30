"use client";

import { useActionState, useState } from "react";
import { cn } from "@/shared/cn";
import { idle } from "@/shared/forms";
import { formatMoney, formatNumber } from "@/shared/format";
import { ButtonLink } from "@/shared/ui/button";
import { Alert } from "@/shared/ui/primitives";
import { SubmitButton } from "@/shared/ui/submit-button";
import { changePlanAction } from "../actions";
import { effectText, previewPlanChange, type Interval, type SubscriptionStatus } from "../model";

export type PlanOption = {
  code: string;
  name: string;
  monthlyPriceCents: number;
  annualPriceCents: number;
  requestQuotaMonthly: number;
  maxSeats: number;
};

/** Escolha do plano alvo com prévia do efeito (imediato/proporcional ou no fim do período) antes de confirmar. */
export function ChangePlanForm({
  plans,
  current,
  status,
}: {
  plans: PlanOption[];
  current: { code: string; interval: Interval; monthlyPriceCents: number; annualPriceCents: number };
  status: SubscriptionStatus;
}) {
  const [state, action] = useActionState(changePlanAction, idle);
  const [interval, setInterval] = useState<Interval>(current.interval);
  const [planCode, setPlanCode] = useState(current.code);
  const target = plans.find((p) => p.code === planCode);
  const effect = target ? previewPlanChange(status, current, target, interval) : "no-change";
  const seats = state.data as { activeSeats?: number; targetMaxSeats?: number } | undefined;
  const disabled = effect === "no-change" || effect === "blocked-past-due";

  return (
    <form action={action} className="max-w-3xl space-y-6">
      <input type="hidden" name="planCode" value={planCode} />
      <input type="hidden" name="interval" value={interval} />

      <fieldset className="flex items-center gap-3">
        <legend className="sr-only">Cobrança</legend>
        <span aria-hidden className="text-meta text-ink-soft">
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
              <input type="radio" name="interval-choice" className="sr-only" checked={interval === value} onChange={() => setInterval(value)} />
              {value === "monthly" ? "Mensal" : "Anual"}
            </label>
          ))}
        </div>
      </fieldset>

      <fieldset>
        <legend className="sr-only">Plano</legend>
        <ul className="divide-y divide-line border-y border-ink">
          {plans.map((plan) => {
            const isCurrent = plan.code === current.code && interval === current.interval;
            return (
              <li key={plan.code}>
                <label className="grid cursor-pointer grid-cols-[auto_minmax(0,1fr)_auto] items-baseline gap-x-4 px-2 py-4 transition-colors hover:bg-paper has-checked:bg-ledger-50">
                  <input
                    type="radio"
                    name="plan-choice"
                    className="size-4 translate-y-0.5 accent-ledger-600"
                    checked={plan.code === planCode}
                    onChange={() => setPlanCode(plan.code)}
                  />
                  <span>
                    <span className="font-semibold">{plan.name}</span>
                    {isCurrent && <span className="ml-2 text-meta text-ink-faint">plano atual</span>}
                    <span className="block text-meta text-ink-soft">
                      {formatNumber(plan.requestQuotaMonthly)} requisições por mês, até {plan.maxSeats} usuários
                    </span>
                  </span>
                  <span className="numeric text-right font-semibold">
                    {formatMoney(interval === "monthly" ? plan.monthlyPriceCents : plan.annualPriceCents)}
                    <span className="block text-meta font-normal text-ink-faint">por {interval === "monthly" ? "mês" : "ano"}</span>
                  </span>
                </label>
              </li>
            );
          })}
        </ul>
      </fieldset>

      <Alert tone={effect === "blocked-past-due" ? "warning" : "neutral"} title="O que acontece">
        {effectText[effect]}
      </Alert>

      {state.status === "error" && (
        <Alert tone="danger" title={state.message}>
          {seats?.activeSeats !== undefined && (
            <>
              Sua conta ocupa <strong>{seats.activeSeats}</strong> assentos (usuários ativos e convites pendentes) e o plano escolhido
              permite <strong>{seats.targetMaxSeats}</strong>. Desative usuários ou revogue convites na página Equipe e tente de novo.
            </>
          )}
        </Alert>
      )}

      <div className="flex flex-wrap items-center gap-3">
        <SubmitButton disabled={disabled} pendingLabel="Aplicando a mudança…">
          {effect === "period-end" ? "Agendar a mudança" : "Mudar de plano agora"}
        </SubmitButton>
        <ButtonLink href="/billing" variant="ghost">
          Voltar sem mudar
        </ButtonLink>
      </div>
    </form>
  );
}
