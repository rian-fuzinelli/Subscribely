import type { Metadata } from "next";
import { InvoiceTable, listInvoices } from "@/features/invoices";
import { SubscriptionCard, getSubscription } from "@/features/subscription";
import { UsageSummary, getUsage } from "@/features/usage";
import { requireSession } from "@/shared/auth/session";
import { TextLink } from "@/shared/ui/button";
import { Alert, SectionTitle } from "@/shared/ui/primitives";

export const metadata: Metadata = { title: "Visão geral" };

/**
 * Coluna principal: o documento (assinatura) e as últimas faturas. Coluna lateral, mais estreita: os medidores
 * do mês — a pergunta "quanto usei" é secundária à "quanto pago e até quando".
 */
export default async function OverviewPage({ searchParams }: { searchParams: Promise<{ denied?: string }> }) {
  const session = await requireSession("/overview");
  const isAdmin = session.role === "TenantAdmin";
  const [{ denied }, subscription, usage, invoices] = await Promise.all([
    searchParams,
    getSubscription(),
    getUsage(),
    isAdmin ? listInvoices(undefined, 3) : Promise.resolve(null),
  ]);

  return (
    <div className="space-y-8">
      <h1 className="text-title font-semibold tracking-tight">Visão geral</h1>
      {denied && <Alert tone="warning">Essa página é só para administradores da conta.</Alert>}

      {/* Celular: assinatura, uso e faturas, nessa ordem. Desktop: uso numa coluna lateral ao lado das duas. */}
      <div className="grid gap-10 lg:grid-cols-[minmax(0,1.65fr)_minmax(0,1fr)] lg:gap-x-12">
        <div className="lg:col-start-1">
          {subscription ? (
            <SubscriptionCard subscription={subscription} isAdmin={isAdmin} />
          ) : (
            <Alert tone="brand" title="A assinatura está sendo criada">Recarregue a página em alguns segundos.</Alert>
          )}
        </div>

        <aside aria-label="Uso do mês" className="lg:col-start-2 lg:row-span-2 lg:row-start-1">
          {usage && <UsageSummary usage={usage} isAdmin={isAdmin} layout="stack" />}
        </aside>

        {invoices && (
          <section aria-labelledby="ultimas-faturas" className="space-y-3 lg:col-start-1">
            <div className="flex items-baseline justify-between">
              <SectionTitle id="ultimas-faturas">Últimas faturas</SectionTitle>
              {invoices.items.length > 0 && <TextLink href="/billing/invoices" className="text-meta">Todas as faturas</TextLink>}
            </div>
            <InvoiceTable invoices={invoices.items} compact />
          </section>
        )}
      </div>
    </div>
  );
}
