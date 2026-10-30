import { Gauge, ReceiptText, ShieldCheck } from "lucide-react";
import { PlanGrid, listPlans } from "@/features/pricing";
import { EmptyState } from "@/shared/ui/primitives";

export const dynamic = "force-dynamic";

const highlights = [
  { icon: ReceiptText, title: "Cobrança sem surpresas", text: "Faturas detalhadas, upgrade com cobrança proporcional e downgrade só no fim do período." },
  { icon: Gauge, title: "Uso sob controle", text: "Acompanhe as requisições do mês e receba alertas em 80% e 100% do limite." },
  { icon: ShieldCheck, title: "Dados isolados", text: "Cada empresa tem um banco de dados exclusivo; ninguém vê os dados de outra conta." },
];

export default async function HomePage() {
  const plans = await listPlans();
  return (
    <>
      <section className="bg-white">
        <div className="mx-auto max-w-6xl px-4 py-16 text-center sm:py-24">
          <h1 className="mx-auto max-w-3xl text-4xl font-bold tracking-tight text-slate-900 sm:text-5xl">
            Assinaturas, faturas e uso da sua API em um só lugar
          </h1>
          <p className="mx-auto mt-4 max-w-2xl text-lg text-slate-600">
            Comece com 14 dias de teste grátis, sem cartão. Troque de plano quando quiser.
          </p>
        </div>
        <ul className="mx-auto grid max-w-6xl gap-6 px-4 pb-16 md:grid-cols-3">
          {highlights.map(({ icon: Icon, title, text }) => (
            <li key={title} className="rounded-xl border border-slate-200 p-6">
              <Icon className="size-6 text-brand-600" aria-hidden />
              <h2 className="mt-3 font-semibold text-slate-900">{title}</h2>
              <p className="mt-1 text-sm text-slate-600">{text}</p>
            </li>
          ))}
        </ul>
      </section>

      <section id="planos" aria-labelledby="planos-titulo" className="mx-auto max-w-6xl scroll-mt-16 px-4 py-16">
        <h2 id="planos-titulo" className="mb-8 text-center text-3xl font-bold tracking-tight text-slate-900">
          Planos
        </h2>
        {plans.length > 0 ? (
          <PlanGrid plans={plans} hrefTemplate="/signup?plan={plan}&interval={interval}" />
        ) : (
          <EmptyState title="Os planos estão indisponíveis no momento" description="Tente de novo em instantes." />
        )}
      </section>
    </>
  );
}
