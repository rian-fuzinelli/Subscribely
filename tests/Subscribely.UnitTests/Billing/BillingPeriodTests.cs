using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Billing.Domain.Subscriptions;

namespace Subscribely.UnitTests.Billing;

public sealed class BillingPeriodTests
{
    private static DateTimeOffset At(int year, int month, int day) => new(year, month, day, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(2026, 1, 31, 2026, 2, 28)]
    [InlineData(2028, 1, 31, 2028, 2, 29)]
    [InlineData(2026, 1, 15, 2026, 2, 15)]
    [InlineData(2026, 12, 31, 2027, 1, 31)]
    public void Starting_Monthly_AddsOneCalendarMonth(int y, int m, int d, int ey, int em, int ed)
    {
        var period = BillingPeriod.Starting(At(y, m, d), BillingInterval.Monthly);

        period.Start.ShouldBe(At(y, m, d));
        period.End.ShouldBe(At(ey, em, ed));
    }

    [Fact]
    public void After_KeepsTheOriginalDayOfMonth()
    {
        var anchor = At(2026, 1, 31);

        var feb = BillingPeriod.Starting(anchor, BillingInterval.Monthly);
        var mar = BillingPeriod.After(anchor, BillingInterval.Monthly, feb.End);
        var apr = BillingPeriod.After(anchor, BillingInterval.Monthly, mar.End);

        feb.End.ShouldBe(At(2026, 2, 28));
        mar.ShouldBe(new BillingPeriod(At(2026, 2, 28), At(2026, 3, 31)));
        apr.ShouldBe(new BillingPeriod(At(2026, 3, 31), At(2026, 4, 30)));
    }

    [Fact]
    public void Annual_FromLeapDay_ReturnsToLeapDayWhenItExists()
    {
        var anchor = At(2028, 2, 29);

        var first = BillingPeriod.Starting(anchor, BillingInterval.Annual);
        var fourth = BillingPeriod.After(anchor, BillingInterval.Annual, BillingPeriod.Add(anchor, BillingInterval.Annual, 3));

        first.End.ShouldBe(At(2029, 2, 28));
        fourth.End.ShouldBe(At(2032, 2, 29));
    }

    [Fact]
    public void TotalSeconds_IsTheLengthOfThePeriod()
    {
        BillingPeriod.Starting(At(2026, 2, 1), BillingInterval.Monthly).TotalSeconds.ShouldBe(28L * 24 * 3600);
    }
}
