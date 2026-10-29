using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Billing.Domain.Plans;
using Subscribely.Modules.Billing.Domain.Subscriptions;
using Subscribely.SharedKernel;

namespace Subscribely.UnitTests.Billing;

public sealed class SubscriptionTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly PlanVersion Starter = Version("Starter", 4_900, 49_000, seats: 3);
    private static readonly PlanVersion Pro = Version("Pro", 14_900, 149_000, seats: 10);
    private static readonly PlanVersion Twin = Version("Twin", 4_900, 49_000, seats: 3);

    private static PlanVersion Version(string name, long monthly, long annual, int seats) =>
        PlanVersion.Create(Guid.NewGuid(), 1, new PlanTerms(name, monthly, annual, 1_000, seats), Start);

    private static Subscription Trial(PlanVersion? plan = null, BillingInterval interval = BillingInterval.Monthly) =>
        Subscription.StartTrial(Guid.NewGuid(), (plan ?? Starter).Id, interval, Start);

    private static Subscription Active(PlanVersion? plan = null, BillingInterval interval = BillingInterval.Monthly)
    {
        var s = Trial(plan, interval);
        s.EndTrial(hasPaymentMethod: true, s.TrialEndsAt!.Value).IsSuccess.ShouldBeTrue();
        s.ClearEvents();
        return s;
    }

    private static Subscription InStatus(SubscriptionStatus status)
    {
        var s = Active();
        var now = s.CurrentPeriodStart;
        switch (status)
        {
            case SubscriptionStatus.Trialing:
                return Trial();
            case SubscriptionStatus.PastDue:
                s.MarkPastDue(now);
                break;
            case SubscriptionStatus.Suspended:
                s.Suspend(now);
                break;
            case SubscriptionStatus.Expired:
                s = Trial();
                s.EndTrial(false, s.TrialEndsAt!.Value);
                break;
            case SubscriptionStatus.Canceled:
                s.Cancel(now);
                s.ClosePeriod(s.CurrentPeriodEnd, null);
                break;
        }

        s.Status.ShouldBe(status);
        s.ClearEvents();
        return s;
    }

    [Fact]
    public void StartTrial_Is14DaysAndPublishesStarted()
    {
        var s = Trial();

        s.Status.ShouldBe(SubscriptionStatus.Trialing);
        s.TrialUsed.ShouldBeTrue();
        s.TrialEndsAt.ShouldBe(Start.AddDays(14));
        s.CurrentPeriod.ShouldBe(new BillingPeriod(Start, Start.AddDays(14)));
        s.ProductAccessAllowed.ShouldBeTrue();
        s.Events.ShouldHaveSingleItem().ShouldBeOfType<SubscriptionStarted>().TrialEndsAt.ShouldBe(Start.AddDays(14));
    }

    [Fact]
    public void NotifyTrialEnding_OnlyFromD3_AndOnlyOnce()
    {
        var s = Trial();
        var endsAt = s.TrialEndsAt!.Value;

        s.NotifyTrialEnding(endsAt.AddDays(-4)).ShouldBeFalse();
        s.NotifyTrialEnding(endsAt.AddDays(-3)).ShouldBeTrue();
        s.NotifyTrialEnding(endsAt.AddDays(-2)).ShouldBeFalse();

        s.Events.OfType<TrialEnding>().ShouldHaveSingleItem();
    }

    [Fact]
    public void NotifyTrialEnding_AfterTrialEnd_IsIgnored() =>
        Trial().NotifyTrialEnding(Start.AddDays(15)).ShouldBeFalse();

    [Fact]
    public void EndTrial_WithPaymentMethod_ActivatesWithPeriodFromTrialEnd()
    {
        var s = Trial();
        var endsAt = s.TrialEndsAt!.Value;

        s.EndTrial(true, endsAt.AddMinutes(30)).Value.ShouldBe(SubscriptionStatus.Active);

        s.CurrentPeriod.ShouldBe(new BillingPeriod(endsAt, endsAt.AddMonths(1)));
        s.Events.OfType<SubscriptionChanged>().ShouldHaveSingleItem().Change.ShouldBe("trial-converted");
    }

    [Fact]
    public void EndTrial_WithoutPaymentMethod_Expires_AndBlocksProduct()
    {
        var s = Trial();

        s.EndTrial(false, s.TrialEndsAt!.Value).Value.ShouldBe(SubscriptionStatus.Expired);

        s.ProductAccessAllowed.ShouldBeFalse();
        s.Events.OfType<SubscriptionExpired>().ShouldHaveSingleItem();
    }

    [Fact]
    public void EndTrial_WithCancelScheduled_Cancels()
    {
        var s = Trial();
        s.Cancel(Start).IsSuccess.ShouldBeTrue();

        s.EndTrial(true, s.TrialEndsAt!.Value).Value.ShouldBe(SubscriptionStatus.Canceled);
        s.Events.OfType<SubscriptionCanceled>().ShouldHaveSingleItem().Reason.ShouldBe("canceled-at-trial-end");
    }

    [Fact]
    public void EndTrial_BeforeTrialEnd_OrTwice_Fails()
    {
        var s = Trial();
        s.EndTrial(true, Start.AddDays(1)).Error!.Code.ShouldBe("subscription-not-changeable");

        s.EndTrial(true, s.TrialEndsAt!.Value).IsSuccess.ShouldBeTrue();
        s.EndTrial(true, s.TrialEndsAt!.Value).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void ChangePlan_DuringTrial_IsImmediateAndFree_AndTrialContinues()
    {
        var s = Trial();

        var change = s.ChangePlan(Starter, Pro, BillingInterval.Annual, Start.AddDays(2)).Value;

        change.Immediate.ShouldBeTrue();
        change.Billable.ShouldBeFalse();
        s.PlanVersionId.ShouldBe(Pro.Id);
        s.Interval.ShouldBe(BillingInterval.Annual);
        s.Status.ShouldBe(SubscriptionStatus.Trialing);
        s.TrialEndsAt.ShouldBe(Start.AddDays(14));
    }

    [Fact]
    public void ChangePlan_Upgrade_SameInterval_IsImmediate_Billable_AndKeepsThePeriod()
    {
        var s = Active();
        var period = s.CurrentPeriod;

        var change = s.ChangePlan(Starter, Pro, BillingInterval.Monthly, period.Start.AddDays(10)).Value;

        change.ShouldBe(new PlanChange(PlanChangeKind.Upgrade, true, true, Starter.Id, BillingInterval.Monthly, period));
        s.PlanVersionId.ShouldBe(Pro.Id);
        s.CurrentPeriod.ShouldBe(period);
    }

    [Fact]
    public void ChangePlan_SamePlanOtherInterval_MonthlyToAnnual_StartsNewAnnualPeriod()
    {
        var plan = Version("Solo", 4_900, 49_000, 3);
        var s = Active(plan);
        var now = s.CurrentPeriodStart.AddDays(5);

        var change = s.ChangePlan(plan, plan, BillingInterval.Annual, now).Value;

        change.Kind.ShouldBe(PlanChangeKind.Upgrade);
        change.Billable.ShouldBeTrue();
        s.CurrentPeriod.ShouldBe(new BillingPeriod(now, now.AddYears(1)));
    }

    [Fact]
    public void ChangePlan_Downgrade_IsScheduled_AndAppliedAtPeriodEnd()
    {
        var s = Active(Pro);
        var period = s.CurrentPeriod;

        var change = s.ChangePlan(Pro, Starter, BillingInterval.Monthly, period.Start.AddDays(3)).Value;

        change.Immediate.ShouldBeFalse();
        s.PlanVersionId.ShouldBe(Pro.Id);
        s.PendingPlanVersionId.ShouldBe(Starter.Id);

        s.ClosePeriod(period.End, latestPlanVersionId: null).ShouldBe(PeriodClosure.Renewed);
        s.PlanVersionId.ShouldBe(Starter.Id);
        s.HasPendingChange.ShouldBeFalse();
        s.CurrentPeriod.ShouldBe(new BillingPeriod(period.End, period.End.AddMonths(1)));
        s.Events.OfType<SubscriptionChanged>().Last().Change.ShouldBe("downgrade-applied");
    }

    [Fact]
    public void ChangePlan_AnnualToMonthly_IsDowngrade_AndReanchorsAtPeriodEnd()
    {
        var s = Active(Starter, BillingInterval.Annual);
        var end = s.CurrentPeriodEnd;

        s.ChangePlan(Starter, Twin, BillingInterval.Monthly, s.CurrentPeriodStart.AddDays(1)).Value.Immediate.ShouldBeFalse();
        s.ClosePeriod(end, null);

        s.Interval.ShouldBe(BillingInterval.Monthly);
        s.CurrentPeriod.ShouldBe(new BillingPeriod(end, end.AddMonths(1)));
    }

    [Fact]
    public void ChangePlan_SameDailyPrice_IsImmediate_WithoutInvoice()
    {
        var s = Active();

        var change = s.ChangePlan(Starter, Twin, BillingInterval.Monthly, s.CurrentPeriodStart.AddDays(1)).Value;

        change.Kind.ShouldBe(PlanChangeKind.Lateral);
        change.Billable.ShouldBeFalse();
        s.PlanVersionId.ShouldBe(Twin.Id);
    }

    [Fact]
    public void ChangePlan_SamePlanAndInterval_Is422NoChange() =>
        Active().ChangePlan(Starter, Starter, BillingInterval.Monthly, Start).Error!.ShouldSatisfyAllConditions(
            e => e.Code.ShouldBe("no-change"),
            e => e.Type.ShouldBe(ErrorType.Unprocessable));

    [Fact]
    public void ChangePlan_UpgradeWhilePastDue_Is409()
    {
        var s = InStatus(SubscriptionStatus.PastDue);

        s.ChangePlan(Starter, Pro, BillingInterval.Monthly, Start).Error!.Code.ShouldBe("subscription-past-due");
    }

    [Theory]
    [InlineData("Suspended")]
    [InlineData("Expired")]
    [InlineData("Canceled")]
    public void ChangePlan_InBlockedStatus_Is409NotChangeable(string status) =>
        InStatus(Enum.Parse<SubscriptionStatus>(status)).ChangePlan(Starter, Pro, BillingInterval.Monthly, Start).Error!.Code.ShouldBe("subscription-not-changeable");

    [Fact]
    public void Upgrade_DiscardsAScheduledDowngrade()
    {
        var s = Active(Pro);
        s.ChangePlan(Pro, Starter, BillingInterval.Monthly, s.CurrentPeriodStart);

        s.ChangePlan(Pro, Pro, BillingInterval.Annual, s.CurrentPeriodStart.AddDays(1)).IsSuccess.ShouldBeTrue();

        s.HasPendingChange.ShouldBeFalse();
    }

    [Fact]
    public void CancelPendingChange_RemovesTheDowngrade_AndIsIdempotent()
    {
        var s = Active(Pro);
        s.ChangePlan(Pro, Starter, BillingInterval.Monthly, s.CurrentPeriodStart);

        s.CancelPendingChange(Start).IsSuccess.ShouldBeTrue();
        s.CancelPendingChange(Start).IsSuccess.ShouldBeTrue();

        s.HasPendingChange.ShouldBeFalse();
        s.Events.OfType<SubscriptionChanged>().Count(e => e.Change == "pending-change-canceled").ShouldBe(1);
    }

    [Fact]
    public void Cancel_SchedulesForPeriodEnd_ClearsPending_AndIsIdempotent()
    {
        var s = Active(Pro);
        s.ChangePlan(Pro, Starter, BillingInterval.Monthly, s.CurrentPeriodStart);

        s.Cancel(Start).IsSuccess.ShouldBeTrue();
        s.Cancel(Start).IsSuccess.ShouldBeTrue();

        s.CancelAtPeriodEnd.ShouldBeTrue();
        s.HasPendingChange.ShouldBeFalse();
        s.Status.ShouldBe(SubscriptionStatus.Active);
        s.Events.OfType<SubscriptionChanged>().Count(e => e.Change == "cancel-scheduled").ShouldBe(1);
    }

    [Fact]
    public void Cancel_AlreadyCanceled_IsIdempotent() =>
        InStatus(SubscriptionStatus.Canceled).Cancel(Start).IsSuccess.ShouldBeTrue();

    [Theory]
    [InlineData("Suspended")]
    [InlineData("Expired")]
    public void Cancel_SuspendedOrExpired_Is409(string status) =>
        InStatus(Enum.Parse<SubscriptionStatus>(status)).Cancel(Start).Error!.Code.ShouldBe("subscription-not-changeable");

    [Fact]
    public void Resume_UndoesTheCancellation_BeforePeriodEnd()
    {
        var s = Active();
        s.Cancel(Start);

        s.Resume(Start).IsSuccess.ShouldBeTrue();
        s.Resume(Start).IsSuccess.ShouldBeTrue();

        s.CancelAtPeriodEnd.ShouldBeFalse();
        s.ClosePeriod(s.CurrentPeriodEnd, null).ShouldBe(PeriodClosure.Renewed);
    }

    [Fact]
    public void Resume_AfterCanceled_Is409() =>
        InStatus(SubscriptionStatus.Canceled).Resume(Start).Error!.Code.ShouldBe("subscription-not-changeable");

    [Fact]
    public void ClosePeriod_WithCancel_Cancels_EvenWithPendingChange()
    {
        var s = Active();
        s.Cancel(Start);

        s.ClosePeriod(s.CurrentPeriodEnd, Pro.Id).ShouldBe(PeriodClosure.Canceled);

        s.Status.ShouldBe(SubscriptionStatus.Canceled);
        s.ProductAccessAllowed.ShouldBeFalse();
        s.Events.OfType<SubscriptionCanceled>().ShouldHaveSingleItem().Reason.ShouldBe("canceled-at-period-end");
    }

    [Fact]
    public void ClosePeriod_MigratesToTheLatestPlanVersion()
    {
        var s = Active();
        var newer = Guid.NewGuid();

        s.ClosePeriod(s.CurrentPeriodEnd, newer).ShouldBe(PeriodClosure.Renewed);

        s.PlanVersionId.ShouldBe(newer);
        s.Events.OfType<SubscriptionChanged>().Last().Change.ShouldBe("plan-version-migrated");
    }

    [Fact]
    public void ClosePeriod_BeforeEnd_OrTwiceForSamePeriod_IsNoOp()
    {
        var s = Active();
        var end = s.CurrentPeriodEnd;

        s.ClosePeriod(end.AddSeconds(-1), null).ShouldBeNull();
        s.ClosePeriod(end, null).ShouldBe(PeriodClosure.Renewed);
        s.ClosePeriod(end, null).ShouldBeNull();
    }

    [Fact]
    public void ClosePeriod_KeepsTheAnchorDay()
    {
        var s = Trial();
        s.EndTrial(true, s.TrialEndsAt!.Value);
        var anchor = s.BillingAnchor;

        for (var i = 0; i < 3; i++)
        {
            s.ClosePeriod(s.CurrentPeriodEnd, null);
        }

        s.CurrentPeriodEnd.ShouldBe(anchor.AddMonths(4));
    }

    [Theory]
    [InlineData("Trialing")]
    [InlineData("Suspended")]
    [InlineData("Expired")]
    [InlineData("Canceled")]
    public void ClosePeriod_OutsideActiveOrPastDue_IsNoOp(string status) =>
        InStatus(Enum.Parse<SubscriptionStatus>(status)).ClosePeriod(DateTimeOffset.MaxValue, null).ShouldBeNull();

    [Fact]
    public void PaymentFailure_ThenExhausted_ThenPaid_GoesPastDueSuspendedActive()
    {
        var s = Active();

        s.MarkPastDue(Start).IsSuccess.ShouldBeTrue();
        s.MarkPastDue(Start).IsSuccess.ShouldBeTrue();
        s.Status.ShouldBe(SubscriptionStatus.PastDue);
        s.ProductAccessAllowed.ShouldBeTrue();

        s.Suspend(Start).IsSuccess.ShouldBeTrue();
        s.Suspend(Start).IsSuccess.ShouldBeTrue();
        s.Status.ShouldBe(SubscriptionStatus.Suspended);
        s.SuspendedAt.ShouldBe(Start);
        s.ProductAccessAllowed.ShouldBeFalse();

        s.SettleDebt(Start).ShouldBeTrue();
        s.SettleDebt(Start).ShouldBeFalse();
        s.Status.ShouldBe(SubscriptionStatus.Active);
        s.SuspendedAt.ShouldBeNull();
        s.Events.OfType<SubscriptionSuspended>().ShouldHaveSingleItem();
        s.Events.OfType<SubscriptionReactivated>().ShouldHaveSingleItem();
    }

    [Fact]
    public void SettleDebt_FromPastDue_PublishesChanged()
    {
        var s = InStatus(SubscriptionStatus.PastDue);

        s.SettleDebt(Start).ShouldBeTrue();

        s.Events.OfType<SubscriptionChanged>().ShouldHaveSingleItem().Change.ShouldBe("debt-settled");
    }

    [Theory]
    [InlineData("Trialing")]
    [InlineData("Expired")]
    [InlineData("Canceled")]
    public void MarkPastDue_OrSuspend_OutsideActive_Is409(string status)
    {
        var s = InStatus(Enum.Parse<SubscriptionStatus>(status));

        s.MarkPastDue(Start).Error!.Code.ShouldBe("subscription-not-changeable");
        s.Suspend(Start).Error!.Code.ShouldBe("subscription-not-changeable");
    }

    [Fact]
    public void ExpireSuspension_After30Days_Cancels()
    {
        var s = InStatus(SubscriptionStatus.Suspended);
        var since = s.SuspendedAt!.Value;

        s.ExpireSuspension(since.AddDays(29)).ShouldBeFalse();
        s.ExpireSuspension(since.AddDays(30)).ShouldBeTrue();
        s.ExpireSuspension(since.AddDays(31)).ShouldBeFalse();

        s.Status.ShouldBe(SubscriptionStatus.Canceled);
        s.Events.OfType<SubscriptionCanceled>().ShouldHaveSingleItem().Reason.ShouldBe("suspension-expired");
    }

    [Theory]
    [InlineData("Expired")]
    [InlineData("Canceled")]
    public void Reactivate_StartsAFullPeriodNow_WithoutNewTrial(string status)
    {
        var s = InStatus(Enum.Parse<SubscriptionStatus>(status));
        var now = Start.AddDays(90);

        s.Reactivate(true, Pro.Id, BillingInterval.Monthly, now).IsSuccess.ShouldBeTrue();

        s.Status.ShouldBe(SubscriptionStatus.Active);
        s.PlanVersionId.ShouldBe(Pro.Id);
        s.CurrentPeriod.ShouldBe(new BillingPeriod(now, now.AddMonths(1)));
        s.TrialUsed.ShouldBeTrue();
        s.Events.OfType<SubscriptionReactivated>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Reactivate_WithoutPaymentMethod_Is402() =>
        InStatus(SubscriptionStatus.Expired).Reactivate(false, Starter.Id, BillingInterval.Monthly, Start).Error!
            .Type.ShouldBe(ErrorType.PaymentRequired);

    [Theory]
    [InlineData("Trialing")]
    [InlineData("Active")]
    [InlineData("PastDue")]
    [InlineData("Suspended")]
    public void Reactivate_FromOtherStatus_Is409(string status) =>
        InStatus(Enum.Parse<SubscriptionStatus>(status)).Reactivate(true, Starter.Id, BillingInterval.Monthly, Start).Error!.Code.ShouldBe("subscription-not-changeable");
}
