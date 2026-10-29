using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Billing.Domain.Plans;
using Subscribely.Modules.Billing.Domain.Subscriptions;

namespace Subscribely.UnitTests.Billing;

public sealed class PlanChangeClassifierTests
{
    private static PlanVersion Plan(long monthly, long annual) =>
        PlanVersion.Create(Guid.NewGuid(), 1, new PlanTerms("p", monthly, annual, 100, 1), DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData(4_900, 14_900, "Upgrade")]
    [InlineData(14_900, 4_900, "Downgrade")]
    [InlineData(4_900, 4_900, "Lateral")]
    public void SameInterval_ComparesPrices(long current, long target, string expected) =>
        PlanChangeClassifier.Classify(Plan(current, current * 10), BillingInterval.Monthly, Plan(target, target * 10), BillingInterval.Monthly)
            .ShouldBe(Enum.Parse<PlanChangeKind>(expected));

    [Fact]
    public void SameInterval_Annual_UsesTheAnnualPrice() =>
        PlanChangeClassifier.Classify(Plan(100, 1_000), BillingInterval.Annual, Plan(100, 1_100), BillingInterval.Annual)
            .ShouldBe(PlanChangeKind.Upgrade);

    [Fact]
    public void MonthlyToAnnual_IsUpgrade_EvenWhenTheDailyPriceIsLower() =>
        PlanChangeClassifier.Classify(Plan(10_000, 60_000), BillingInterval.Monthly, Plan(10_000, 60_000), BillingInterval.Annual)
            .ShouldBe(PlanChangeKind.Upgrade);

    [Fact]
    public void AnnualToMonthly_IsDowngrade_EvenOnAPricierPlan() =>
        PlanChangeClassifier.Classify(Plan(4_900, 49_000), BillingInterval.Annual, Plan(49_900, 499_000), BillingInterval.Monthly)
            .ShouldBe(PlanChangeKind.Downgrade);
}
