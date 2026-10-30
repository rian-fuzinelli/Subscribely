"use server";

import { revalidatePath } from "next/cache";
import { api } from "@/shared/api/client";
import { requireAdmin } from "@/shared/auth/session";
import { fromProblem, type FormState } from "@/shared/forms";

/** Nova tentativa de cobrança com o meio de pagamento atual; o resultado chega pelo webhook do gateway. */
export async function payInvoiceAction(invoiceId: string): Promise<FormState> {
  await requireAdmin("/billing/invoices/" + invoiceId);
  const result = await api("/v1/invoices/" + encodeURIComponent(invoiceId) + "/pay", { method: "POST" });
  if (!result.ok) {
    return fromProblem(result.error);
  }

  revalidatePath("/", "layout");
  return { status: "success", message: "Pagamento enviado. Esta página se atualiza sozinha quando o banco responder." };
}
