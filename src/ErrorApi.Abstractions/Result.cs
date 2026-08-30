using System;

namespace ErrorApi;

/// <summary>
/// The runtime shape <see cref="Result"/> and <see cref="Result{T}"/> share, so a hosting-layer
/// component — the endpoint filter behind <c>AddErrorApiResults()</c> — can map a boxed result
/// without knowing <c>T</c>. Not intended for application code; call the typed members instead.
/// </summary>
public interface IErrorApiResult
{
    /// <summary><see langword="true"/> when the operation succeeded.</summary>
    bool IsSuccess { get; }

    /// <summary>The failure, or <see cref="ErrorApi.Error.None"/> when successful.</summary>
    Error Error { get; }

    /// <summary><see langword="true"/> when a successful result carries a value.</summary>
    bool HasValue { get; }

    /// <summary>The boxed success value, or <see langword="null"/>.</summary>
    object? ValueOrNull { get; }
}

/// <summary>An operation that either succeeded or failed with a single <see cref="ErrorApi.Error"/>.</summary>
public readonly struct Result : IErrorApiResult
{
    private Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>A successful result.</summary>
    public static Result Success() => new(true, Error.None);

    /// <summary>A failed result carrying <paramref name="error"/>.</summary>
    public static Result Failure(Error error) => new(false, error);

    /// <summary><see langword="true"/> when the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary><see langword="true"/> when the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>The failure, or <see cref="ErrorApi.Error.None"/> when successful.</summary>
    public Error Error { get; }

    /// <summary>Folds both branches into a single value.</summary>
    public TOut Match<TOut>(Func<TOut> onSuccess, Func<Error, TOut> onFailure) =>
        IsSuccess ? onSuccess() : onFailure(Error);

    bool IErrorApiResult.HasValue => false;

    object? IErrorApiResult.ValueOrNull => null;

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>An operation that either produced a <typeparamref name="T"/> or failed with a single <see cref="ErrorApi.Error"/>.</summary>
/// <typeparam name="T">The success value type.</typeparam>
public readonly struct Result<T> : IErrorApiResult
{
    private readonly T _value;

    private Result(bool isSuccess, T value, Error error)
    {
        IsSuccess = isSuccess;
        _value = value;
        Error = error;
    }

    /// <summary>A successful result carrying <paramref name="value"/>.</summary>
    public static Result<T> Success(T value) => new(true, value, Error.None);

    /// <summary>A failed result carrying <paramref name="error"/>.</summary>
    public static Result<T> Failure(Error error) => new(false, default!, error);

    /// <summary><see langword="true"/> when the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary><see langword="true"/> when the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>The failure, or <see cref="ErrorApi.Error.None"/> when successful.</summary>
    public Error Error { get; }

    /// <summary>The success value. Throws when the result is a failure.</summary>
    /// <exception cref="InvalidOperationException">The result is a failure.</exception>
    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException($"Result is a failure ({Error.Code}); Value is not available.");

    /// <summary>Folds both branches into a single value.</summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure) =>
        IsSuccess ? onSuccess(_value) : onFailure(Error);

    /// <summary>Projects the success value, propagating the failure untouched.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> map) =>
        IsSuccess ? Result<TOut>.Success(map(_value)) : Result<TOut>.Failure(Error);

    /// <summary>Chains another fallible operation, propagating the failure untouched.</summary>
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> bind) =>
        IsSuccess ? bind(_value) : Result<TOut>.Failure(Error);

    /// <summary>Drops the success value, keeping only success/failure.</summary>
    public Result WithoutValue() => IsSuccess ? Result.Success() : Result.Failure(Error);

    bool IErrorApiResult.HasValue => IsSuccess;

    object? IErrorApiResult.ValueOrNull => IsSuccess ? _value : null;

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}
