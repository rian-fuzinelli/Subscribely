using Subscribely.Modules.Billing.Domain.Plans;
using Subscribely.SharedKernel;

namespace Subscribely.UnitTests.Billing;

public sealed class PlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly PlanTerms Terms = new("Pro", 10_000, 100_000, 1_000, 5);

    [Fact]
    public void Create_StartsActiveWithVersionOne()
    {
        var plan = Plan.Create("pro", Terms, Now).Value;

        plan.Status.ShouldBe(PlanStatus.Active);
        plan.CurrentVersion.Version.ShouldBe(1);
        plan.CurrentVersion.MaxSeats.ShouldBe(5);
    }

    [Fact]
    public void Revise_AddsNewVersionAndKeepsOldOne()
    {
        var plan = Plan.Create("pro", Terms, Now).Value;

        plan.Revise(Terms with { MaxSeats = 8 }, Now.AddDays(1)).IsSuccess.ShouldBeTrue();

        plan.Versions.Count.ShouldBe(2);
        plan.CurrentVersion.Version.ShouldBe(2);
        plan.CurrentVersion.MaxSeats.ShouldBe(8);
        plan.Versions.Single(v => v.Version == 1).MaxSeats.ShouldBe(5);
    }

    [Theory]
    [InlineData(1_000, 12_001, "annual-price-invalid", ErrorType.Unprocessable)]
    [InlineData(-1, 0, "plan-price-invalid", ErrorType.Validation)]
    public void Create_InvalidPrices_Fails(long monthly, long annual, string code, ErrorType type)
    {
        var result = Plan.Create("pro", Terms with { MonthlyPriceCents = monthly, AnnualPriceCents = annual }, Now);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(type);
    }

    [Fact]
    public void Create_AnnualEqualToTwelveMonths_IsAllowed() =>
        Plan.Create("pro", Terms with { MonthlyPriceCents = 1_000, AnnualPriceCents = 12_000 }, Now).IsSuccess.ShouldBeTrue();

    [Fact]
    public void Create_FreePlan_IsAllowed() =>
        Plan.Create("free", Terms with { MonthlyPriceCents = 0, AnnualPriceCents = 0 }, Now).IsSuccess.ShouldBeTrue();

    [Fact]
    public void Create_ZeroSeats_Fails() =>
        Plan.Create("pro", Terms with { MaxSeats = 0 }, Now).Error!.Code.ShouldBe("plan-limits-invalid");

    [Fact]
    public void Archive_ChangesStatus()
    {
        var plan = Plan.Create("pro", Terms, Now).Value;

        plan.Archive();

        plan.Status.ShouldBe(PlanStatus.Archived);
    }
}
