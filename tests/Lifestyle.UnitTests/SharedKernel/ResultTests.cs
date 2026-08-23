using Lifestyle.SharedKernel.Results;
using Shouldly;
using Xunit;

namespace Lifestyle.UnitTests.SharedKernel;

public sealed class ResultTests
{
    [Fact]
    public void Success_carries_a_value_and_no_error()
    {
        Result<int> result = 42;

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Value.ShouldBe(42);
        result.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void Failure_carries_an_error_and_refuses_its_value()
    {
        Result<int> result = Error.NotFound("catalog.product_not_found");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.product_not_found");
        result.Error.Type.ShouldBe(ErrorType.NotFound);

        // Reading Value on a failure is a bug in the caller, and should be loud rather than silent.
        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void A_successful_result_cannot_carry_an_error()
    {
        Should.Throw<InvalidOperationException>(() => Result.Failure(Error.None));
    }

    [Fact]
    public void Match_branches_on_the_outcome()
    {
        Result<int> success = 10;
        Result<int> failure = Error.Conflict("x.y", "nope");

        success.Match(v => $"ok:{v}", e => $"err:{e.Code}").ShouldBe("ok:10");
        failure.Match(v => $"ok:{v}", e => $"err:{e.Code}").ShouldBe("err:x.y");
    }

    [Fact]
    public void Map_transforms_success_and_passes_failure_through()
    {
        Result<int> success = 5;
        Result<int> failure = Error.Validation("bad", "no");

        success.Map(v => v * 2).Value.ShouldBe(10);
        failure.Map(v => v * 2).Error.Code.ShouldBe("bad");
    }

    [Fact]
    public void Validation_errors_carry_field_details()
    {
        var error = Error.Validation(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["email"] = ["Email is required."],
            ["password"] = ["Password must be at least 10 characters."]
        });

        error.Type.ShouldBe(ErrorType.Validation);
        error.FieldErrors.ShouldNotBeNull();
        error.FieldErrors!.Count.ShouldBe(2);
        error.FieldErrors["email"].ShouldContain("Email is required.");
    }
}
