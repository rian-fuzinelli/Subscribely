import { PlanGrid, listPlans } from "@/features/pricing";
import { ButtonLink, TextLink } from "@/shared/ui/button";
import { Ledger } from "@/shared/ui/ledger";
import { Badge, EmptyState } from "@/shared/ui/primitives";

export const dynamic = "force-dynamic";

/** O ciclo real de cobrança (spec 03 e 05): é uma sequência, por isso aparece como régua. */
const cycle = [
  { when: "Dia 1", what: "O teste começa", detail: "14 dias com o plano completo, sem cartão." },
  { when: "Dia 11", what: "Aviso de fim do teste", detail: "Os administradores recebem um e-mail três dias antes." },
  { when: "Dia 15", what: "Primeira fatura", detail: "Cobrada no cartão cadastrado; sem cartão, o acesso pausa e os dados ficam guardados." },
  { when: "Se falhar", what: "Novas tentativas", detail: "Em 1, 3 e 7 dias. Só depois disso o acesso é suspenso." },
];

export default async function HomePage() {
  const plans = await listPlans();
  return (
    <>
      <section className="border-b border-line bg-paper">
        <div className="mx-auto grid max-w-6xl gap-12 px-4 py-14 sm:py-20 lg:grid-cols-[1.1fr_1fr] lg:items-center lg:gap-16">
          <div className="max-w-xl">
            <h1 className="text-[2.5rem] leading-[1.08] font-bold tracking-tight text-ink sm:text-[3.25rem]">
              Cobrança de assinaturas que fecha no centavo.
            </h1>
            <p className="mt-5 max-w-lg text-[1.0625rem] text-ink-soft">
              Upgrade no meio do mês vira crédito pelos dias não usados e cobrança só da diferença. Faturas numeradas, quota da API medida
              em tempo real e avisos antes de qualquer bloqueio.
            </p>
            <div className="mt-8 flex flex-wrap items-center gap-x-6 gap-y-3">
              <ButtonLink href="/signup" size="lg">
                Começar o teste de 14 dias
              </ButtonLink>
              <TextLink href="#planos">Comparar planos</TextLink>
            </div>
          </div>

          <figure className="relative">
            <div className="rounded-surface border border-line bg-paper px-6 pt-5 pb-6 sm:px-8">
              <div className="flex items-start justify-between gap-4 border-b border-line pb-4">
                <div>
                  <p className="font-semibold">Fatura SUB-2026-000214</p>
                  <p className="text-meta text-ink-faint">Upgrade de Starter para Pro, no dia 15 de um mês de 30</p>
                </div>
                <Badge tone="success">Paga</Badge>
              </div>
              <Ledger
                className="mt-3"
                rows={[
                  { label: "Crédito: Starter mensal", detail: "15 dias não usados", value: "-R$ 24,50", credit: true },
                  { label: "Pro mensal, proporcional", detail: "15 dias restantes do período", value: "R$ 74,50" },
                ]}
                total={{ label: "Total cobrado", value: "R$ 50,00" }}
              />
            </div>
            <figcaption className="mt-3 text-meta text-ink-faint">
              Exemplo de fatura gerada pelo Subscribely. O período não muda; você paga só a diferença.
            </figcaption>
          </figure>
        </div>
      </section>

      <section aria-labelledby="ciclo-titulo" className="mx-auto max-w-6xl px-4 py-14">
        <h2 id="ciclo-titulo" className="text-title font-semibold">
          Como a cobrança acontece
        </h2>
        <ol className="mt-8 grid gap-8 md:grid-cols-4 md:gap-6">
          {cycle.map((step) => (
            <li key={step.when} className="border-t-2 border-ink pt-3">
              <p className="numeric text-meta font-semibold text-ledger-700">{step.when}</p>
              <p className="mt-1 font-semibold">{step.what}</p>
              <p className="mt-1 text-meta text-ink-soft">{step.detail}</p>
            </li>
          ))}
        </ol>
      </section>

      <section id="planos" aria-labelledby="planos-titulo" className="scroll-mt-16 border-t border-line bg-paper">
        <div className="mx-auto max-w-6xl px-4 py-14">
          <div className="mb-8 max-w-xl">
            <h2 id="planos-titulo" className="text-title font-semibold">
              Planos
            </h2>
            <p className="mt-1 text-ink-soft">Todos começam com 14 dias de teste. Troque de plano quando quiser, sem perder dados.</p>
          </div>
          {plans.length > 0 ? (
            <PlanGrid plans={plans} hrefTemplate="/signup?plan={plan}&interval={interval}" />
          ) : (
            <EmptyState title="Os planos não carregaram" description="O servidor de cobrança não respondeu. Recarregue a página em instantes." />
          )}
        </div>
      </section>
    </>
  );
}
