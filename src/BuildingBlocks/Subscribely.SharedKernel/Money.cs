namespace Subscribely.SharedKernel;

/// <summary>Valor monetário em centavos (BRL). Nunca usar ponto flutuante para dinheiro.</summary>
public readonly record struct Money(long Cents) : IComparable<Money>
{
    public const string Currency = "BRL";

    public static Money Zero => new(0);

    public static Money operator +(Money a, Money b) => new(checked(a.Cents + b.Cents));

    public static Money operator -(Money a, Money b) => new(checked(a.Cents - b.Cents));

    public static Money operator -(Money a) => new(checked(-a.Cents));

    public static bool operator <(Money a, Money b) => a.Cents < b.Cents;

    public static bool operator >(Money a, Money b) => a.Cents > b.Cents;

    public static bool operator <=(Money a, Money b) => a.Cents <= b.Cents;

    public static bool operator >=(Money a, Money b) => a.Cents >= b.Cents;

    /// <summary>Calcula <c>Cents × numerator / denominator</c> com arredondamento half-up, sem overflow.</summary>
    public Money Fraction(long numerator, long denominator)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
        ArgumentOutOfRangeException.ThrowIfNegative(numerator);

        var product = (Int128)Cents * numerator;
        var (quotient, remainder) = Int128.DivRem(product, denominator);
        if (Int128.Abs(remainder) * 2 >= denominator)
        {
            quotient += Int128.Sign(product);
        }

        return new Money((long)quotient);
    }

    public int CompareTo(Money other) => Cents.CompareTo(other.Cents);

    public override string ToString() => $"{Cents / 100m:0.00} {Currency}";
}
