"use server";

import { revalidatePath } from "next/cache";
import { api } from "@/shared/api/client";
import { requireAdmin } from "@/shared/auth/session";
import { fromProblem, type FormState } from "@/shared/forms";
import { isTestCard } from "./cards";

export async function setPaymentMethodAction(_: FormState, form: FormData): Promise<FormState> {
  await requireAdmin("/billing/payment-method");
  const token = String(form.get("token") ?? "");
  if (!isTestCard(token)) {
    return { status: "error", message: "Escolha um dos cartões de teste.", fieldErrors: { token: ["Escolha um cartão."] } };
  }

  const result = await api("/v1/billing/payment-method", { method: "PUT", body: { token } });
  if (!result.ok) {
    return fromProblem(result.error);
  }

  revalidatePath("/", "layout");
  return {
    status: "success",
    message: "Meio de pagamento salvo. Se havia uma fatura em aberto com pagamento pendente, já tentamos cobrar de novo.",
  };
}
