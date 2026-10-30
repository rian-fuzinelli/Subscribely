import { CreditCard } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getPaymentMethod } from "@/features/payment-method";
import { listPlans } from "@/features/pricing";
import { SubscriptionCard, getSubscription } from "@/features/subscription";
import { requireSession } from "@/shared/auth/session";
import { formatDate } from "@/shared/format";
import { ButtonLink } from "@/shared/ui/button";
import { Alert, Card, CardTitle, PageHeader } from "@/shared/ui/primitives";

export const metadata: Metadata = { title: "Assinatura" };

export default async function BillingPage({ searchParams }: { searchParams: Promise<{ changed?: string; invoice?: string }> }) {
  const session = await requireSession("/billing");
  const isAdmin = session.role === "TenantAdmin";
  const [{ changed, invoice }, subscription, paymentMethod, plans] = await Promise.all([
    searchParams,
    getSubscription(),
    isAdmin ? getPaymentMethod() : Promise.resolve(null),
    listPlans(),
  ]);
  const pendingPlanName = plans.find((p) => p.code === subscription?.pendingChange?.planCode)?.name;

  return (
    <div className="space-y-8">
      <PageHeader title="Assinatura" description="Plano, período e meio de pagamento." />

      {changed === "immediate" && (
        <Alert tone="success" title="Plano alterado">
          A mudança já está valendo.{" "}
          {invoice && (
            <Link href={"/billing/invoices/" + invoice} className="font-medium underline">
              Ver a fatura proporcional
            </Link>
          )}
        </Alert>
      )}
      {changed === "period_end" && (
        <Alert tone="success" title="Mudança agendada">
          O novo plano começa no fim do período atual. Você pode desfazer a mudança até lá.
        </Alert>
      )}

      {subscription ? (
        <SubscriptionCard subscription={subscription} isAdmin={isAdmin} pendingPlanName={pendingPlanName} />
      ) : (
        <Alert tone="brand">A assinatura ainda está sendo criada. Recarregue em alguns segundos.</Alert>
      )}

      {isAdmin && (
        <Card className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex items-center gap-4">
            <span className="grid size-10 place-items-center rounded-lg bg-slate-100">
              <CreditCard className="size-5 text-slate-600" aria-hidden />
            </span>
            <div>
              <CardTitle>Meio de pagamento</CardTitle>
              <p className="text-sm text-slate-600">
                {paymentMethod
                  ? paymentMethod.brand.toUpperCase() + " final " + paymentMethod.last4 + " · atualizado em " + formatDate(paymentMethod.updatedAt)
                  : "Nenhum cartão cadastrado. Sem ele, o teste termina sem conversão e o acesso é bloqueado."}
              </p>
            </div>
          </div>
          <ButtonLink href="/billing/payment-method" variant={paymentMethod ? "secondary" : "primary"}>
            {paymentMethod ? "Trocar cartão" : "Cadastrar cartão"}
          </ButtonLink>
        </Card>
      )}
    </div>
  );
}
