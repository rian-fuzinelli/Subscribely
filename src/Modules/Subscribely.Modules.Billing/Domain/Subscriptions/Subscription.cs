using Subscribely.Infrastructure.Auditing;
using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Billing.Domain.Plans;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Billing.Domain.Subscriptions;

internal enum SubscriptionStatus
{
    Trialing,
    Active,
    PastDue,
    Suspended,
    Expired,
    Canceled,
}

/// <summary>Resultado de uma mudança de plano: o suficiente para a fatura de proração (spec 04).</summary>
internal sealed record PlanChange(
    PlanChangeKind Kind,
    bool Immediate,
    bool Billable,
    Guid PreviousPlanVersionId,
    BillingInterval PreviousInterval,
    BillingPeriod PreviousPeriod);

internal enum PeriodClosure
{
    Canceled,
    Renewed,
}

/// <summary>
/// Assinatura do tenant (uma por tenant). Toda transição de status passa por um método daqui,
/// que devolve <see cref="Result"/> e publica o evento correspondente no outbox.
/// </summary>
[Auditable]
internal sealed class Subscription : Entity
{
    public static readonly TimeSpan TrialLength = TimeSpan.FromDays(14);
    public static readonly TimeSpan TrialEndingNotice = TimeSpan.FromDays(3);
    public static readonly TimeSpan SuspensionLimit = TimeSpan.FromDays(30);

    private Subscription()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid PlanVersionId { get; private set; }

    public BillingInterval Interval { get; private set; }

    public SubscriptionStatus Status { get; private set; }

    public DateTimeOffset? TrialEndsAt { get; private set; }

    public bool TrialUsed { get; private set; }

    public DateTimeOffset? TrialEndingNotifiedAt { get; private set; }

    /// <summary>Âncora das fronteiras de período (preserva o dia original em meses curtos).</summary>
    public DateTimeOffset BillingAnchor { get; private set; }

    public DateTimeOffset CurrentPeriodStart { get; private set; }

    public DateTimeOffset CurrentPeriodEnd { get; private set; }

    public bool CancelAtPeriodEnd { get; private set; }

    public Guid? PendingPlanVersionId { get; private set; }

    public BillingInterval? PendingInterval { get; private set; }

    public DateTimeOffset? SuspendedAt { get; private set; }

    /// <summary>Token de concorrência otimista (coluna de sistema <c>xmin</c>).</summary>
    public uint Version { get; private set; }

    public BillingPeriod CurrentPeriod => new(CurrentPeriodStart, CurrentPeriodEnd);

    public bool ProductAccessAllowed => Status is SubscriptionStatus.Trialing or SubscriptionStatus.Active or SubscriptionStatus.PastDue;

    public bool HasPendingChange => PendingPlanVersionId is not null;

    public static Subscription StartTrial(Guid tenantId, Guid planVersionId, BillingInterval interval, DateTimeOffset now)
    {
        var trialEndsAt = now + TrialLength;
        var subscription = new Subscription
        {
            TenantId = tenantId,
            PlanVersionId = planVersionId,
            Interval = interval,
            Status = SubscriptionStatus.Trialing,
            TrialEndsAt = trialEndsAt,
            TrialUsed = true,
            BillingAnchor = trialEndsAt,
            CurrentPeriodStart = now,
            CurrentPeriodEnd = trialEndsAt,
        };
        subscription.Raise(new SubscriptionStarted(tenantId, subscription.Id, trialEndsAt, now));
        return subscription;
    }

    /// <returns><c>true</c> na primeira vez que o aviso de D-3 é devido; depois, sempre <c>false</c>.</returns>
    public bool NotifyTrialEnding(DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.Trialing || TrialEndingNotifiedAt is not null || TrialEndsAt is not { } endsAt
            || now < endsAt - TrialEndingNotice || now >= endsAt)
        {
            return false;
        }

        TrialEndingNotifiedAt = now;
        Raise(new TrialEnding(TenantId, Id, endsAt, now));
        return true;
    }

    /// <summary>Fim do trial: converte (com meio de pagamento), expira (sem) ou cancela (se agendado).</summary>
    public Result<SubscriptionStatus> EndTrial(bool hasPaymentMethod, DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.Trialing || TrialEndsAt is not { } endsAt || now < endsAt)
        {
            return NotChangeable();
        }

        if (CancelAtPeriodEnd)
        {
            CloseAsCanceled("canceled-at-trial-end", now);
        }
        else if (hasPaymentMethod)
        {
            Status = SubscriptionStatus.Active;
            StartPeriodAt(endsAt);
            Raise(new SubscriptionChanged(TenantId, Id, "trial-converted", Status.ToString(), now));
        }
        else
        {
            Status = SubscriptionStatus.Expired;
            Raise(new SubscriptionExpired(TenantId, Id, now));
        }

        return Status;
    }

    public Result<PlanChange> ChangePlan(PlanVersion current, PlanVersion target, BillingInterval targetInterval, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(target);

        if (Status is not (SubscriptionStatus.Trialing or SubscriptionStatus.Active or SubscriptionStatus.PastDue))
        {
            return NotChangeable();
        }

        if (current.PlanId == target.PlanId && Interval == targetInterval)
        {
            return Error.Unprocessable("no-change", "The subscription is already on this plan and interval.");
        }

        var kind = PlanChangeClassifier.Classify(current, Interval, target, targetInterval);
        var change = new PlanChange(kind, Immediate: true, Billable: false, PlanVersionId, Interval, CurrentPeriod);

        if (Status == SubscriptionStatus.Trialing)
        {
            Apply(target.Id, targetInterval);
            Raise(new SubscriptionChanged(TenantId, Id, "trial-plan-changed", Status.ToString(), now));
            return change;
        }

        switch (kind)
        {
            case PlanChangeKind.Upgrade when Status == SubscriptionStatus.PastDue:
                return Error.Conflict("subscription-past-due", "Pay the open invoice before upgrading.");

            case PlanChangeKind.Upgrade:
                var intervalChanged = Interval != targetInterval;
                Apply(target.Id, targetInterval);
                if (intervalChanged)
                {
                    StartPeriodAt(now);
                }

                Raise(new SubscriptionChanged(TenantId, Id, "plan-upgraded", Status.ToString(), now));
                return change with { Billable = true };

            case PlanChangeKind.Downgrade:
                PendingPlanVersionId = target.Id;
                PendingInterval = targetInterval;
                Raise(new SubscriptionChanged(TenantId, Id, "downgrade-scheduled", Status.ToString(), now));
                return change with { Immediate = false };

            default:
                Apply(target.Id, targetInterval);
                Raise(new SubscriptionChanged(TenantId, Id, "plan-changed", Status.ToString(), now));
                return change;
        }
    }

    public Result CancelPendingChange(DateTimeOffset now)
    {
        if (!HasPendingChange)
        {
            return Result.Success();
        }

        ClearPending();
        Raise(new SubscriptionChanged(TenantId, Id, "pending-change-canceled", Status.ToString(), now));
        return Result.Success();
    }

    /// <summary>Agenda o cancelamento para o fim do período (idempotente) e descarta a mudança pendente.</summary>
    public Result Cancel(DateTimeOffset now)
    {
        if (Status == SubscriptionStatus.Canceled || CancelAtPeriodEnd)
        {
            return Result.Success();
        }

        if (Status is not (SubscriptionStatus.Trialing or SubscriptionStatus.Active or SubscriptionStatus.PastDue))
        {
            return NotChangeable();
        }

        CancelAtPeriodEnd = true;
        ClearPending();
        Raise(new SubscriptionChanged(TenantId, Id, "cancel-scheduled", Status.ToString(), now));
        return Result.Success();
    }

    public Result Resume(DateTimeOffset now)
    {
        if (Status is not (SubscriptionStatus.Trialing or SubscriptionStatus.Active or SubscriptionStatus.PastDue))
        {
            return NotChangeable();
        }

        if (!CancelAtPeriodEnd)
        {
            return Result.Success();
        }

        CancelAtPeriodEnd = false;
        Raise(new SubscriptionChanged(TenantId, Id, "cancel-reverted", Status.ToString(), now));
        return Result.Success();
    }

    /// <summary>
    /// Fim do período, nesta ordem: cancelamento agendado → <c>Canceled</c>; senão mudança pendente; senão
    /// migração para <paramref name="latestPlanVersionId"/> (versão vigente do plano, se houver uma mais nova).
    /// </summary>
    /// <returns><c>null</c> quando não há nada a fazer (período ainda aberto ou status sem renovação).</returns>
    public PeriodClosure? ClosePeriod(DateTimeOffset now, Guid? latestPlanVersionId)
    {
        if (Status is not (SubscriptionStatus.Active or SubscriptionStatus.PastDue) || now < CurrentPeriodEnd)
        {
            return null;
        }

        if (CancelAtPeriodEnd)
        {
            CloseAsCanceled("canceled-at-period-end", now);
            return PeriodClosure.Canceled;
        }

        var end = CurrentPeriodEnd;
        string change;
        if (PendingPlanVersionId is { } pendingVersion && PendingInterval is { } pendingInterval)
        {
            var intervalChanged = pendingInterval != Interval;
            Apply(pendingVersion, pendingInterval);
            if (intervalChanged)
            {
                BillingAnchor = end;
            }

            change = "downgrade-applied";
        }
        else if (latestPlanVersionId is { } latest && latest != PlanVersionId)
        {
            PlanVersionId = latest;
            change = "plan-version-migrated";
        }
        else
        {
            change = "renewed";
        }

        var next = BillingPeriod.After(BillingAnchor, Interval, end);
        CurrentPeriodStart = next.Start;
        CurrentPeriodEnd = next.End;
        Raise(new SubscriptionChanged(TenantId, Id, change, Status.ToString(), now));
        return PeriodClosure.Renewed;
    }

    /// <summary>Primeira falha de pagamento (spec 05).</summary>
    public Result MarkPastDue(DateTimeOffset now)
    {
        if (Status == SubscriptionStatus.PastDue)
        {
            return Result.Success();
        }

        if (Status != SubscriptionStatus.Active)
        {
            return NotChangeable();
        }

        Status = SubscriptionStatus.PastDue;
        Raise(new SubscriptionChanged(TenantId, Id, "payment-failed", Status.ToString(), now));
        return Result.Success();
    }

    /// <summary>Retentativas esgotadas (spec 05): produto bloqueado até o pagamento.</summary>
    public Result Suspend(DateTimeOffset now)
    {
        if (Status == SubscriptionStatus.Suspended)
        {
            return Result.Success();
        }

        if (Status is not (SubscriptionStatus.Active or SubscriptionStatus.PastDue))
        {
            return NotChangeable();
        }

        Status = SubscriptionStatus.Suspended;
        SuspendedAt = now;
        Raise(new SubscriptionSuspended(TenantId, Id, now));
        return Result.Success();
    }

    /// <summary>Fatura paga: <c>PastDue</c>/<c>Suspended</c> → <c>Active</c>. Nos demais status, nada muda.</summary>
    public bool SettleDebt(DateTimeOffset now)
    {
        if (Status is not (SubscriptionStatus.PastDue or SubscriptionStatus.Suspended))
        {
            return false;
        }

        var wasSuspended = Status == SubscriptionStatus.Suspended;
        Status = SubscriptionStatus.Active;
        SuspendedAt = null;
        Raise(wasSuspended
            ? new SubscriptionReactivated(TenantId, Id, now)
            : new SubscriptionChanged(TenantId, Id, "debt-settled", Status.ToString(), now));
        return true;
    }

    /// <returns><c>true</c> quando a assinatura estava suspensa há 30 dias e foi cancelada.</returns>
    public bool ExpireSuspension(DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.Suspended || SuspendedAt is not { } since || now < since + SuspensionLimit)
        {
            return false;
        }

        CloseAsCanceled("suspension-expired", now);
        return true;
    }

    /// <summary>De <c>Expired</c>/<c>Canceled</c>: novo período cheio a partir de agora, na versão vigente do plano. Sem novo trial.</summary>
    public Result Reactivate(bool hasPaymentMethod, Guid planVersionId, BillingInterval interval, DateTimeOffset now)
    {
        if (Status is not (SubscriptionStatus.Expired or SubscriptionStatus.Canceled))
        {
            return NotChangeable();
        }

        if (!hasPaymentMethod)
        {
            return Error.PaymentRequired("payment-method-required", "Register a payment method before reactivating.");
        }

        Status = SubscriptionStatus.Active;
        Apply(planVersionId, interval);
        CancelAtPeriodEnd = false;
        SuspendedAt = null;
        StartPeriodAt(now);
        Raise(new SubscriptionReactivated(TenantId, Id, now));
        return Result.Success();
    }

    private static Error NotChangeable() =>
        Error.Conflict("subscription-not-changeable", "This operation is not allowed in the current subscription status.");

    private void Apply(Guid planVersionId, BillingInterval interval)
    {
        PlanVersionId = planVersionId;
        Interval = interval;
        ClearPending();
    }

    private void StartPeriodAt(DateTimeOffset anchor)
    {
        var period = BillingPeriod.Starting(anchor, Interval);
        BillingAnchor = anchor;
        CurrentPeriodStart = period.Start;
        CurrentPeriodEnd = period.End;
    }

    private void ClearPending()
    {
        PendingPlanVersionId = null;
        PendingInterval = null;
    }

    private void CloseAsCanceled(string reason, DateTimeOffset now)
    {
        Status = SubscriptionStatus.Canceled;
        CancelAtPeriodEnd = false;
        SuspendedAt = null;
        ClearPending();
        Raise(new SubscriptionCanceled(TenantId, Id, reason, now));
    }
}
