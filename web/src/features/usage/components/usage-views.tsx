import { formatDateTime, formatNumber, formatPeriod } from "@/shared/format";
import { ButtonLink } from "@/shared/ui/button";
import { Alert, Card, CardTitle, EmptyState, Meter } from "@/shared/ui/primitives";
import type { Usage, UsageHistoryItem } from "../api";
import { quotaResetsAt, usagePercent } from "../reset";

/** Requisições e assentos do mês, com alerta a partir de 80% (FR-015). */
export function UsageSummary({ usage, isAdmin }: { usage: Usage; isAdmin: boolean }) {
  const percent = usagePercent(usage.requestsUsed, usage.requestLimit);
  const resetsAt = formatDateTime(quotaResetsAt(usage.period));
  return (
    <div className="space-y-4">
      {percent >= 80 && (
        <Alert
          tone={percent >= 100 ? "danger" : "warning"}
          title={percent >= 100 ? "Limite de requisições atingido" : "Você já usou " + percent + "% das requisições do mês"}
          action={isAdmin ? <ButtonLink href="/billing/change-plan" size="sm" variant="secondary">Ver planos</ButtonLink> : undefined}
        >
          {percent >= 100
            ? "As novas requisições ao produto serão recusadas até " + resetsAt + ". Um upgrade libera mais requisições na hora."
            : "O contador zera em " + resetsAt + "."}
        </Alert>
      )}
      <div className="grid gap-4 md:grid-cols-2">
        <Card className="space-y-3">
          <CardTitle>Requisições em {formatPeriod(usage.period)}</CardTitle>
          <p className="text-3xl font-bold tabular-nums text-slate-900">
            {formatNumber(usage.requestsUsed)}
            <span className="text-base font-normal text-slate-500"> de {formatNumber(usage.requestLimit)}</span>
          </p>
          <Meter label="Requisições usadas no mês" value={usage.requestsUsed} max={usage.requestLimit} />
          <p className="text-xs text-slate-500">Zera em {resetsAt}.</p>
        </Card>
        <Card className="space-y-3">
          <CardTitle>Assentos</CardTitle>
          <p className="text-3xl font-bold tabular-nums text-slate-900">
            {usage.seatsUsed}
            <span className="text-base font-normal text-slate-500"> de {usage.seatLimit}</span>
          </p>
          <Meter label="Assentos ocupados" value={usage.seatsUsed} max={usage.seatLimit} />
          <p className="text-xs text-slate-500">Usuários ativos e convites pendentes ocupam assentos.</p>
        </Card>
      </div>
    </div>
  );
}

export function UsageHistory({ items }: { items: UsageHistoryItem[] }) {
  if (items.length === 0) {
    return <EmptyState title="Ainda sem histórico" description="O uso de cada mês é registrado de hora em hora." />;
  }

  const max = Math.max(...items.map((item) => item.requestsCount), 1);
  return (
    <Card className="space-y-4">
      <CardTitle>Últimos 12 meses</CardTitle>
      <ul className="space-y-3">
        {items.map((item) => (
          <li key={item.period} className="grid grid-cols-[8rem_1fr_auto] items-center gap-3 text-sm">
            <span className="text-slate-700 capitalize">{formatPeriod(item.period)}</span>
            <span aria-hidden className="h-2 rounded-full bg-brand-200">
              <span className="block h-2 rounded-full bg-brand-600" style={{ width: (item.requestsCount / max) * 100 + "%" }} />
            </span>
            <span className="text-right tabular-nums text-slate-900">{formatNumber(item.requestsCount)}</span>
          </li>
        ))}
      </ul>
    </Card>
  );
}
