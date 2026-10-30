import { ArrowLeft } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { InvoiceDetail, getInvoice } from "@/features/invoices";
import { requireAdmin } from "@/shared/auth/session";

export const metadata: Metadata = { title: "Fatura" };

/** Também é o destino do link "Ver fatura" dos e-mails (`/billing/invoices/{id}`). */
export default async function InvoicePage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  await requireAdmin("/billing/invoices/" + id);
  const invoice = await getInvoice(id);
  if (!invoice) {
    notFound();
  }

  return (
    <div className="space-y-6">
      <Link href="/billing/invoices" className="inline-flex items-center gap-1 text-meta font-medium text-ledger-700 hover:underline">
        <ArrowLeft className="size-4" aria-hidden /> Faturas
      </Link>
      <InvoiceDetail invoice={invoice} />
    </div>
  );
}
