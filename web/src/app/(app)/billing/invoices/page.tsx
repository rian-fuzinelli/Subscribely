import type { Metadata } from "next";
import { InvoiceTable, listInvoices } from "@/features/invoices";
import { requireAdmin } from "@/shared/auth/session";
import { ButtonLink } from "@/shared/ui/button";
import { PageHeader } from "@/shared/ui/primitives";

export const metadata: Metadata = { title: "Faturas" };

export default async function InvoicesPage({ searchParams }: { searchParams: Promise<{ cursor?: string }> }) {
  await requireAdmin("/billing/invoices");
  const { cursor } = await searchParams;
  const page = await listInvoices(cursor);
  return (
    <div className="space-y-8">
      <PageHeader title="Faturas" description="Todas as cobranças da conta, das mais recentes para as mais antigas." />
      <InvoiceTable invoices={page.items} />
      <nav aria-label="Paginação" className="flex justify-between">
        {cursor ? (
          <ButtonLink href="/billing/invoices" variant="secondary">
            Mais recentes
          </ButtonLink>
        ) : (
          <span />
        )}
        {page.nextCursor && (
          <ButtonLink href={"/billing/invoices?cursor=" + encodeURIComponent(page.nextCursor)} variant="secondary">
            Mais antigas
          </ButtonLink>
        )}
      </nav>
    </div>
  );
}
