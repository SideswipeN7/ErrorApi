using System;
using System.Threading.Tasks;

namespace ErrorApi;

/// <summary>
/// Flow over a result without leaving it. <see cref="Result{T}.Match{TOut}"/> folds to a value;
/// these are its action-shaped relatives: <c>Switch</c> runs one of two branches and ends the flow,
/// <c>OnSuccess</c>/<c>OnFailure</c> run a side effect and hand the same result back, so they slot
/// into the middle of a chain — logging, metrics, auditing — without interrupting it.
/// </summary>
/// <example>
/// <code>
/// // Switch closes the flow:
/// service.Pay(id).Switch(
///     order => _log.Paid(order),
///     error => _alerts.Raise(error));
///
/// // OnSuccess/OnFailure ride along inside one:
/// return (await service.PayAsync(id)
///     .OnSuccess(order => _log.Paid(order))
///     .OnFailure(error => _metrics.Bump(error.Code)))
///     .ToHttpResult();
/// </code>
/// </example>
public static class ResultFlowExtensions
{
    /// <summary>Runs exactly one branch — the action twin of <c>Match</c>.</summary>
    public static void Switch(this Result result, Action onSuccess, Action<Error> onFailure)
    {
        if (result.IsSuccess)
        {
            onSuccess();
        }
        else
        {
            onFailure(result.Error);
        }
    }

    /// <summary>Runs exactly one branch — the action twin of <c>Match</c>.</summary>
    public static void Switch<T>(this Result<T> result, Action<T> onSuccess, Action<Error> onFailure)
    {
        if (result.IsSuccess)
        {
            onSuccess(result.Value);
        }
        else
        {
            onFailure(result.Error);
        }
    }

    /// <summary>Runs <paramref name="action"/> when the result succeeded, then hands the result back.</summary>
    public static Result OnSuccess(this Result result, Action action)
    {
        if (result.IsSuccess)
        {
            action();
        }

        return result;
    }

    /// <summary>Runs <paramref name="action"/> with the value when the result succeeded, then hands the result back.</summary>
    public static Result<T> OnSuccess<T>(this Result<T> result, Action<T> action)
    {
        if (result.IsSuccess)
        {
            action(result.Value);
        }

        return result;
    }

    /// <summary>Runs <paramref name="action"/> with the failure when the result failed, then hands the result back.</summary>
    public static Result OnFailure(this Result result, Action<Error> action)
    {
        if (result.IsFailure)
        {
            action(result.Error);
        }

        return result;
    }

    /// <summary>Runs <paramref name="action"/> with the failure when the result failed, then hands the result back.</summary>
    public static Result<T> OnFailure<T>(this Result<T> result, Action<Error> action)
    {
        if (result.IsFailure)
        {
            action(result.Error);
        }

        return result;
    }

    /// <inheritdoc cref="Switch(Result, Action, Action{Error})"/>
    public static async Task Switch(this Task<Result> result, Action onSuccess, Action<Error> onFailure) =>
        (await result.ConfigureAwait(false)).Switch(onSuccess, onFailure);

    /// <inheritdoc cref="Switch{T}(Result{T}, Action{T}, Action{Error})"/>
    public static async Task Switch<T>(this Task<Result<T>> result, Action<T> onSuccess, Action<Error> onFailure) =>
        (await result.ConfigureAwait(false)).Switch(onSuccess, onFailure);

    /// <inheritdoc cref="OnSuccess(Result, Action)"/>
    public static async Task<Result> OnSuccess(this Task<Result> result, Action action) =>
        (await result.ConfigureAwait(false)).OnSuccess(action);

    /// <inheritdoc cref="OnSuccess{T}(Result{T}, Action{T})"/>
    public static async Task<Result<T>> OnSuccess<T>(this Task<Result<T>> result, Action<T> action) =>
        (await result.ConfigureAwait(false)).OnSuccess(action);

    /// <inheritdoc cref="OnFailure(Result, Action{Error})"/>
    public static async Task<Result> OnFailure(this Task<Result> result, Action<Error> action) =>
        (await result.ConfigureAwait(false)).OnFailure(action);

    /// <inheritdoc cref="OnFailure{T}(Result{T}, Action{Error})"/>
    public static async Task<Result<T>> OnFailure<T>(this Task<Result<T>> result, Action<Error> action) =>
        (await result.ConfigureAwait(false)).OnFailure(action);

#if NET
    /// <inheritdoc cref="Switch{T}(Result{T}, Action{T}, Action{Error})"/>
    public static async ValueTask Switch<T>(this ValueTask<Result<T>> result, Action<T> onSuccess, Action<Error> onFailure) =>
        (await result.ConfigureAwait(false)).Switch(onSuccess, onFailure);

    /// <inheritdoc cref="Switch(Result, Action, Action{Error})"/>
    public static async ValueTask Switch(this ValueTask<Result> result, Action onSuccess, Action<Error> onFailure) =>
        (await result.ConfigureAwait(false)).Switch(onSuccess, onFailure);

    /// <inheritdoc cref="OnSuccess{T}(Result{T}, Action{T})"/>
    public static async ValueTask<Result<T>> OnSuccess<T>(this ValueTask<Result<T>> result, Action<T> action) =>
        (await result.ConfigureAwait(false)).OnSuccess(action);

    /// <inheritdoc cref="OnFailure{T}(Result{T}, Action{Error})"/>
    public static async ValueTask<Result<T>> OnFailure<T>(this ValueTask<Result<T>> result, Action<Error> action) =>
        (await result.ConfigureAwait(false)).OnFailure(action);
#endif
}
