"use client";

import { useActionState } from "react";
import { idle } from "@/shared/forms";
import { FormMessage } from "@/shared/ui/form-message";
import { SubmitButton } from "@/shared/ui/submit-button";
import { setPaymentMethodAction } from "../actions";
import { testCards } from "../cards";

export function PaymentMethodForm({ currentLast4 }: { currentLast4?: string }) {
  const [state, action] = useActionState(setPaymentMethodAction, idle);
  const current = testCards.find((card) => card.label.endsWith(currentLast4 ?? "----"));
  return (
    <form action={action} className="max-w-2xl space-y-5">
      <FormMessage state={state} />
      <fieldset>
        <legend className="sr-only">Cartão de teste</legend>
        <ul className="divide-y divide-line border-y border-ink">
          {testCards.map((card) => (
            <li key={card.token}>
              <label className="grid cursor-pointer grid-cols-[auto_minmax(0,1fr)] items-baseline gap-x-4 px-2 py-3.5 transition-colors hover:bg-paper has-checked:bg-ledger-50">
                <input
                  type="radio"
                  name="token"
                  value={card.token}
                  defaultChecked={card.token === (current?.token ?? "pm_card_ok")}
                  className="size-4 translate-y-0.5 accent-ledger-600"
                />
                <span>
                  <span className="font-medium">{card.label}</span>
                  <span className="block text-meta text-ink-soft">{card.outcome}</span>
                </span>
              </label>
            </li>
          ))}
        </ul>
      </fieldset>
      <p className="text-meta text-ink-faint">
        Ambiente de demonstração: o gateway é simulado e nenhum número de cartão é digitado ou armazenado.
      </p>
      <SubmitButton pendingLabel="Salvando o cartão…">Salvar cartão</SubmitButton>
    </form>
  );
}
