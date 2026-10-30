"use client";

import { useActionState } from "react";
import { idle } from "@/shared/forms";
import { Field, inputClass } from "@/shared/ui/field";
import { FormMessage } from "@/shared/ui/form-message";
import { SubmitButton } from "@/shared/ui/submit-button";
import { inviteAction } from "../actions";

export function InviteForm({ seatsLeft }: { seatsLeft: number }) {
  const [state, action] = useActionState(inviteAction, idle);
  const full = seatsLeft <= 0;
  return (
    <form action={action} className="space-y-4" noValidate>
      <FormMessage state={state} />
      <div className="grid gap-4 sm:grid-cols-[1fr_12rem_auto] sm:items-end">
        <Field
          label="E-mail"
          name="email"
          type="email"
          autoComplete="off"
          required
          disabled={full}
          defaultValue={state.status === "error" ? state.values?.email : ""}
          key={state.status === "success" ? state.message : "invite"}
          errors={state.fieldErrors?.email}
        />
        <div className="space-y-1.5">
          <label htmlFor="invite-role" className="block text-sm font-medium text-slate-800">
            Papel
          </label>
          <select id="invite-role" name="role" defaultValue={state.values?.role ?? "Member"} disabled={full} className={inputClass}>
            <option value="Member">Membro (só leitura)</option>
            <option value="TenantAdmin">Administrador</option>
          </select>
        </div>
        <SubmitButton disabled={full} pendingLabel="Enviando…">
          Convidar
        </SubmitButton>
      </div>
    </form>
  );
}
