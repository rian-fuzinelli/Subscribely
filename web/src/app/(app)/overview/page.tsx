import { ArrowRight } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { InvoiceTable, listInvoices } from "@/features/invoices";
import { SubscriptionCard, getSubscription } from "@/features/subscription";
import { UsageSummary, getUsage } from "@/features/usage";
import { requireSession } from "@/shared/auth/session";
import { Alert, PageHeader } from "@/shared/ui/primitives";

export const metadata: Metadata = { title: "Visão geral" };

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
      <PageHeader title="Visão geral" description="Assinatura, uso do mês e as últimas faturas da sua conta." />
      {denied && <Alert tone="warning">Essa página é só para administradores da conta.</Alert>}

      {subscription ? (
        <SubscriptionCard subscription={subscription} isAdmin={isAdmin} />
      ) : (
        <Alert tone="brand" title="Estamos terminando de preparar sua assinatura">Recarregue a página em alguns segundos.</Alert>
      )}

      {usage && <UsageSummary usage={usage} isAdmin={isAdmin} />}

      {invoices && (
        <section aria-labelledby="ultimas-faturas" className="space-y-4">
          <div className="flex items-center justify-between">
            <h2 id="ultimas-faturas" className="text-base font-semibold text-slate-900">Últimas faturas</h2>
            <Link href="/billing/invoices" className="inline-flex items-center gap-1 text-sm font-medium text-brand-700 hover:underline">
              Ver todas <ArrowRight className="size-4" aria-hidden />
            </Link>
          </div>
          <InvoiceTable invoices={invoices.items} />
        </section>
      )}
    </div>
  );
}
