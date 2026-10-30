import type { Metadata } from "next";
import { CreateKeyForm, KeyList, listApiKeys } from "@/features/api-keys";
import { requireAdmin } from "@/shared/auth/session";
import { PageHeader, Section } from "@/shared/ui/primitives";

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
            Seus sistemas chamam o produto com o header <code className="rounded-[3px] border border-line bg-paper px-1 font-mono text-meta">X-Api-Key</code>. Cada chamada conta na quota do mês.
          </>
        }
      />
      <Section title="Nova chave" className="max-w-2xl">
        <CreateKeyForm />
      </Section>
      <KeyList keys={keys} />
    </div>
  );
}
