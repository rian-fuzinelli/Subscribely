import Link from "next/link";
import { formatDate, formatDateRange, formatDateTime, formatMoney } from "@/shared/format";
import { ActionButton } from "@/shared/ui/action-button";
import { Ledger } from "@/shared/ui/ledger";
import { Badge, Card, EmptyState } from "@/shared/ui/primitives";
import { payInvoiceAction } from "../actions";
import { invoiceStatusLabel, invoiceStatusTone, reasonLabel, voidReasonLabel, type Invoice, type InvoiceStatus, type InvoiceSummary } from "../model";

export function InvoiceStatusBadge({ status }: { status: InvoiceStatus }) {
  return <Badge tone={invoiceStatusTone[status]}>{invoiceStatusLabel[status]}</Badge>;
}

const reason = (invoice: InvoiceSummary) => reasonLabel[invoice.reason] ?? invoice.reason;

/**
 * Tabela com hairlines no desktop; no celular, cada fatura vira uma linha de extrato (número, motivo e valor)
 * sem rolagem horizontal. `compact` omite o período (visão geral).
 */
export function InvoiceTable({ invoices, compact = false }: { invoices: InvoiceSummary[]; compact?: boolean }) {
  if (invoices.length === 0) {
    return <EmptyState title="Nenhuma fatura ainda" description="A primeira é emitida e cobrada no fim do período de teste." />;
  }

  return (
    <>
      <ul className="divide-y divide-line border-y border-line md:hidden">
        {invoices.map((invoice) => (
          <li key={invoice.id}>
            <Link href={"/billing/invoices/" + invoice.id} className="flex items-baseline justify-between gap-4 py-3 hover:bg-paper">
              <span className="min-w-0">
                <span className="block font-medium">{invoice.number}</span>
                <span className="block text-meta text-ink-faint">
                  {reason(invoice)}, {formatDate(invoice.issuedAt)}
                </span>
              </span>
              <span className="shrink-0 text-right">
                <span className="numeric block font-semibold">{formatMoney(invoice.totalCents)}</span>
                <InvoiceStatusBadge status={invoice.status} />
              </span>
            </Link>
          </li>
        ))}
      </ul>

      <table className="hidden w-full border-collapse text-left md:table">
        <caption className="sr-only">Faturas, das mais recentes para as mais antigas</caption>
        <thead>
          <tr className="border-b border-ink text-meta text-ink-soft">
            <th scope="col" className="py-2 pr-4 font-medium">Número</th>
            <th scope="col" className="py-2 pr-4 font-medium">Motivo</th>
            <th scope="col" className="py-2 pr-4 font-medium">Emitida</th>
            {!compact && <th scope="col" className="py-2 pr-4 font-medium">Período</th>}
            <th scope="col" className="py-2 pr-4 font-medium">Situação</th>
            <th scope="col" className="py-2 text-right font-medium">Total</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-line">
          {invoices.map((invoice) => (
            <tr key={invoice.id} className="group">
              <td className="py-3 pr-4">
                <Link href={"/billing/invoices/" + invoice.id} className="font-medium text-ledger-700 underline-offset-4 group-hover:underline">
                  {invoice.number}
                </Link>
              </td>
              <td className="py-3 pr-4 text-ink-soft">{reason(invoice)}</td>
              <td className="numeric py-3 pr-4 text-ink-soft">{formatDate(invoice.issuedAt)}</td>
              {!compact && (
                <td className="numeric py-3 pr-4 text-ink-soft">
                  {formatDateRange(invoice.periodStart, invoice.periodEnd)}
                </td>
              )}
              <td className="py-3 pr-4">
                <InvoiceStatusBadge status={invoice.status} />
              </td>
              <td className="numeric py-3 text-right font-semibold">{formatMoney(invoice.totalCents)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}

/** A fatura como documento: cabeçalho, extrato dos itens e o total; a ação de pagar fica junto do valor. */
export function InvoiceDetail({ invoice }: { invoice: Invoice }) {
  const settled =
    invoice.status === "Void" && invoice.voidedAt
      ? "Anulada em " + formatDateTime(invoice.voidedAt) + (invoice.voidReason ? ". " + (voidReasonLabel[invoice.voidReason] ?? invoice.voidReason) + "." : ".")
      : invoice.paidAt
        ? "Paga em " + formatDateTime(invoice.paidAt) + "."
        : "Aguardando pagamento.";

  return (
    <Card className="max-w-2xl p-6 sm:p-8">
      <header className="flex flex-wrap items-start justify-between gap-4 border-b border-line pb-5">
        <div>
          <h1 className="text-title font-semibold">Fatura {invoice.number}</h1>
          <p className="mt-1 text-ink-soft">
            {reasonLabel[invoice.reason] ?? invoice.reason}. Emitida em {formatDateTime(invoice.issuedAt)}.
          </p>
          <p className="numeric text-meta text-ink-faint">
            Período de {formatDateRange(invoice.periodStart, invoice.periodEnd)}
          </p>
        </div>
        <InvoiceStatusBadge status={invoice.status} />
      </header>

      <Ledger
        className="mt-4"
        rows={invoice.lines.map((line) => ({
          label: line.description,
          detail: formatDateRange(line.periodStart, line.periodEnd),
          value: formatMoney(line.amountCents),
          credit: line.amountCents < 0,
        }))}
        total={{ label: "Total", value: formatMoney(invoice.totalCents) }}
      />

      <footer className="mt-6 flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <p className="text-meta text-ink-soft">{settled}</p>
        {invoice.status === "Open" && (
          <ActionButton variant="primary" action={payInvoiceAction.bind(null, invoice.id)} watchFor={30_000}>
            Pagar {formatMoney(invoice.totalCents)} agora
          </ActionButton>
        )}
      </footer>
    </Card>
  );
}
