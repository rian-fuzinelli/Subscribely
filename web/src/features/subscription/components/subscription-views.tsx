import { daysUntil, formatDate, intervalLabel } from "@/shared/format";
import { ActionButton } from "@/shared/ui/action-button";
import { ButtonLink } from "@/shared/ui/button";
import { Alert, Badge, Card, CardTitle, DescriptionList } from "@/shared/ui/primitives";
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

/** Resumo da assinatura com as ações permitidas ao papel e ao status. */
export function SubscriptionCard({ subscription, isAdmin, pendingPlanName }: { subscription: Subscription; isAdmin: boolean; pendingPlanName?: string }) {
  const { status } = subscription;
  const trial = status === "Trialing" && subscription.trialEndsAt;
  return (
    <Card className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <CardTitle>Assinatura</CardTitle>
        <StatusBadge status={status} />
      </div>

      <DescriptionList
        items={[
          { term: "Plano", value: subscription.plan.name + " (" + intervalLabel[subscription.interval] + ")" },
          trial
            ? { term: "Teste termina em", value: formatDate(subscription.trialEndsAt!) + " · " + daysUntil(subscription.trialEndsAt!) + " dia(s)" }
            : { term: "Período atual", value: formatDate(subscription.currentPeriodStart) + " a " + formatDate(subscription.currentPeriodEnd) },
          {
            term: "Próxima renovação",
            value: subscription.cancelAtPeriodEnd || canReactivate(status) ? "—" : formatDate(subscription.currentPeriodEnd),
          },
          {
            term: "Mudança agendada",
            value: subscription.pendingChange
              ? (pendingPlanName ?? subscription.pendingChange.planCode) +
                " (" + intervalLabel[subscription.pendingChange.interval] + ") em " + formatDate(subscription.pendingChange.effectiveAt)
              : "Nenhuma",
          },
        ]}
      />

      {isAdmin && (
        <div className="flex flex-wrap items-start gap-3 border-t border-slate-100 pt-6">
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
              className="text-red-700 hover:bg-red-50"
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
        <p className="border-t border-slate-100 pt-4 text-sm text-slate-500">
          Só administradores da conta alteram a assinatura.
        </p>
      )}
    </Card>
  );
}
