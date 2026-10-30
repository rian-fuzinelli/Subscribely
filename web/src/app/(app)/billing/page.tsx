import type { Metadata } from "next";
import { getPaymentMethod } from "@/features/payment-method";
import { listPlans } from "@/features/pricing";
import { SubscriptionCard, getSubscription } from "@/features/subscription";
import { requireSession } from "@/shared/auth/session";
import { formatDate } from "@/shared/format";
import { ButtonLink, TextLink } from "@/shared/ui/button";
import { Ledger } from "@/shared/ui/ledger";
import { Alert, PageHeader, Section } from "@/shared/ui/primitives";

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
    <div className="max-w-3xl space-y-10">
      <PageHeader title="Assinatura" description="Plano, período e como você paga." />

      {changed === "immediate" && (
        <Alert tone="success" title="Plano alterado">
          A mudança já vale.{" "}
          {invoice && <TextLink href={"/billing/invoices/" + invoice}>Abrir a fatura proporcional</TextLink>}
        </Alert>
      )}
      {changed === "period_end" && (
        <Alert tone="success" title="Mudança agendada">
          O novo plano começa no fim do período atual. Dá para desfazer até lá.
        </Alert>
      )}

      {subscription ? (
        <SubscriptionCard subscription={subscription} isAdmin={isAdmin} pendingPlanName={pendingPlanName} />
      ) : (
        <Alert tone="brand">A assinatura ainda está sendo criada. Recarregue em alguns segundos.</Alert>
      )}

      {isAdmin && (
        <Section title="Meio de pagamento" description="Usado na primeira fatura, nas renovações e nas novas tentativas de cobrança.">
          {paymentMethod ? (
            <Ledger
              className="border-y border-line"
              rows={[
                {
                  label: "Cartão " + paymentMethod.brand.toUpperCase() + " final " + paymentMethod.last4,
                  detail: "Atualizado em " + formatDate(paymentMethod.updatedAt),
                  value: <TextLink href="/billing/payment-method">Trocar cartão</TextLink>,
                },
              ]}
            />
          ) : (
            <Alert
              tone="warning"
              title="Nenhum cartão cadastrado"
              action={<ButtonLink href="/billing/payment-method" size="sm">Cadastrar cartão</ButtonLink>}
            >
              Sem cartão, o teste termina sem cobrança e o acesso ao produto é pausado. Os dados ficam guardados.
            </Alert>
          )}
        </Section>
      )}
    </div>
  );
}
