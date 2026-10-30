import type { Metadata } from "next";
import { ProvisioningStatusPanel } from "@/features/signup";
import { AuthCard } from "../../auth-card";

export const metadata: Metadata = { title: "Preparando sua conta" };

export default async function ProvisioningPage({
  params,
  searchParams,
}: {
  params: Promise<{ tenantId: string }>;
  searchParams: Promise<{ email?: string }>;
}) {
  const [{ tenantId }, { email }] = await Promise.all([params, searchParams]);
  return (
    <AuthCard title="Cadastro recebido">
      <ProvisioningStatusPanel tenantId={tenantId} email={email} />
    </AuthCard>
  );
}
