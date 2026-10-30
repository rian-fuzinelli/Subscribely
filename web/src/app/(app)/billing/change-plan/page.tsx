import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { listPlans } from "@/features/pricing";
import { ChangePlanForm, canChangePlan, getSubscription } from "@/features/subscription";
import { requireAdmin } from "@/shared/auth/session";
import { Alert, PageHeader } from "@/shared/ui/primitives";

export const metadata: Metadata = { title: "Mudar de plano" };

export default async function ChangePlanPage() {
  await requireAdmin("/billing/change-plan");
  const [subscription, plans] = await Promise.all([getSubscription(), listPlans()]);
  if (!subscription || !canChangePlan(subscription.status)) {
    redirect("/billing");
  }

  const current = plans.find((p) => p.code === subscription.plan.code);
  return (
    <div className="space-y-8">
      <PageHeader
        title="Mudar de plano"
        description="Upgrades valem na hora, com cobrança proporcional. Downgrades começam no fim do período."
      />
      {!current && (
        <Alert tone="warning">Seu plano atual não é mais oferecido. Você pode continuar nele ou escolher um dos planos abaixo.</Alert>
      )}
      <ChangePlanForm
        plans={plans}
        status={subscription.status}
        current={{
          code: subscription.plan.code,
          interval: subscription.interval,
          monthlyPriceCents: current?.monthlyPriceCents ?? 0,
          annualPriceCents: current?.annualPriceCents ?? 0,
        }}
      />
    </div>
  );
}
