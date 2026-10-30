import type { Metadata } from "next";
import { CreateKeyForm, KeyList, listApiKeys } from "@/features/api-keys";
import { requireAdmin } from "@/shared/auth/session";
import { Card, CardTitle, PageHeader } from "@/shared/ui/primitives";

export const metadata: Metadata = { title: "Chaves de API" };

export default async function ApiKeysPage() {
  await requireAdmin("/api-keys");
  const keys = await listApiKeys();
  return (
    <div className="space-y-8">
      <PageHeader
        title="Chaves de API"
        description={
          <>
            Seus sistemas chamam o produto com o header <code className="rounded bg-slate-100 px-1 font-mono text-xs">X-Api-Key</code>. Cada chamada conta na quota do mês.
          </>
        }
      />
      <Card className="space-y-4">
        <CardTitle>Nova chave</CardTitle>
        <CreateKeyForm />
      </Card>
      <KeyList keys={keys} />
    </div>
  );
}
