import type { ReactNode } from "react";
import { cn } from "@/shared/cn";

export type LedgerRow = {
  label: ReactNode;
  /** Linha de apoio sob o rótulo (período, observação). */
  detail?: ReactNode;
  value: ReactNode;
  /** Crédito: valor em verde. */
  credit?: boolean;
};

/**
 * Extrato: rótulo, linha pontilhada e valor alinhado à direita em algarismos tabulares — a identidade
 * visual do produto, usada em faturas, no resumo da assinatura e na amostra da página inicial.
 */
export function Ledger({ rows, total, className }: { rows: LedgerRow[]; total?: { label: ReactNode; value: ReactNode }; className?: string }) {
  return (
    <dl className={cn("text-ink", className)}>
      {rows.map((row, index) => (
        <div key={index} className="grid grid-cols-[minmax(0,1fr)_auto] items-baseline gap-x-2 py-2">
          <dt className="flex min-w-0 items-baseline gap-2">
            <span className="min-w-0">{row.label}</span>
            <span aria-hidden className="mb-1 min-w-6 flex-1 border-b border-dotted border-line-strong" />
          </dt>
          <dd className={cn("numeric text-right", row.credit && "text-ledger-700")}>{row.value}</dd>
          {row.detail && <dd className="col-span-2 mt-0.5 text-meta text-ink-faint">{row.detail}</dd>}
        </div>
      ))}
      {total && (
        <div className="mt-2 flex items-baseline justify-between gap-4 border-t-2 border-ink pt-3">
          <dt className="font-semibold">{total.label}</dt>
          <dd className="numeric text-title font-semibold">{total.value}</dd>
        </div>
      )}
    </dl>
  );
}
