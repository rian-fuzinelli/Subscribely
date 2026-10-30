import "server-only";
import { api } from "@/shared/api/client";
import type { Invoice, InvoicePage } from "./model";

export const PAGE_SIZE = 20;

export async function listInvoices(cursor?: string, limit = PAGE_SIZE): Promise<InvoicePage> {
  const query = new URLSearchParams({ limit: String(limit) });
  if (cursor) {
    query.set("cursor", cursor);
  }

  const result = await api<InvoicePage>("/v1/invoices?" + query.toString());
  return result.ok ? result.data : { items: [], nextCursor: null };
}

/** `null` quando não existe ou é de outro tenant (a API responde 404 nos dois casos). */
export async function getInvoice(id: string): Promise<Invoice | null> {
  if (!/^[0-9a-f-]{36}$/i.test(id)) {
    return null;
  }

  const result = await api<Invoice>("/v1/invoices/" + id);
  return result.ok ? result.data : null;
}
