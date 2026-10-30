export type InvoiceStatus = "Open" | "Paid" | "Void";

export type InvoiceSummary = {
  id: string;
  number: string;
  reason: string;
  status: InvoiceStatus;
  periodStart: string;
  periodEnd: string;
  totalCents: number;
  currency: string;
  issuedAt: string;
  paidAt: string | null;
};

export type Invoice = InvoiceSummary & {
  voidedAt: string | null;
  voidReason: string | null;
  lines: { description: string; amountCents: number; periodStart: string; periodEnd: string }[];
};

export type InvoicePage = { items: InvoiceSummary[]; nextCursor: string | null };

export const reasonLabel: Record<string, string> = {
  TrialConversion: "Fim do teste",
  Renewal: "Renovação",
  Upgrade: "Upgrade (proporcional)",
  Reactivation: "Reativação",
};

export const invoiceStatusLabel: Record<InvoiceStatus, string> = { Open: "Em aberto", Paid: "Paga", Void: "Anulada" };

export const invoiceStatusTone: Record<InvoiceStatus, "warning" | "success" | "neutral"> = { Open: "warning", Paid: "success", Void: "neutral" };

export const voidReasonLabel: Record<string, string> = {
  "suspension-expired": "Assinatura cancelada após 30 dias suspensa",
};
