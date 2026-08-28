using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

// CSharpFunctionalExtensions and ErrorApi both export a type called Result; inside this namespace the
// bare name binds to ours, so the CFE side is spelled out — an alias cannot cover the open generics.
using CfeResult = CSharpFunctionalExtensions.Result;

namespace ErrorApi.Interop;

/// <summary>
/// Bridges <see href="https://github.com/vkhorikov/CSharpFunctionalExtensions">CSharpFunctionalExtensions</see>
/// onto ErrorApi.
/// </summary>
/// <remarks>
/// <para>
/// <c>Result&lt;T, E&gt;</c> is the sweet spot: the failure <em>is</em> a type of your own, so that is
/// where the catalog entry goes — annotate <c>E</c> (or its concrete cases) and keep returning results
/// everywhere else:
/// </para>
/// <code>
/// [ErrorApi.Error("Orders.NotFound", 404, Title = "Order not found")]
/// public sealed record OrderNotFound(Guid Id);
///
/// public Result&lt;Order, OrderNotFound&gt; GetById(Guid id) =&gt;
///     _orders.TryGetValue(id, out var order) ? order : new OrderNotFound(id);
///
/// orders.MapGet("/{id:guid}", (Guid id, IOrderService s) =&gt; s.GetById(id).ToHttpResult());
/// </code>
/// <para>
/// The generator documents the endpoint wherever it sees the case constructed; at runtime the instance
/// resolves through the generated type switch, so status and title come from the same declaration the
/// document was built from. A string-error <c>Result&lt;T&gt;</c> still answers: a message that happens
/// to be a known catalog code resolves fully, anything else is a 500 carrying the message — deliberately
/// unhelpful as a contract, because a message is not one.
/// </para>
/// </remarks>
public static class CfeHttpExtensions
{
    /// <summary>
    /// Resolves a typed failure against the generated catalog: by its type first, then — when the value
    /// is a string that is a known code — by the code, falling back to a 500.
    /// </summary>
    /// <param name="error">The failure value.</param>
    /// <param name="metadata">The model to resolve against. Defaults to the one <c>AddErrorApi()</c> registered.</param>
    public static ErrorApi.Error Resolve<TError>(TError error, IErrorApiMetadata? metadata = null)
    {
        var model = metadata ?? ErrorApiRuntime.Metadata;

        var descriptor = model?.FindErrorForInstance(error);
        if (descriptor is null && error is string code)
        {
            descriptor = model?.FindError(code);
        }

        if (descriptor is not null)
        {
            return descriptor.ToError();
        }

        return new ErrorApi.Error(
            error?.GetType().Name ?? "Unknown",
            StatusCodes.Status500InternalServerError,
            title: null,
            detail: error?.ToString());
    }

    /// <summary>Maps a failure value onto <c>application/problem+json</c>. A plain static rather than an
    /// extension on <c>TError</c>, so it does not appear on every type in IntelliSense.</summary>
    public static IResult Problem<TError>(TError error) => Resolve(error).ToProblem();

    /// <summary>Maps success to <c>200 OK</c> and the typed failure to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult<TValue, TError>(this global::CSharpFunctionalExtensions.Result<TValue, TError> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : Problem(result.Error);

    /// <summary>Maps success through <paramref name="onSuccess"/> and the typed failure to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult<TValue, TError>(
        this global::CSharpFunctionalExtensions.Result<TValue, TError> result, Func<TValue, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : Problem(result.Error);

    /// <summary>Maps success to <c>200 OK</c> and the string failure to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult<TValue>(this global::CSharpFunctionalExtensions.Result<TValue> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : Problem(result.Error);

    /// <summary>Maps success to <c>204 No Content</c> and the string failure to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult(this CfeResult result) =>
        result.IsSuccess ? TypedResults.NoContent() : Problem(result.Error);

    /// <summary>Maps success to <c>204 No Content</c> and the typed failure to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult<TError>(this global::CSharpFunctionalExtensions.UnitResult<TError> result) =>
        result.IsSuccess ? TypedResults.NoContent() : Problem(result.Error);

    /// <summary>Maps success to <c>204 No Content</c> and the typed failure to <c>ProblemDetails</c>.</summary>
    public static IResult ToNoContentResult<TValue, TError>(this global::CSharpFunctionalExtensions.Result<TValue, TError> result) =>
        result.IsSuccess ? TypedResults.NoContent() : Problem(result.Error);

    /// <summary>Maps success to <c>201 Created</c> at a location built from the created value, and the typed failure to <c>ProblemDetails</c>.</summary>
    public static IResult ToCreated<TValue, TError>(
        this global::CSharpFunctionalExtensions.Result<TValue, TError> result, Func<TValue, string> location) =>
        result.IsSuccess ? TypedResults.Created(location(result.Value), result.Value) : Problem(result.Error);

    /// <summary>Maps success to <c>201 Created</c> at a fixed location, and the typed failure to <c>ProblemDetails</c>.</summary>
    public static IResult ToCreated<TValue, TError>(
        this global::CSharpFunctionalExtensions.Result<TValue, TError> result, string location) =>
        result.IsSuccess ? TypedResults.Created(location, result.Value) : Problem(result.Error);

    /// <summary>The <see cref="Uri"/> twin of the string-location form — its own name, so a throwing lambda is never ambiguous between the two.</summary>
    public static IResult ToCreatedAtUri<TValue, TError>(
        this global::CSharpFunctionalExtensions.Result<TValue, TError> result, Func<TValue, Uri> location) =>
        result.IsSuccess ? TypedResults.Created(location(result.Value), result.Value) : Problem(result.Error);

    /// <inheritdoc cref="ToHttpResult{TValue, TError}(global::CSharpFunctionalExtensions.Result{TValue, TError})"/>
    public static async Task<IResult> ToHttpResult<TValue, TError>(
        this Task<global::CSharpFunctionalExtensions.Result<TValue, TError>> result) =>
        (await result.ConfigureAwait(false)).ToHttpResult();

    /// <inheritdoc cref="ToHttpResult{TValue}(global::CSharpFunctionalExtensions.Result{TValue})"/>
    public static async Task<IResult> ToHttpResult<TValue>(
        this Task<global::CSharpFunctionalExtensions.Result<TValue>> result) =>
        (await result.ConfigureAwait(false)).ToHttpResult();

    /// <inheritdoc cref="ToHttpResult(CfeResult)"/>
    public static async Task<IResult> ToHttpResult(this Task<CfeResult> result) =>
        (await result.ConfigureAwait(false)).ToHttpResult();
}
