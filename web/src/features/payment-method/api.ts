import "server-only";
import { api } from "@/shared/api/client";

export type PaymentMethod = { brand: string; last4: string; updatedAt: string };

export async function getPaymentMethod(): Promise<PaymentMethod | null> {
  const result = await api<PaymentMethod>("/v1/billing/payment-method");
  return result.ok ? result.data : null;
}
