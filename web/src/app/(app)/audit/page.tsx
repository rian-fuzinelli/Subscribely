import type { Metadata } from "next";
import { AuditFilterForm, AuditTable, queryAudit, type AuditFilters } from "@/features/audit";
import { requireAdmin } from "@/shared/auth/session";
import { PageHeader } from "@/shared/ui/primitives";

export const metadata: Metadata = { title: "Auditoria" };

type Search = { source?: string; from?: string; to?: string; action?: string; cursor?: string };

const date = /^\d{4}-\d{2}-\d{2}$/;

export default async function AuditPage({ searchParams }: { searchParams: Promise<Search> }) {
  await requireAdmin("/audit");
  const search = await searchParams;
  const filters: AuditFilters = {
    source: search.source === "product" ? "product" : "control",
    from: search.from && date.test(search.from) ? search.from : undefined,
    to: search.to && date.test(search.to) ? search.to : undefined,
    action: search.action?.trim() || undefined,
    cursor: search.cursor || undefined,
  };
  const page = await queryAudit(filters);
  return (
    <div className="space-y-8">
      <PageHeader title="Auditoria" description="Quem fez o quê e quando, na conta e no produto." />
      <AuditFilterForm filters={filters} />
      <AuditTable page={page} filters={filters} />
    </div>
  );
}
