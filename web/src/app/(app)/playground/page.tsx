import type { Metadata } from "next";
import { Playground, loadPlayground } from "@/features/playground";
import { requireAdmin } from "@/shared/auth/session";
import { formatDateTime } from "@/shared/format";
import { ButtonLink } from "@/shared/ui/button";
import { Alert, PageHeader } from "@/shared/ui/primitives";

export const metadata: Metadata = { title: "Playground" };

export default async function PlaygroundPage() {
  await requireAdmin("/playground");
  const load = await loadPlayground();
  return (
    <div className="space-y-8">
      <PageHeader
        title="Playground"
        description="Use o produto demo (projetos) direto pelo portal e veja a quota sendo consumida em tempo real."
      />
      {load.ok ? (
        <Playground initialProjects={load.projects} initialQuota={load.quota} />
      ) : (
        <Alert
          tone="warning"
          title={load.message}
          action={
            <ButtonLink href={load.code === "quota-exceeded" ? "/billing/change-plan" : "/billing"} size="sm" variant="secondary">
              {load.code === "quota-exceeded" ? "Fazer upgrade" : "Ver assinatura"}
            </ButtonLink>
          }
        >
          {load.code === "quota-exceeded" && load.quota ? "O contador zera em " + formatDateTime(load.quota.resetsAt) + "." : undefined}
        </Alert>
      )}
    </div>
  );
}
