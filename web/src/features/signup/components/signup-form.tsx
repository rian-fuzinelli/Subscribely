"use client";

import Link from "next/link";
import { useActionState, useState } from "react";
import { idle } from "@/shared/forms";
import { formatMoney } from "@/shared/format";
import { Field, inputClass } from "@/shared/ui/field";
import { FormMessage } from "@/shared/ui/form-message";
import { SubmitButton } from "@/shared/ui/submit-button";
import type { Plan } from "@/features/pricing";
import { signupAction } from "../actions";
import { suggestSlug } from "../schema";

export function SignupForm({ plans, plan, interval }: { plans: Plan[]; plan?: string; interval?: string }) {
  const [state, action] = useActionState(signupAction, idle);
  const values = state.values ?? {};
  const errors = state.fieldErrors ?? {};
  const [company, setCompany] = useState(values.companyName ?? "");
  const [slug, setSlug] = useState(values.slug ?? "");
  const [slugEdited, setSlugEdited] = useState(Boolean(values.slug));

  const selectedPlan = values.planCode ?? plan ?? plans[0]?.code ?? "";
  const selectedInterval = values.interval ?? (interval === "annual" ? "annual" : "monthly");

  return (
    <form action={action} className="space-y-5" noValidate>
      <FormMessage state={state} />

      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-1.5">
          <label htmlFor="planCode" className="block text-meta font-medium text-ink">
            Plano
          </label>
          <select id="planCode" name="planCode" defaultValue={selectedPlan} className={inputClass} aria-invalid={errors.planCode ? true : undefined}>
            {plans.map((p) => (
              <option key={p.code} value={p.code}>
                {p.name} — {formatMoney(p.monthlyPriceCents)}/mês
              </option>
            ))}
          </select>
          {errors.planCode && <p className="text-meta font-medium text-fault-600">{errors.planCode.join(" ")}</p>}
        </div>
        <fieldset className="space-y-1.5">
          <legend className="block text-meta font-medium text-ink">Cobrança</legend>
          <div className="flex h-10 gap-4">
            {(["monthly", "annual"] as const).map((value) => (
              <label key={value} className="flex items-center gap-2 text-meta text-ink-soft">
                <input type="radio" name="interval" value={value} defaultChecked={selectedInterval === value} className="size-4 accent-ledger-600" />
                {value === "monthly" ? "Mensal" : "Anual"}
              </label>
            ))}
          </div>
        </fieldset>
      </div>

      <Field
        label="Nome da empresa"
        name="companyName"
        autoComplete="organization"
        required
        value={company}
        onChange={(event) => {
          setCompany(event.target.value);
          if (!slugEdited) {
            setSlug(suggestSlug(event.target.value));
          }
        }}
        errors={errors.companyName}
      />
      <Field
        label="Identificador da conta"
        name="slug"
        required
        value={slug}
        onChange={(event) => {
          setSlugEdited(true);
          setSlug(event.target.value.toLowerCase());
        }}
        hint="Letras minúsculas, números e hífen. Não pode ser alterado depois."
        errors={errors.slug}
        spellCheck={false}
      />
      <Field
        label="Seu e-mail"
        name="adminEmail"
        type="email"
        autoComplete="email"
        required
        defaultValue={values.adminEmail}
        errors={errors.adminEmail}
      />
      <Field
        label="Senha"
        name="password"
        type="password"
        autoComplete="new-password"
        required
        minLength={12}
        hint="Pelo menos 12 caracteres."
        errors={errors.password}
      />

      <SubmitButton className="w-full" size="lg" pendingLabel="Criando sua conta…">
        Criar conta e começar o teste
      </SubmitButton>
      <p className="text-center text-meta text-ink-soft">
        Já tem conta?{" "}
        <Link href="/login" className="font-medium text-ledger-700 hover:underline">
          Entrar
        </Link>
      </p>
    </form>
  );
}
