import Link from "next/link";
import { formatDate, formatDateTime, formatMoney } from "@/shared/format";
import { ActionButton } from "@/shared/ui/action-button";
import { Badge, Card, CardTitle, DescriptionList, EmptyState } from "@/shared/ui/primitives";
import { payInvoiceAction } from "../actions";
import { invoiceStatusLabel, invoiceStatusTone, reasonLabel, voidReasonLabel, type Invoice, type InvoiceStatus, type InvoiceSummary } from "../model";

export function InvoiceStatusBadge({ status }: { status: InvoiceStatus }) {
  return <Badge tone={invoiceStatusTone[status]}>{invoiceStatusLabel[status]}</Badge>;
}

/** Tabela no desktop, lista de cartões no celular (sem rolagem horizontal). */
export function InvoiceTable({ invoices }: { invoices: InvoiceSummary[] }) {
  if (invoices.length === 0) {
    return <EmptyState title="Nenhuma fatura ainda" description="A primeira fatura é emitida no fim do período de teste." />;
  }

  return (
    <>
      <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200 bg-white md:hidden">
        {invoices.map((invoice) => (
          <li key={invoice.id}>
            <Link href={"/billing/invoices/" + invoice.id} className="flex items-center justify-between gap-4 p-4 hover:bg-slate-50">
              <span>
                <span className="block text-sm font-medium text-slate-900">{invoice.number}</span>
                <span className="block text-xs text-slate-500">
                  {reasonLabel[invoice.reason] ?? invoice.reason} · {formatDate(invoice.issuedAt)}
                </span>
              </span>
              <span className="text-right">
                <span className="block text-sm font-semibold text-slate-900">{formatMoney(invoice.totalCents)}</span>
                <InvoiceStatusBadge status={invoice.status} />
              </span>
            </Link>
          </li>
        ))}
      </ul>

      <div className="hidden overflow-hidden rounded-xl border border-slate-200 bg-white md:block">
        <table className="w-full text-left text-sm">
          <caption className="sr-only">Faturas, das mais recentes para as mais antigas</caption>
          <thead className="bg-slate-50 text-xs font-medium tracking-wide text-slate-500 uppercase">
            <tr>
              <th scope="col" className="px-4 py-3">Número</th>
              <th scope="col" className="px-4 py-3">Motivo</th>
              <th scope="col" className="px-4 py-3">Emitida em</th>
              <th scope="col" className="px-4 py-3">Período</th>
              <th scope="col" className="px-4 py-3 text-right">Total</th>
              <th scope="col" className="px-4 py-3">Status</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {invoices.map((invoice) => (
              <tr key={invoice.id} className="hover:bg-slate-50">
                <td className="px-4 py-3 font-medium">
                  <Link href={"/billing/invoices/" + invoice.id} className="text-brand-700 hover:underline">
                    {invoice.number}
                  </Link>
                </td>
                <td className="px-4 py-3 text-slate-700">{reasonLabel[invoice.reason] ?? invoice.reason}</td>
                <td className="px-4 py-3 text-slate-700">{formatDate(invoice.issuedAt)}</td>
                <td className="px-4 py-3 text-slate-700">
                  {formatDate(invoice.periodStart)} a {formatDate(invoice.periodEnd)}
                </td>
                <td className="px-4 py-3 text-right font-semibold tabular-nums">{formatMoney(invoice.totalCents)}</td>
                <td className="px-4 py-3">
                  <InvoiceStatusBadge status={invoice.status} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}

export function InvoiceDetail({ invoice }: { invoice: Invoice }) {
  return (
    <div className="space-y-6">
      <Card className="space-y-6">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <CardTitle>Fatura {invoice.number}</CardTitle>
          <InvoiceStatusBadge status={invoice.status} />
        </div>
        <DescriptionList
          items={[
            { term: "Motivo", value: reasonLabel[invoice.reason] ?? invoice.reason },
            { term: "Período", value: formatDate(invoice.periodStart) + " a " + formatDate(invoice.periodEnd) },
            { term: "Emitida em", value: formatDateTime(invoice.issuedAt) },
            {
              term: invoice.status === "Void" ? "Anulada em" : "Paga em",
              value: invoice.status === "Void" && invoice.voidedAt
                ? formatDateTime(invoice.voidedAt) + (invoice.voidReason ? " · " + (voidReasonLabel[invoice.voidReason] ?? invoice.voidReason) : "")
                : invoice.paidAt ? formatDateTime(invoice.paidAt) : "—",
            },
          ]}
        />
        {invoice.status === "Open" && (
          <div className="border-t border-slate-100 pt-6">
            <ActionButton variant="primary" action={payInvoiceAction.bind(null, invoice.id)} watchFor={30_000}>
              Pagar agora
            </ActionButton>
          </div>
        )}
      </Card>

      <Card className="p-0">
        <table className="w-full text-left text-sm">
          <caption className="px-6 pt-6 pb-2 text-left text-base font-semibold text-slate-900">Itens</caption>
          <thead className="text-xs font-medium tracking-wide text-slate-500 uppercase">
            <tr>
              <th scope="col" className="px-6 py-3">Descrição</th>
              <th scope="col" className="px-6 py-3 text-right">Valor</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {invoice.lines.map((line, index) => (
              <tr key={index}>
                <td className="px-6 py-3">
                  <span className="block text-slate-900">{line.description}</span>
                  <span className="block text-xs text-slate-500">
                    {formatDate(line.periodStart)} a {formatDate(line.periodEnd)}
                  </span>
                </td>
                <td className={"px-6 py-3 text-right tabular-nums " + (line.amountCents < 0 ? "text-emerald-700" : "text-slate-900")}>
                  {formatMoney(line.amountCents)}
                </td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr className="border-t border-slate-200">
              <th scope="row" className="px-6 py-4 text-left font-semibold">Total</th>
              <td className="px-6 py-4 text-right text-base font-bold tabular-nums">{formatMoney(invoice.totalCents)}</td>
            </tr>
          </tfoot>
        </table>
      </Card>
    </div>
  );
}
