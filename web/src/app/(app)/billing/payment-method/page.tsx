import type { Metadata } from "next";
import { PaymentMethodForm, getPaymentMethod } from "@/features/payment-method";
import { requireAdmin } from "@/shared/auth/session";
import { Card, PageHeader } from "@/shared/ui/primitives";

export const metadata: Metadata = { title: "Meio de pagamento" };

export default async function PaymentMethodPage() {
  await requireAdmin("/billing/payment-method");
  const current = await getPaymentMethod();
  return (
    <div className="space-y-8">
      <PageHeader
        title="Meio de pagamento"
        description="Usado na conversão do teste, nas renovações e nas novas tentativas de cobrança."
      />
      <Card>
        <PaymentMethodForm currentLast4={current?.last4} />
      </Card>
    </div>
  );
}
