using System.Globalization;

namespace Lifestyle.SharedKernel.Domain;

/// <summary>
/// Money is a decimal amount plus an ISO-4217 currency. Never float, never a bare decimal —
/// a bare decimal loses the currency and lets you add MYR to BDT without noticing.
/// Mapped as an EF owned type to numeric(18,4) + char(3).
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new ArgumentException("Currency must be a 3-letter ISO-4217 code.", nameof(currency));

        Amount = decimal.Round(amount, 4, MidpointRounding.ToEven);
        Currency = currency.ToUpperInvariant();
    }

    public static Money Zero(string currency) => new(0m, currency);

    public bool IsZero => Amount == 0m;
    public bool IsNegative => Amount < 0m;

    public static Money operator +(Money a, Money b) => new(a.Amount + Same(a, b).Amount, a.Currency);
    public static Money operator -(Money a, Money b) => new(a.Amount - Same(a, b).Amount, a.Currency);
    public static Money operator *(Money a, decimal factor) => new(a.Amount * factor, a.Currency);
    public static Money operator /(Money a, decimal divisor) => new(a.Amount / divisor, a.Currency);

    public static bool operator >(Money a, Money b) => a.Amount > Same(a, b).Amount;
    public static bool operator <(Money a, Money b) => a.Amount < Same(a, b).Amount;
    public static bool operator >=(Money a, Money b) => a.Amount >= Same(a, b).Amount;
    public static bool operator <=(Money a, Money b) => a.Amount <= Same(a, b).Amount;

    public Money Add(Money other) => this + other;
    public Money Subtract(Money other) => this - other;
    public Money Multiply(decimal factor) => this * factor;
    public Money Divide(decimal divisor) => this / divisor;

    public int CompareTo(Money other) => Amount.CompareTo(Same(this, other).Amount);

    /// <summary>Rounds to the currency's minor unit for display and for what we actually charge.</summary>
    public Money ToMinorUnit(int decimals = 2) =>
        new(decimal.Round(Amount, decimals, MidpointRounding.AwayFromZero), Currency);

    private static Money Same(Money a, Money b) => a.Currency == b.Currency
        ? b
        : throw new InvalidOperationException($"Cannot combine {a.Currency} with {b.Currency}.");

    /// <summary>Serialised as a string so no JSON parser can turn it into a float (FRD §19.1).</summary>
    public override string ToString() =>
        $"{Amount.ToString("0.####", CultureInfo.InvariantCulture)} {Currency}";
}
