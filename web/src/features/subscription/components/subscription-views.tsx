import { daysUntil, formatDate, formatDateRange, intervalLabel } from "@/shared/format";
import { ActionButton } from "@/shared/ui/action-button";
import { ButtonLink } from "@/shared/ui/button";
import { Ledger } from "@/shared/ui/ledger";
import { Alert, Badge, Card } from "@/shared/ui/primitives";
import {
  cancelPendingChangeAction,
  cancelSubscriptionAction,
  reactivateSubscriptionAction,
  resumeSubscriptionAction,
} from "../actions";
import { canChangePlan, canReactivate, statusLabel, statusTone, type Subscription, type SubscriptionStatus } from "../model";

export function StatusBadge({ status }: { status: SubscriptionStatus }) {
  return <Badge tone={statusTone[status]}>{statusLabel[status]}</Badge>;
}

/** Aviso permanente no topo do portal quando a assinatura exige ação (FR-013). */
export function SubscriptionBanner({ subscription, isAdmin }: { subscription: Subscription | null; isAdmin: boolean }) {
  if (!subscription) {
    return null;
  }

  const action = (href: string, label: string) =>
    isAdmin ? (
      <ButtonLink href={href} size="sm" variant="secondary" className="shrink-0">
        {label}
      </ButtonLink>
    ) : undefined;

  switch (subscription.status) {
    case "PastDue":
      return (
        <Alert tone="warning" title="Não conseguimos cobrar a última fatura" action={action("/billing/invoices", "Ver faturas")}>
          Vamos tentar de novo automaticamente. Para evitar a suspensão, atualize o meio de pagamento ou pague agora.
        </Alert>
      );
    case "Suspended":
      return (
        <Alert tone="danger" title="Acesso ao produto suspenso por falta de pagamento" action={action("/billing/invoices", "Pagar fatura")}>
          Seus dados estão guardados. O acesso volta assim que a fatura em aberto for paga.
        </Alert>
      );
    case "Expired":
      return (
        <Alert tone="warning" title="O período de teste terminou" action={action("/billing", "Reativar assinatura")}>
          Cadastre um meio de pagamento e reative a assinatura para voltar a usar o produto. Seus dados estão guardados.
        </Alert>
      );
    case "Canceled":
      return (
        <Alert tone="warning" title="Assinatura cancelada" action={action("/billing", "Reativar assinatura")}>
          O produto está bloqueado, mas seus dados continuam guardados e voltam com a reativação.
        </Alert>
      );
    default:
      if (subscription.cancelAtPeriodEnd) {
        return (
          <Alert tone="warning" title={"Sua assinatura termina em " + formatDate(subscription.currentPeriodEnd)} action={action("/billing", "Manter assinatura")}>
            O cancelamento foi agendado. Até lá, tudo continua funcionando.
          </Alert>
        );
      }

      if (subscription.status === "Trialing" && subscription.trialEndsAt && daysUntil(subscription.trialEndsAt) <= 3) {
        return (
          <Alert tone="brand" title={"Seu teste termina em " + formatDate(subscription.trialEndsAt)} action={action("/billing/payment-method", "Cadastrar cartão")}>
            Cadastre um meio de pagamento para continuar sem interrupção.
          </Alert>
        );
      }

      return null;
  }
}

/** Frase de status em linguagem simples (o que está acontecendo e até quando). */
function statusSentence(subscription: Subscription): string {
  const end = formatDate(subscription.currentPeriodEnd);
  switch (subscription.status) {
    case "Trialing": {
      const days = daysUntil(subscription.trialEndsAt ?? subscription.currentPeriodEnd);
      return subscription.cancelAtPeriodEnd
        ? "Teste grátis até " + end + ". O cancelamento já está agendado."
        : "Teste grátis até " + formatDate(subscription.trialEndsAt ?? subscription.currentPeriodEnd) + (days === 1 ? ", falta 1 dia." : ", faltam " + days + " dias.");
    }
    case "Active":
      return subscription.cancelAtPeriodEnd ? "Ativa até " + end + ", sem renovação." : "Ativa. Renova em " + end + ".";
    case "PastDue":
      return "A última cobrança não foi aprovada. Novas tentativas estão agendadas.";
    case "Suspended":
      return "Suspensa por falta de pagamento. O produto volta assim que a fatura for paga.";
    case "Expired":
      return "O teste terminou sem meio de pagamento. Reative para voltar a usar.";
    default:
      return "Cancelada. Os dados continuam guardados e voltam com a reativação.";
  }
}

/** Resumo da assinatura: o plano como título, a situação numa frase e as datas num extrato. */
export function SubscriptionCard({ subscription, isAdmin, pendingPlanName }: { subscription: Subscription; isAdmin: boolean; pendingPlanName?: string }) {
  const { status } = subscription;
  const rows = [
    {
      label: status === "Trialing" ? "Período de teste" : "Período atual",
      value: formatDateRange(subscription.currentPeriodStart, subscription.currentPeriodEnd),
    },
    {
      label: "Próxima cobrança",
      value: subscription.cancelAtPeriodEnd || canReactivate(status) || status === "Suspended" ? "nenhuma" : formatDate(subscription.currentPeriodEnd),
    },
  ];
  if (subscription.pendingChange) {
    rows.push({
      label: "Muda para " + (pendingPlanName ?? subscription.pendingChange.planCode) + " " + intervalLabel[subscription.pendingChange.interval],
      value: "em " + formatDate(subscription.pendingChange.effectiveAt),
    });
  }

  return (
    <Card className="space-y-5">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h2 className="text-title font-semibold">
            {subscription.plan.name} <span className="font-normal text-ink-soft">{intervalLabel[subscription.interval]}</span>
          </h2>
          <p className="mt-1 text-ink-soft">{statusSentence(subscription)}</p>
        </div>
        <StatusBadge status={status} />
      </div>

      <Ledger rows={rows} />

      {isAdmin && (
        <div className="flex flex-wrap items-start gap-3 pt-1">
          {canChangePlan(status) && <ButtonLink href="/billing/change-plan">Mudar de plano</ButtonLink>}
          {canReactivate(status) && (
            <ActionButton
              variant="primary"
              action={reactivateSubscriptionAction}
              confirm={{
                title: "Reativar a assinatura?",
                description: "Um novo período começa agora e a fatura é cobrada no meio de pagamento cadastrado. Não há novo período de teste.",
                confirmLabel: "Reativar",
              }}
            >
              Reativar assinatura
            </ActionButton>
          )}
          {subscription.pendingChange && (
            <ActionButton action={cancelPendingChangeAction}>Desfazer mudança agendada</ActionButton>
          )}
          {canChangePlan(status) && subscription.cancelAtPeriodEnd && (
            <ActionButton variant="primary" action={resumeSubscriptionAction}>
              Manter assinatura
            </ActionButton>
          )}
          {canChangePlan(status) && !subscription.cancelAtPeriodEnd && (
            <ActionButton
              variant="ghost"
              destructive
              className="text-fault-700 hover:bg-fault-50"
              action={cancelSubscriptionAction}
              confirm={{
                title: "Cancelar a assinatura?",
                description: (
                  <>
                    O acesso continua até <strong>{formatDate(subscription.currentPeriodEnd)}</strong> e você pode desfazer o
                    cancelamento até lá. Não há reembolso do período atual; seus dados ficam guardados.
                  </>
                ),
                confirmLabel: "Cancelar no fim do período",
              }}
            >
              Cancelar assinatura
            </ActionButton>
          )}
        </div>
      )}

      {!isAdmin && (
        <p className="text-meta text-ink-faint">
          Só administradores da conta alteram a assinatura.
        </p>
      )}
    </Card>
  );
}
