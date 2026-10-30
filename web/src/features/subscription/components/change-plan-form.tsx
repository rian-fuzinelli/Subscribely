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
    <form action={action} className="space-y-6">
      <input type="hidden" name="planCode" value={planCode} />
      <input type="hidden" name="interval" value={interval} />

      <fieldset>
        <legend className="mb-3 text-sm font-medium text-slate-800">Cobrança</legend>
        <div className="inline-flex rounded-xl bg-slate-100 p-1">
          {(["monthly", "annual"] as const).map((value) => (
            <label
              key={value}
              className={cn(
                "cursor-pointer rounded-lg px-4 py-2 text-sm font-medium has-focus-visible:outline-2 has-focus-visible:outline-brand-600",
                interval === value ? "bg-white text-slate-900 shadow-xs" : "text-slate-600",
              )}
            >
              <input type="radio" className="sr-only" checked={interval === value} onChange={() => setInterval(value)} />
              {value === "monthly" ? "Mensal" : "Anual"}
            </label>
          ))}
        </div>
      </fieldset>

      <fieldset>
        <legend className="mb-3 text-sm font-medium text-slate-800">Plano</legend>
        <div className="grid gap-4 md:grid-cols-3">
          {plans.map((plan) => {
            const selected = plan.code === planCode;
            const isCurrent = plan.code === current.code && interval === current.interval;
            return (
              <label
                key={plan.code}
                className={cn(
                  "relative flex cursor-pointer flex-col rounded-xl border bg-white p-5 has-focus-visible:outline-2 has-focus-visible:outline-brand-600",
                  selected ? "border-brand-600 ring-1 ring-brand-600" : "border-slate-200 hover:border-slate-300",
                )}
              >
                <input type="radio" className="sr-only" checked={selected} onChange={() => setPlanCode(plan.code)} />
                <span className="flex items-center justify-between font-semibold text-slate-900">
                  {plan.name}
                  {isCurrent && <span className="text-xs font-medium text-slate-500">Atual</span>}
                </span>
                <span className="mt-2 text-2xl font-bold text-slate-900">
                  {formatMoney(interval === "monthly" ? plan.monthlyPriceCents : plan.annualPriceCents)}
                  <span className="text-sm font-normal text-slate-500">/{interval === "monthly" ? "mês" : "ano"}</span>
                </span>
                <span className="mt-3 text-sm text-slate-600">
                  {formatNumber(plan.requestQuotaMonthly)} requisições/mês · até {plan.maxSeats} usuários
                </span>
              </label>
            );
          })}
        </div>
      </fieldset>

      <Alert tone={effect === "blocked-past-due" ? "warning" : "brand"} title="O que acontece">
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

      <div className="flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
        <ButtonLink href="/billing" variant="secondary">
          Voltar
        </ButtonLink>
        <SubmitButton disabled={disabled} pendingLabel="Aplicando…">
          {effect === "period-end" ? "Agendar mudança" : "Confirmar mudança"}
        </SubmitButton>
      </div>
    </form>
  );
}
