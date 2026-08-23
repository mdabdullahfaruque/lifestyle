using Lifestyle.SharedKernel.Domain;
using Shouldly;
using Xunit;

namespace Lifestyle.UnitTests.SharedKernel;

public sealed class MoneyTests
{
    [Fact]
    public void Requires_a_three_letter_currency()
    {
        Should.Throw<ArgumentException>(() => new Money(10m, "MYRR"));
        Should.Throw<ArgumentException>(() => new Money(10m, "M"));
        Should.Throw<ArgumentException>(() => new Money(10m, " "));
    }

    [Fact]
    public void Normalises_currency_to_upper_case()
    {
        new Money(10m, "myr").Currency.ShouldBe("MYR");
    }

    /// <summary>
    /// The bug this type exists to prevent: adding two amounts in different currencies and getting
    /// a plausible-looking number.
    /// </summary>
    [Fact]
    public void Refuses_to_combine_different_currencies()
    {
        var myr = new Money(10m, "MYR");
        var bdt = new Money(10m, "BDT");

        Should.Throw<InvalidOperationException>(() => myr + bdt);
        Should.Throw<InvalidOperationException>(() => myr - bdt);
        Should.Throw<InvalidOperationException>(() => myr > bdt);
    }

    [Fact]
    public void Adds_and_subtracts_within_a_currency()
    {
        (new Money(10.50m, "MYR") + new Money(4.50m, "MYR")).ShouldBe(new Money(15m, "MYR"));
        (new Money(10m, "MYR") - new Money(2.25m, "MYR")).ShouldBe(new Money(7.75m, "MYR"));
    }

    [Fact]
    public void Multiplies_and_divides()
    {
        (new Money(10m, "MYR") * 3).ShouldBe(new Money(30m, "MYR"));
        (new Money(10m, "MYR") / 4).ShouldBe(new Money(2.5m, "MYR"));
    }

    /// <summary>
    /// Four decimal places of storage, so a 6% commission on 93.00 does not lose precision before
    /// it is rounded for display.
    /// </summary>
    [Fact]
    public void Keeps_four_decimal_places()
    {
        var commission = new Money(93m, "MYR") * 0.065m;
        commission.Amount.ShouldBe(6.045m);
    }

    [Fact]
    public void Rounds_to_the_minor_unit_away_from_zero()
    {
        new Money(6.045m, "MYR").ToMinorUnit().Amount.ShouldBe(6.05m);
        new Money(6.044m, "MYR").ToMinorUnit().Amount.ShouldBe(6.04m);
    }

    [Fact]
    public void Formats_with_invariant_culture()
    {
        // Never a comma decimal separator, whatever the server's locale — Italy is a target market.
        new Money(1234.5m, "EUR").ToString().ShouldBe("1234.5 EUR");
    }

    [Fact]
    public void Compares_within_a_currency()
    {
        (new Money(10m, "MYR") > new Money(5m, "MYR")).ShouldBeTrue();
        (new Money(5m, "MYR") <= new Money(5m, "MYR")).ShouldBeTrue();
        new Money(0m, "MYR").IsZero.ShouldBeTrue();
        new Money(-1m, "MYR").IsNegative.ShouldBeTrue();
    }
}
