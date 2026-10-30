"use client";

import { CreditCard } from "lucide-react";
import { useActionState } from "react";
import { cn } from "@/shared/cn";
import { idle } from "@/shared/forms";
import { FormMessage } from "@/shared/ui/form-message";
import { SubmitButton } from "@/shared/ui/submit-button";
import { setPaymentMethodAction } from "../actions";
import { testCards } from "../cards";

export function PaymentMethodForm({ currentLast4 }: { currentLast4?: string }) {
  const [state, action] = useActionState(setPaymentMethodAction, idle);
  const current = testCards.find((card) => card.label.endsWith(currentLast4 ?? "----"));
  return (
    <form action={action} className="space-y-5">
      <FormMessage state={state} />
      <fieldset>
        <legend className="mb-3 text-sm font-medium text-slate-800">Cartão de teste</legend>
        <div className="grid gap-3 sm:grid-cols-2">
          {testCards.map((card) => (
            <label
              key={card.token}
              className={cn(
                "flex cursor-pointer gap-3 rounded-xl border border-slate-200 bg-white p-4 hover:border-slate-300",
                "has-checked:border-brand-600 has-checked:ring-1 has-checked:ring-brand-600 has-focus-visible:outline-2 has-focus-visible:outline-brand-600",
              )}
            >
              <input type="radio" name="token" value={card.token} defaultChecked={card.token === (current?.token ?? "pm_card_ok")} className="mt-1 size-4 accent-brand-600" />
              <span>
                <span className="flex items-center gap-2 text-sm font-semibold text-slate-900">
                  <CreditCard className="size-4 text-slate-500" aria-hidden />
                  {card.label}
                </span>
                <span className="mt-0.5 block text-sm text-slate-600">{card.outcome}</span>
              </span>
            </label>
          ))}
        </div>
      </fieldset>
      <p className="text-xs text-slate-500">
        Ambiente de demonstração: o gateway é simulado e nenhum número de cartão é digitado ou armazenado.
      </p>
      <div className="flex justify-end">
        <SubmitButton pendingLabel="Salvando…">Salvar meio de pagamento</SubmitButton>
      </div>
    </form>
  );
}
