using System.Diagnostics.CodeAnalysis;

namespace Lifestyle.SharedKernel.Results;

/// <summary>
/// The expected-failure channel. Handlers return this instead of throwing for anything a client can
/// cause; exceptions are reserved for bugs and infrastructure faults (docs/04 §3.5).
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
            throw new InvalidOperationException("A successful result cannot carry an error.");
        if (!isSuccess && error == Error.None)
            throw new InvalidOperationException("A failed result must carry an error.");

        IsSuccess = isSuccess;
        Error = error;
    }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>A <see cref="Result"/> that carries a value when successful.</summary>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error) : base(isSuccess, error) => _value = value;

    /// <summary>Throws if the result is a failure — check <see cref="Result.IsSuccess"/> first.</summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot read the value of a failed result.");

    public static implicit operator Result<TValue>(TValue value) => Success(value);
    public static implicit operator Result<TValue>(Error error) => Failure<TValue>(error);

    public TOut Match<TOut>(Func<TValue, TOut> onSuccess, Func<Error, TOut> onFailure) =>
        IsSuccess ? onSuccess(_value!) : onFailure(Error);

    public Result<TOut> Map<TOut>(Func<TValue, TOut> map) =>
        IsSuccess ? Success(map(_value!)) : Failure<TOut>(Error);
}
