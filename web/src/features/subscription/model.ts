/** Contratos da API (spec 03) e as regras de exibição do portal — sem dependência de servidor. */

export type Interval = "monthly" | "annual";

export type SubscriptionStatus = "Trialing" | "Active" | "PastDue" | "Suspended" | "Expired" | "Canceled";

export type Subscription = {
  status: SubscriptionStatus;
  plan: { code: string; name: string; version: number };
  interval: Interval;
  trialEndsAt: string | null;
  currentPeriodStart: string;
  currentPeriodEnd: string;
  cancelAtPeriodEnd: boolean;
  pendingChange: { planCode: string; interval: Interval; effectiveAt: string } | null;
};

export type Tenant = { id: string; slug: string; name: string; status: string };

export const statusLabel: Record<SubscriptionStatus, string> = {
  Trialing: "Em teste",
  Active: "Ativa",
  PastDue: "Pagamento pendente",
  Suspended: "Suspensa",
  Expired: "Expirada",
  Canceled: "Cancelada",
};

export const statusTone: Record<SubscriptionStatus, "brand" | "success" | "warning" | "danger" | "neutral"> = {
  Trialing: "brand",
  Active: "success",
  PastDue: "warning",
  Suspended: "danger",
  Expired: "neutral",
  Canceled: "neutral",
};

/** O produto funciona nestes status (spec 03). */
export function productAvailable(status: SubscriptionStatus): boolean {
  return status === "Trialing" || status === "Active" || status === "PastDue";
}

export function canChangePlan(status: SubscriptionStatus): boolean {
  return status === "Trialing" || status === "Active" || status === "PastDue";
}

export function canReactivate(status: SubscriptionStatus): boolean {
  return status === "Expired" || status === "Canceled";
}

export type PlanPrice = { code: string; monthlyPriceCents: number; annualPriceCents: number };

export type PlanChangeEffect =
  | "no-change"
  | "immediate-free"
  | "immediate-prorated"
  | "period-end"
  | "blocked-past-due";

/**
 * Prévia do efeito de uma mudança de plano, com a mesma regra da API (`PlanChangeClassifier`): trocar de
 * intervalo decide sozinho (mensal → anual = upgrade); no mesmo intervalo, compara o preço.
 */
export function previewPlanChange(
  status: SubscriptionStatus,
  current: PlanPrice & { interval: Interval },
  target: PlanPrice,
  targetInterval: Interval,
): PlanChangeEffect {
  if (current.code === target.code && current.interval === targetInterval) {
    return "no-change";
  }

  if (status === "Trialing") {
    return "immediate-free";
  }

  const price = (plan: PlanPrice, interval: Interval) => (interval === "annual" ? plan.annualPriceCents : plan.monthlyPriceCents);
  const difference = price(target, targetInterval) - price(current, current.interval);
  const kind =
    current.interval !== targetInterval
      ? targetInterval === "annual" ? "upgrade" : "downgrade"
      : difference > 0 ? "upgrade" : difference < 0 ? "downgrade" : "lateral";

  if (kind === "upgrade") {
    return status === "PastDue" ? "blocked-past-due" : "immediate-prorated";
  }

  return kind === "downgrade" ? "period-end" : "immediate-free";
}

export const effectText: Record<PlanChangeEffect, string> = {
  "no-change": "Este já é o seu plano atual.",
  "immediate-free": "A mudança vale agora, sem cobrança adicional.",
  "immediate-prorated":
    "A mudança vale agora. Você recebe crédito pelos dias não usados do plano atual e paga só a diferença proporcional até o fim do período.",
  "period-end": "A mudança será aplicada no fim do período atual. Até lá, você continua no plano atual, sem reembolso.",
  "blocked-past-due": "Pague a fatura em aberto antes de fazer upgrade.",
};
