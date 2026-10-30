"use client";

import { Check, Copy } from "lucide-react";
import { useActionState, useState } from "react";
import { idle } from "@/shared/forms";
import { Button } from "@/shared/ui/button";
import { Field } from "@/shared/ui/field";
import { FormMessage } from "@/shared/ui/form-message";
import { Alert } from "@/shared/ui/primitives";
import { SubmitButton } from "@/shared/ui/submit-button";
import { createApiKeyAction } from "../actions";

export function CreateKeyForm() {
  const [state, action] = useActionState(createApiKeyAction, idle);
  const created = state.status === "success" ? (state.data as { name: string; key: string } | undefined) : undefined;
  return (
    <div className="space-y-4">
      {created && <NewKey name={created.name} secret={created.key} key={created.key} />}
      <form action={action} className="space-y-4" noValidate>
        <FormMessage state={state} />
        <div className="grid gap-4 sm:grid-cols-[1fr_auto] sm:items-end">
          <Field
            label="Nome da chave"
            name="name"
            placeholder="Ex.: servidor de produção"
            maxLength={60}
            required
            key={created?.key ?? "new"}
            defaultValue={state.status === "error" ? state.values?.name : ""}
            errors={state.fieldErrors?.name}
          />
          <SubmitButton pendingLabel="Criando…">Criar chave</SubmitButton>
        </div>
      </form>
    </div>
  );
}

/** O segredo só existe aqui, na memória da página, até ela ser fechada. */
function NewKey({ name, secret }: { name: string; secret: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <Alert tone="success" title={"Chave \"" + name + "\" criada — copie agora"}>
      <p>Por segurança, ela não será mostrada de novo. Guarde-a num cofre de segredos.</p>
      <div className="mt-3 flex flex-col gap-2 sm:flex-row">
        <code className="block flex-1 rounded-lg bg-white px-3 py-2 font-mono text-xs break-all text-slate-900 ring-1 ring-emerald-200">
          {secret}
        </code>
        <Button
          size="sm"
          variant="secondary"
          onClick={async () => {
            await navigator.clipboard.writeText(secret);
            setCopied(true);
          }}
        >
          {copied ? <Check className="size-4" aria-hidden /> : <Copy className="size-4" aria-hidden />}
          {copied ? "Copiada" : "Copiar"}
        </Button>
      </div>
    </Alert>
  );
}
