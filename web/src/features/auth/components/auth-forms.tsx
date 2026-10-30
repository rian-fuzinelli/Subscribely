"use client";

import Link from "next/link";
import { useActionState } from "react";
import { idle } from "@/shared/forms";
import { Field } from "@/shared/ui/field";
import { FormMessage } from "@/shared/ui/form-message";
import { SubmitButton } from "@/shared/ui/submit-button";
import { acceptInvitationAction, forgotPasswordAction, loginAction, resetPasswordAction } from "../actions";

export function LoginForm({ next, email }: { next?: string; email?: string }) {
  const [state, action] = useActionState(loginAction, idle);
  const values = state.values ?? {};
  return (
    <form action={action} className="space-y-5" noValidate>
      <FormMessage state={state} />
      <input type="hidden" name="next" value={values.next || next || ""} />
      <Field
        label="E-mail"
        name="email"
        type="email"
        autoComplete="email"
        required
        autoFocus={!email}
        defaultValue={values.email ?? email}
        errors={state.fieldErrors?.email}
      />
      <Field
        label="Senha"
        name="password"
        type="password"
        autoComplete="current-password"
        required
        autoFocus={Boolean(email)}
        errors={state.fieldErrors?.password}
      />
      <div className="flex justify-end">
        <Link href="/forgot-password" className="text-sm font-medium text-brand-700 hover:underline">
          Esqueci minha senha
        </Link>
      </div>
      <SubmitButton className="w-full" size="lg" pendingLabel="Entrando…">
        Entrar
      </SubmitButton>
    </form>
  );
}

export function ForgotPasswordForm() {
  const [state, action] = useActionState(forgotPasswordAction, idle);
  return (
    <form action={action} className="space-y-5" noValidate>
      <FormMessage state={state} />
      <Field
        label="E-mail da conta"
        name="email"
        type="email"
        autoComplete="email"
        required
        defaultValue={state.values?.email}
        errors={state.fieldErrors?.email}
      />
      <SubmitButton className="w-full" size="lg" pendingLabel="Enviando…">
        Enviar link de redefinição
      </SubmitButton>
    </form>
  );
}

/** Nova senha + confirmação para um token de uso único (redefinição ou convite). */
export function NewPasswordForm({ token, kind }: { token: string; kind: "reset" | "invitation" }) {
  const [state, action] = useActionState(kind === "reset" ? resetPasswordAction : acceptInvitationAction, idle);
  return (
    <form action={action} className="space-y-5" noValidate>
      <FormMessage state={state} />
      <input type="hidden" name="token" value={token} />
      <Field
        label={kind === "reset" ? "Nova senha" : "Crie uma senha"}
        name="password"
        type="password"
        autoComplete="new-password"
        required
        minLength={12}
        hint="Pelo menos 12 caracteres."
        errors={state.fieldErrors?.password}
      />
      <Field
        label="Confirme a senha"
        name="confirmation"
        type="password"
        autoComplete="new-password"
        required
        errors={state.fieldErrors?.confirmation}
      />
      <SubmitButton className="w-full" size="lg" pendingLabel="Salvando…">
        {kind === "reset" ? "Redefinir senha" : "Aceitar convite e entrar"}
      </SubmitButton>
    </form>
  );
}
