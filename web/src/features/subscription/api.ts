import "server-only";
import { api } from "@/shared/api/client";
import type { Subscription, Tenant } from "./model";

/** Assinatura atual; `null` enquanto o trial ainda não foi criado (logo após o cadastro). */
export async function getSubscription(): Promise<Subscription | null> {
  const result = await api<Subscription>("/v1/subscription");
  return result.ok ? result.data : null;
}

export async function getTenant(): Promise<Tenant | null> {
  const result = await api<Tenant>("/v1/tenant");
  return result.ok ? result.data : null;
}
