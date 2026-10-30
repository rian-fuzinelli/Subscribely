import Link from "next/link";
import { formatDateTime } from "@/shared/format";
import { buttonClass } from "@/shared/ui/button";
import { inputClass } from "@/shared/ui/field";
import { Alert, Badge, EmptyState } from "@/shared/ui/primitives";
import type { AuditFilters, AuditPage } from "../api";
import { actorLabel, describeAction } from "../labels";

/** Filtros por GET: funcionam sem JavaScript e o endereço pode ser compartilhado. */
export function AuditFilterForm({ filters }: { filters: AuditFilters }) {
  return (
    <form method="get" className="grid gap-4 rounded-surface border border-line bg-paper p-4 sm:grid-cols-2 lg:grid-cols-[10rem_1fr_1fr_1fr_auto] lg:items-end">
      <div className="space-y-1.5">
        <label htmlFor="audit-source" className="block text-meta font-medium text-ink">Fonte</label>
        <select id="audit-source" name="source" defaultValue={filters.source} className={inputClass}>
          <option value="control">Conta e cobrança</option>
          <option value="product">Produto</option>
        </select>
      </div>
      <div className="space-y-1.5">
        <label htmlFor="audit-from" className="block text-meta font-medium text-ink">De</label>
        <input id="audit-from" type="date" name="from" defaultValue={filters.from} className={inputClass} />
      </div>
      <div className="space-y-1.5">
        <label htmlFor="audit-to" className="block text-meta font-medium text-ink">Até</label>
        <input id="audit-to" type="date" name="to" defaultValue={filters.to} className={inputClass} />
      </div>
      <div className="space-y-1.5">
        <label htmlFor="audit-action" className="block text-meta font-medium text-ink">Ação</label>
        <input id="audit-action" name="action" placeholder="ex.: subscription.updated" defaultValue={filters.action} className={inputClass} />
      </div>
      <button type="submit" className={buttonClass({ variant: "secondary" })}>Filtrar</button>
    </form>
  );
}

export function AuditTable({ page, filters }: { page: AuditPage; filters: AuditFilters }) {
  if (page.error) {
    return <Alert tone="danger">{page.error}</Alert>;
  }

  if (page.items.length === 0) {
    return <EmptyState title="Nenhum registro encontrado" description="Ajuste os filtros ou o período." />;
  }

  const next = new URLSearchParams(
    Object.entries({ ...filters, cursor: page.nextCursor ?? "" }).filter(([, value]) => value) as [string, string][],
  );
  return (
    <div className="space-y-4">
      <ol className="divide-y divide-line rounded-surface border border-line bg-paper">
        {page.items.map((entry) => (
          <li key={entry.id} className="p-4">
            <details className="group">
              <summary className="flex cursor-pointer list-none flex-col gap-1 sm:flex-row sm:items-center sm:justify-between">
                <span className="text-meta text-ink">
                  <Badge className="mr-2">{actorLabel(entry.actorType)}</Badge>
                  {describeAction(entry.action)}
                  {entry.entityId && <span className="ml-1 font-mono text-meta text-ink-faint">{entry.entityId.slice(0, 8)}</span>}
                </span>
                <time dateTime={entry.occurredAt} className="text-meta text-ink-faint">{formatDateTime(entry.occurredAt)}</time>
              </summary>
              <div className="mt-3 grid gap-3 text-meta md:grid-cols-2">
                <JsonBlock title="Antes" value={entry.before} />
                <JsonBlock title="Depois" value={entry.after} />
              </div>
            </details>
          </li>
        ))}
      </ol>
      {page.nextCursor && (
        <div className="flex justify-center">
          <Link href={"/audit?" + next.toString()} className={buttonClass({ variant: "secondary" })}>
            Registros mais antigos
          </Link>
        </div>
      )}
    </div>
  );
}

function JsonBlock({ title, value }: { title: string; value: string | null }) {
  return (
    <div>
      <p className="mb-1 font-medium text-ink-soft">{title}</p>
      <pre className="max-h-60 overflow-auto rounded-control bg-canvas p-3 text-ink">{value ? JSON.stringify(JSON.parse(value), null, 2) : "—"}</pre>
    </div>
  );
}
