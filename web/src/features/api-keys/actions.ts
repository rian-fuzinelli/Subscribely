"use server";

import { revalidatePath } from "next/cache";
import { z } from "zod";
import { api } from "@/shared/api/client";
import { requireAdmin } from "@/shared/auth/session";
import { formValues, fromProblem, fromZod, type FormState } from "@/shared/forms";

const createSchema = z.object({ name: z.string().trim().min(1, "Dê um nome à chave.").max(60, "Use no máximo 60 caracteres.") });

/** Cria a chave; o segredo volta só neste estado (nunca é guardado no portal). */
export async function createApiKeyAction(_: FormState, form: FormData): Promise<FormState> {
  await requireAdmin("/api-keys");
  const values = formValues(form, "name");
  const parsed = createSchema.safeParse(values);
  if (!parsed.success) {
    return fromZod(parsed.error, values);
  }

  const result = await api<{ id: string; name: string; key: string }>("/v1/api-keys", { method: "POST", body: parsed.data });
  if (!result.ok) {
    return fromProblem(result.error, values);
  }

  revalidatePath("/api-keys");
  return { status: "success", data: { name: result.data.name, key: result.data.key } };
}

export async function revokeApiKeyAction(id: string): Promise<FormState> {
  await requireAdmin("/api-keys");
  const result = await api("/v1/api-keys/" + encodeURIComponent(id), { method: "DELETE" });
  revalidatePath("/api-keys");
  return result.ok ? { status: "success", message: "Chave revogada. Chamadas com ela passam a ser recusadas na hora." } : fromProblem(result.error);
}
