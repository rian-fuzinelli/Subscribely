import type { ReactNode } from "react";
import { cn } from "@/shared/cn";
import { formatDateTime, formatNumber, formatPeriod } from "@/shared/format";
import { ButtonLink } from "@/shared/ui/button";
import { Alert, EmptyState, Meter, SectionTitle } from "@/shared/ui/primitives";
import type { Usage, UsageHistoryItem } from "../api";
import { quotaResetsAt, usagePercent } from "../reset";

/** Um medidor do mês: régua de tinta no topo, o número em destaque e o contexto embaixo. */
function UsageBlock({ title, used, limit, note }: { title: string; used: number; limit: number; note: ReactNode }) {
  return (
    <div className="border-t-2 border-ink pt-3">
      <p className="text-meta text-ink-soft">{title}</p>
      <p className="numeric mt-1 text-title font-semibold">
        {formatNumber(used)}
        <span className="text-body font-normal text-ink-faint"> de {formatNumber(limit)}</span>
      </p>
      <div className="mt-3">
        <Meter label={title} value={used} max={limit} />
      </div>
      <p className="mt-2 text-meta text-ink-faint">{note}</p>
    </div>
  );
}

/**
 * Requisições e assentos do mês, com alerta a partir de 80% (FR-015).
 * `stack` empilha os medidores (coluna lateral da visão geral); `row` os coloca lado a lado.
 */
export function UsageSummary({ usage, isAdmin, layout = "row" }: { usage: Usage; isAdmin: boolean; layout?: "row" | "stack" }) {
  const percent = usagePercent(usage.requestsUsed, usage.requestLimit);
  const resetsAt = formatDateTime(quotaResetsAt(usage.period));
  return (
    <div className="space-y-6">
      {percent >= 80 && (
        <Alert
          tone={percent >= 100 ? "danger" : "warning"}
          title={percent >= 100 ? "Limite de requisições atingido" : "Você já usou " + percent + "% das requisições do mês"}
          action={isAdmin ? <ButtonLink href="/billing/change-plan" size="sm" variant="secondary">Ver planos</ButtonLink> : undefined}
        >
          {percent >= 100
            ? "Novas requisições ao produto são recusadas até " + resetsAt + ". Um upgrade libera mais na hora."
            : "O contador zera em " + resetsAt + "."}
        </Alert>
      )}
      <div className={cn("grid gap-8", layout === "row" && "md:grid-cols-2")}>
        <UsageBlock
          title={"Requisições em " + formatPeriod(usage.period)}
          used={usage.requestsUsed}
          limit={usage.requestLimit}
          note={"Zera em " + resetsAt + "."}
        />
        <UsageBlock title="Assentos" used={usage.seatsUsed} limit={usage.seatLimit} note="Usuários ativos e convites pendentes ocupam assentos." />
      </div>
    </div>
  );
}

/** Histórico em barras horizontais: comparar meses é a única pergunta que este gráfico responde. */
export function UsageHistory({ items }: { items: UsageHistoryItem[] }) {
  return (
    <section aria-labelledby="historico" className="space-y-4">
      <SectionTitle id="historico">Últimos 12 meses</SectionTitle>
      {items.length === 0 ? (
        <EmptyState title="Ainda sem histórico" description="O uso de cada mês é registrado de hora em hora. Volte depois das primeiras chamadas ao produto." />
      ) : (
        <ul className="divide-y divide-line border-y border-line">
          {items.map((item) => (
            <li key={item.period} className="grid grid-cols-[9rem_1fr_auto] items-center gap-4 py-2.5 text-meta">
              <span className="text-ink-soft first-letter:uppercase">{formatPeriod(item.period)}</span>
              <span aria-hidden className="h-1.5 bg-line">
                <span className="block h-1.5 bg-ledger-600" style={{ width: (item.requestsCount / Math.max(...items.map((i) => i.requestsCount), 1)) * 100 + "%" }} />
              </span>
              <span className="numeric text-right text-ink">{formatNumber(item.requestsCount)}</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
