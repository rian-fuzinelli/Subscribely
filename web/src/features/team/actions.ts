"use server";

import { revalidatePath } from "next/cache";
import { z } from "zod";
import { api } from "@/shared/api/client";
import { requireAdmin } from "@/shared/auth/session";
import { formValues, fromProblem, fromZod, type FormState } from "@/shared/forms";

const inviteSchema = z.object({
  email: z.email("Informe um e-mail válido.").max(254),
  role: z.enum(["TenantAdmin", "Member"], { error: "Escolha o papel." }),
});

export async function inviteAction(_: FormState, form: FormData): Promise<FormState> {
  await requireAdmin("/team");
  const values = formValues(form, "email", "role");
  const parsed = inviteSchema.safeParse(values);
  if (!parsed.success) {
    return fromZod(parsed.error, values);
  }

  const result = await api("/v1/users/invitations", { method: "POST", body: parsed.data });
  if (!result.ok) {
    return fromProblem(result.error, values, result.error.code === "invitation-exists" ? "email" : undefined);
  }

  revalidatePath("/team");
  return { status: "success", message: "Convite enviado para " + parsed.data.email + ". O link vale por 72 horas.", values: { role: values.role ?? "Member" } };
}

async function mutate(path: string, method: "POST" | "DELETE", done: string): Promise<FormState> {
  await requireAdmin("/team");
  const result = await api(path, { method });
  revalidatePath("/team");
  return result.ok ? { status: "success", message: done } : fromProblem(result.error);
}

export async function revokeInvitationAction(id: string): Promise<FormState> {
  return mutate("/v1/users/invitations/" + encodeURIComponent(id), "DELETE", "Convite revogado; o assento foi liberado.");
}

export async function disableUserAction(id: string): Promise<FormState> {
  return mutate("/v1/users/" + encodeURIComponent(id) + "/disable", "POST", "Usuário desativado; o acesso foi encerrado.");
}

export async function enableUserAction(id: string): Promise<FormState> {
  return mutate("/v1/users/" + encodeURIComponent(id) + "/enable", "POST", "Usuário reativado.");
}
