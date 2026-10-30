import type { Metadata } from "next";
import { PaymentMethodForm, getPaymentMethod } from "@/features/payment-method";
import { requireAdmin } from "@/shared/auth/session";
import { PageHeader } from "@/shared/ui/primitives";

export const metadata: Metadata = { title: "Meio de pagamento" };

export default async function PaymentMethodPage() {
  await requireAdmin("/billing/payment-method");
  const current = await getPaymentMethod();
  return (
    <div className="space-y-8">
      <PageHeader
        title="Meio de pagamento"
        description="Escolha um cartão de teste do gateway simulado. Cada um responde de um jeito às cobranças."
      />
      <PaymentMethodForm currentLast4={current?.last4} />
    </div>
  );
}
