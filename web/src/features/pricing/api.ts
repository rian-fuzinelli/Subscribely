import "server-only";
import { api } from "@/shared/api/client";

export type Plan = {
  code: string;
  name: string;
  status: string;
  version: number;
  monthlyPriceCents: number;
  annualPriceCents: number;
  requestQuotaMonthly: number;
  maxSeats: number;
};

/** Planos ativos, do mais barato ao mais caro (rota pública). */
export async function listPlans(): Promise<Plan[]> {
  const result = await api<Plan[]>("/v1/plans", { auth: false });
  return result.ok ? result.data : [];
}
