using Subscribely.SharedKernel;

namespace Subscribely.UnitTests.SharedKernel;

public sealed class MoneyTests
{
    [Theory]
    [InlineData(1000, 1, 2, 500)]
    [InlineData(1001, 1, 2, 501)]
    [InlineData(999, 1, 3, 333)]
    [InlineData(1000, 2, 3, 667)]
    [InlineData(5, 1, 10, 1)]
    [InlineData(4, 1, 10, 0)]
    [InlineData(-1001, 1, 2, -501)]
    [InlineData(1000, 0, 7, 0)]
    public void Fraction_RoundsHalfUp(long cents, long numerator, long denominator, long expected) =>
        new Money(cents).Fraction(numerator, denominator).Cents.ShouldBe(expected);

    [Fact]
    public void Fraction_LargeValues_DoesNotOverflow() =>
        new Money(long.MaxValue / 2).Fraction(31_536_000, 31_536_000).Cents.ShouldBe(long.MaxValue / 2);

    [Fact]
    public void Fraction_ZeroDenominator_Throws() =>
        Should.Throw<ArgumentOutOfRangeException>(() => new Money(10).Fraction(1, 0));

    [Fact]
    public void Operators_AddAndSubtract() =>
        (new Money(150) + new Money(50) - new Money(25)).ShouldBe(new Money(175));

    [Fact]
    public void Add_Overflow_Throws() =>
        Should.Throw<OverflowException>(() => new Money(long.MaxValue) + new Money(1));
}
