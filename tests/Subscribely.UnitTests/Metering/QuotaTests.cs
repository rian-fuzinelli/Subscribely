using Subscribely.Modules.Metering.Quota;

namespace Subscribely.UnitTests.Metering;

public sealed class QuotaTests
{
    [Fact]
    public void Period_IsTheUtcCalendarMonth_AndResetsOnTheFirstOfTheNextMonth()
    {
        var period = QuotaPeriod.At(new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.FromHours(-3)));

        period.Name.ShouldBe("2027-01", "23:30 BRT on Dec 31 is 02:30 UTC on Jan 1");
        period.ResetsAt.ShouldBe(new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Keys_FollowTheSpecFormat()
    {
        var tenantId = Guid.Parse("8d1f7f7e-0000-4000-8000-000000000001");
        var period = QuotaPeriod.At(new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero));

        period.CounterKey(tenantId).ShouldBe("quota:8d1f7f7e-0000-4000-8000-000000000001:202603");
        period.AlertKey(tenantId, 80).ShouldBe("quota-alert:8d1f7f7e-0000-4000-8000-000000000001:2026-03:80");
    }

    [Theory]
    [InlineData("2026-01-31T23:59:59Z", 1)]
    [InlineData("2026-01-31T23:59:59.5Z", 1)]
    [InlineData("2026-01-31T00:00:00Z", 86_400)]
    public void RetryAfter_IsTheSecondsUntilReset_RoundedUp(string now, long expected) =>
        QuotaPeriod.At(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture))
            .SecondsUntilReset(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(expected);

    [Theory]
    [InlineData(80, 100, 80)]
    [InlineData(80, 10, 8)]
    [InlineData(80, 3, 3)] // ceil(2,4)
    [InlineData(100, 7, 7)]
    public void Threshold_UsageIsTheCeilingOfThePercentage(int threshold, long limit, long expected) =>
        QuotaAlerts.UsageFor(threshold, limit).ShouldBe(expected);

    [Fact]
    public void Decision_Remaining_IsNeverNegative()
    {
        var period = QuotaPeriod.At(DateTimeOffset.UnixEpoch);

        new QuotaDecision(false, 100, 100, period).Remaining.ShouldBe(0);
        new QuotaDecision(true, 40, 100, period).Remaining.ShouldBe(60);
        new QuotaDecision(true, null, 100, period).Remaining.ShouldBe(100, "fail-open: the usage is unknown");
    }
}
