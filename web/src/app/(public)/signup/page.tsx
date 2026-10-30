import type { Metadata } from "next";
import { listPlans } from "@/features/pricing";
import { SignupForm } from "@/features/signup";
import { Alert } from "@/shared/ui/primitives";
import { AuthCard } from "../auth-card";

export const metadata: Metadata = { title: "Criar conta" };
export const dynamic = "force-dynamic";

export default async function SignupPage({ searchParams }: { searchParams: Promise<{ plan?: string; interval?: string }> }) {
  const [{ plan, interval }, plans] = await Promise.all([searchParams, listPlans()]);
  return (
    <AuthCard title="Crie sua conta" description="14 dias de teste grátis, sem cartão de crédito.">
      {plans.length > 0 ? (
        <SignupForm plans={plans} plan={plan} interval={interval} />
      ) : (
        <Alert tone="warning">Não foi possível carregar os planos agora. Tente de novo em instantes.</Alert>
      )}
    </AuthCard>
  );
}
