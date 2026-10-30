import "server-only";
import { api } from "@/shared/api/client";

export type AuditEntry = {
  id: string;
  occurredAt: string;
  actorType: string;
  actorId: string | null;
  action: string;
  entityType: string | null;
  entityId: string | null;
  before: string | null;
  after: string | null;
};

export type AuditFilters = { source: "control" | "product"; from?: string; to?: string; action?: string; cursor?: string };

export type AuditPage = { items: AuditEntry[]; nextCursor: string | null; error?: string };

/** `from`/`to` chegam como datas `yyyy-MM-dd` do formulário e viram o intervalo do dia em São Paulo. */
export async function queryAudit(filters: AuditFilters): Promise<AuditPage> {
  const query = new URLSearchParams({ source: filters.source });
  if (filters.from) {
    query.set("from", filters.from + "T00:00:00-03:00");
  }

  if (filters.to) {
    query.set("to", filters.to + "T23:59:59.999-03:00");
  }

  if (filters.action) {
    query.set("action", filters.action);
  }

  if (filters.cursor) {
    query.set("cursor", filters.cursor);
  }

  const result = await api<AuditPage>("/v1/audit?" + query.toString());
  return result.ok ? result.data : { items: [], nextCursor: null, error: result.error.message };
}
