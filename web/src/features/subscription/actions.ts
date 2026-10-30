"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { z } from "zod";
import { api } from "@/shared/api/client";
import { requireAdmin } from "@/shared/auth/session";
import { fromProblem, type FormState } from "@/shared/forms";

const changePlanSchema = z.object({
  planCode: z.string().min(1),
  interval: z.enum(["monthly", "annual"]),
});

export async function changePlanAction(_: FormState, form: FormData): Promise<FormState> {
  await requireAdmin("/billing/change-plan");
  const parsed = changePlanSchema.safeParse({ planCode: form.get("planCode"), interval: form.get("interval") });
  if (!parsed.success) {
    return { status: "error", message: "Escolha um plano e o intervalo de cobrança." };
  }

  const result = await api<{ effective: "immediate" | "period_end"; invoiceId: string | null }>("/v1/subscription/change-plan", {
    method: "POST",
    body: parsed.data,
  });
  if (!result.ok) {
    const state = fromProblem(result.error);
    return result.error.code === "seats-exceed-target-plan" ? { ...state, data: result.error.details } : state;
  }

  revalidatePath("/", "layout");
  const params = new URLSearchParams({ changed: result.data.effective });
  if (result.data.invoiceId) {
    params.set("invoice", result.data.invoiceId);
  }

  redirect("/billing?" + params.toString());
}

/** Ações de um clique sobre a assinatura (cancelar, retomar, desfazer downgrade, reativar). */
async function mutate(path: string, method: "POST" | "DELETE", done: string): Promise<FormState> {
  await requireAdmin("/billing");
  const result = await api(path, { method });
  if (!result.ok) {
    revalidatePath("/", "layout");
    return fromProblem(result.error);
  }

  revalidatePath("/", "layout");
  return { status: "success", message: done };
}

export async function cancelSubscriptionAction(): Promise<FormState> {
  return mutate("/v1/subscription/cancel", "POST", "Cancelamento agendado para o fim do período. Você pode desfazê-lo até lá.");
}

export async function resumeSubscriptionAction(): Promise<FormState> {
  return mutate("/v1/subscription/resume", "POST", "Pronto! Sua assinatura continua normalmente.");
}

export async function cancelPendingChangeAction(): Promise<FormState> {
  return mutate("/v1/subscription/pending-change", "DELETE", "A mudança agendada foi desfeita.");
}

export async function reactivateSubscriptionAction(): Promise<FormState> {
  return mutate("/v1/subscription/reactivate", "POST", "Assinatura reativada! Um novo período começou e a fatura foi emitida.");
}

