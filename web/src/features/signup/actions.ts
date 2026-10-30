"use server";

import { redirect } from "next/navigation";
import { api } from "@/shared/api/client";
import { formValues, fromProblem, fromZod, type FormState } from "@/shared/forms";
import { signupSchema } from "./schema";

const FIELDS = ["companyName", "slug", "adminEmail", "password", "planCode", "interval"] as const;

const FIELD_BY_CODE: Record<string, string> = {
  "slug-taken": "slug",
  "email-unavailable": "adminEmail",
  "plan-unavailable": "planCode",
};

export async function signupAction(_: FormState, form: FormData): Promise<FormState> {
  const values = formValues(form, ...FIELDS);
  const parsed = signupSchema.safeParse(values);
  // A senha nunca volta para o formulário.
  const echo = { ...values, password: "" };
  if (!parsed.success) {
    return fromZod(parsed.error, echo);
  }

  const result = await api<{ tenantId: string }>("/v1/signup", { method: "POST", body: parsed.data, auth: false });
  if (!result.ok) {
    return fromProblem(result.error, echo, FIELD_BY_CODE[result.error.code]);
  }

  redirect("/signup/" + result.data.tenantId + "?email=" + encodeURIComponent(parsed.data.adminEmail));
}

export type ProvisioningStatus = "Provisioning" | "Active" | "ProvisioningFailed" | "Unknown";

/** Consulta o andamento do preparo da conta (rota pública da API). */
export async function provisioningStatusAction(tenantId: string): Promise<ProvisioningStatus> {
  if (!/^[0-9a-f-]{36}$/i.test(tenantId)) {
    return "Unknown";
  }

  const result = await api<{ status: ProvisioningStatus }>("/v1/signup/" + tenantId, { auth: false });
  return result.ok ? result.data.status : "Unknown";
}
