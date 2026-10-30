import type { Metadata } from "next";
import { UsageHistory, UsageSummary, getUsage, getUsageHistory } from "@/features/usage";
import { requireSession } from "@/shared/auth/session";
import { Alert, PageHeader } from "@/shared/ui/primitives";

export const metadata: Metadata = { title: "Uso" };

export default async function UsagePage() {
  const session = await requireSession("/usage");
  const [usage, history] = await Promise.all([getUsage(), getUsageHistory()]);
  return (
    <div className="space-y-8">
      <PageHeader title="Uso" description="Requisições ao produto no mês (UTC) e assentos ocupados." />
      {usage ? (
        <UsageSummary usage={usage} isAdmin={session.role === "TenantAdmin"} />
      ) : (
        <Alert tone="warning">Não foi possível carregar o uso agora. Tente de novo em instantes.</Alert>
      )}
      <UsageHistory items={history} />
    </div>
  );
}
