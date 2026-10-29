using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Billing.Domain.Invoices;
using Subscribely.Modules.Billing.Domain.Plans;
using Subscribely.Modules.Billing.Domain.Subscriptions;

namespace Subscribely.UnitTests.Billing;

public sealed class ProrationCalculatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly BillingPeriod March = new(Start, Start.AddDays(31));

    [Theory]
    [InlineData(0, -4_900, 14_900)] // início: período inteiro
    [InlineData(31 * 24 * 3600, 0, 0)] // fim: nada a prorratear
    [InlineData(31 * 24 * 3600 / 2, -2_450, 7_450)] // meio
    [InlineData(10 * 24 * 3600, -3_319, 10_094)] // 21/31 → 3319,35 e 10093,55
    public void SameInterval_ProratesBothPlansBySeconds(long elapsedSeconds, long credit, long charge)
    {
        var proration = ProrationCalculator.SameInterval(4_900, 14_900, March, Start.AddSeconds(elapsedSeconds));

        proration.ShouldBe(new Proration(credit, charge));
    }

    [Fact]
    public void SameInterval_RoundsHalfUp()
    {
        var period = new BillingPeriod(Start, Start.AddSeconds(4));

        ProrationCalculator.SameInterval(1, 3, period, Start.AddSeconds(2)).ShouldBe(new Proration(-1, 2)); // 0,5 → 1 e 1,5 → 2
    }

    [Fact]
    public void AfterPeriodEnd_IsClampedToZero() =>
        ProrationCalculator.SameInterval(4_900, 14_900, March, March.End.AddDays(2)).ShouldBe(new Proration(0, 0));

    [Fact]
    public void NewPeriod_CreditsTheUnusedMonth_AndChargesTheFullYear()
    {
        var proration = ProrationCalculator.NewPeriod(4_900, 49_000, March, Start.AddSeconds(31 * 24 * 3600 / 2));

        proration.ShouldBe(new Proration(-2_450, 49_000));
        proration.TotalCents.ShouldBe(46_550);
    }

    [Theory]
    [InlineData(1, 999_999_999)]
    [InlineData(4_900, 14_900)]
    [InlineData(long.MaxValue / 4, long.MaxValue / 2)]
    public void Property_CreditNeverExceedsTheOldPrice_AtAnyInstant(long oldPrice, long newPrice)
    {
        for (var elapsed = 0L; elapsed <= March.TotalSeconds; elapsed += 3_607)
        {
            var proration = ProrationCalculator.SameInterval(oldPrice, newPrice, March, Start.AddSeconds(elapsed));

            Math.Abs(proration.CreditCents).ShouldBeLessThanOrEqualTo(oldPrice);
            proration.ChargeCents.ShouldBeLessThanOrEqualTo(newPrice);
            proration.ChargeCents.ShouldBeGreaterThanOrEqualTo(-proration.CreditCents);
        }
    }
}

public sealed class InvoiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 2, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly PlanVersion Starter = PlanVersion.Create(Guid.NewGuid(), 1, new PlanTerms("Starter", 4_900, 49_000, 1_000, 3), Now);
    private static readonly PlanVersion Pro = PlanVersion.Create(Guid.NewGuid(), 1, new PlanTerms("Pro", 14_900, 149_000, 1_000, 10), Now);

    private static Subscription ActiveOn(PlanVersion plan)
    {
        var s = Subscription.StartTrial(Guid.NewGuid(), plan.Id, BillingInterval.Monthly, Now.AddDays(-14));
        s.EndTrial(true, Now);
        return s;
    }

    private static Invoice Issue(Subscription s, PlanVersion plan, DateTimeOffset? at = null)
    {
        var (period, lines) = InvoiceFactory.FullPeriod(s, plan);
        return Invoice.Issue("SUB-2026-000001", s, InvoiceReason.Renewal, period, lines, at ?? Now);
    }

    [Fact]
    public void FormatNumber_UsesYearAndSixDigits() =>
        Invoice.FormatNumber(Now, 42).ShouldBe("SUB-2026-000042");

    [Fact]
    public void Issue_FullPeriod_IsOpen_WithTotalEqualToTheLines_AndPublishesIssued()
    {
        var s = ActiveOn(Starter);

        var invoice = Issue(s, Starter);

        invoice.Status.ShouldBe(InvoiceStatus.Open);
        invoice.TotalCents.ShouldBe(4_900);
        invoice.TotalCents.ShouldBe(invoice.Lines.Sum(l => l.AmountCents));
        invoice.PeriodStart.ShouldBe(s.CurrentPeriodStart);
        invoice.PeriodEnd.ShouldBe(s.CurrentPeriodEnd);
        invoice.Lines.ShouldHaveSingleItem().Description.ShouldBe("Plano Starter (mensal)");
        invoice.Events.ShouldHaveSingleItem().ShouldBeOfType<InvoiceIssued>().TotalCents.ShouldBe(4_900);
    }

    [Fact]
    public void Issue_Upgrade_SameInterval_CreditsAndCharges_ForTheRemainingPeriod()
    {
        var s = ActiveOn(Starter);
        var half = s.CurrentPeriodStart + ((s.CurrentPeriodEnd - s.CurrentPeriodStart) / 2);
        var change = s.ChangePlan(Starter, Pro, BillingInterval.Monthly, half).Value;

        var (period, lines) = InvoiceFactory.Upgrade(s, change, Starter, Pro, half);
        var invoice = Invoice.Issue("SUB-2026-000002", s, InvoiceReason.Upgrade, period, lines, half);

        period.ShouldBe(new BillingPeriod(half, s.CurrentPeriodEnd));
        invoice.Lines.Select(l => l.AmountCents).ShouldBe([-2_450, 7_450]);
        invoice.TotalCents.ShouldBe(5_000);
    }

    [Fact]
    public void Issue_Upgrade_MonthlyToAnnual_CreditsTheMonth_AndChargesTheNewYear()
    {
        var s = ActiveOn(Starter);
        var previousEnd = s.CurrentPeriodEnd;
        var at = s.CurrentPeriodStart + ((s.CurrentPeriodEnd - s.CurrentPeriodStart) / 2);
        var change = s.ChangePlan(Starter, Starter, BillingInterval.Annual, at).Value;

        var (period, lines) = InvoiceFactory.Upgrade(s, change, Starter, Starter, at);

        period.ShouldBe(new BillingPeriod(at, at.AddYears(1)));
        lines[0].Period.ShouldBe(new BillingPeriod(at, previousEnd));
        lines.Select(l => l.AmountCents).ShouldBe([-2_450, 49_000]);
    }

    [Fact]
    public void Issue_ZeroTotal_IsPaidImmediately()
    {
        var s = ActiveOn(Starter);
        var change = s.ChangePlan(Starter, Pro, BillingInterval.Monthly, s.CurrentPeriodEnd).Value;
        var (period, lines) = InvoiceFactory.Upgrade(s, change, Starter, Pro, s.CurrentPeriodEnd);

        var invoice = Invoice.Issue("SUB-2026-000003", s, InvoiceReason.Upgrade, period, lines, s.CurrentPeriodEnd);

        invoice.TotalCents.ShouldBe(0);
        invoice.Status.ShouldBe(InvoiceStatus.Paid);
        invoice.Events.OfType<InvoicePaid>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Issue_NegativeSum_IsNotCarriedOver_TotalStaysEqualToTheLines()
    {
        var s = ActiveOn(Starter);
        var period = s.CurrentPeriod;

        var invoice = Invoice.Issue("SUB-2026-000004", s, InvoiceReason.Upgrade, period,
            [new InvoiceLineDraft("crédito", Starter.Id, -500, period), new InvoiceLineDraft("cobrança", Pro.Id, 200, period)], Now);

        invoice.TotalCents.ShouldBe(0);
        invoice.Lines.Sum(l => l.AmountCents).ShouldBe(0);
        invoice.Status.ShouldBe(InvoiceStatus.Paid);
    }

    [Fact]
    public void MarkPaid_IsIdempotent_AndPublishesOnce()
    {
        var invoice = Issue(ActiveOn(Starter), Starter);

        invoice.MarkPaid(Now).Value.ShouldBeTrue();
        invoice.MarkPaid(Now.AddMinutes(1)).Value.ShouldBeFalse();

        invoice.PaidAt.ShouldBe(Now);
        invoice.Events.OfType<InvoicePaid>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Void_OpenInvoice_RequiresAReason_AndBlocksPayment()
    {
        var invoice = Issue(ActiveOn(Starter), Starter);

        invoice.Void(" ", Now).Error!.Code.ShouldBe("void-reason-required");
        invoice.Void("duplicada", Now).IsSuccess.ShouldBeTrue();
        invoice.Void("duplicada", Now).IsSuccess.ShouldBeTrue();

        invoice.Status.ShouldBe(InvoiceStatus.Void);
        invoice.VoidReason.ShouldBe("duplicada");
        invoice.MarkPaid(Now).Error!.Code.ShouldBe("invoice-void");
        invoice.Events.OfType<InvoiceVoided>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Void_PaidInvoice_Is409()
    {
        var invoice = Issue(ActiveOn(Starter), Starter);
        invoice.MarkPaid(Now);

        invoice.Void("tarde demais", Now).Error!.Code.ShouldBe("invoice-not-open");
    }
}
