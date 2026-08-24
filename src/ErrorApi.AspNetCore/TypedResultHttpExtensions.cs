using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ErrorApi;

/// <summary>
/// The <c>TypedResults</c> twins of <see cref="ResultHttpExtensions"/>. Where <c>ToHttpResult()</c>
/// answers with <see cref="IResult"/>, these answer with <c>Results&lt;…, ProblemHttpResult&gt;</c>, so
/// ASP.NET reads the success shape straight off the endpoint signature and documents its schema without
/// any help from the transformer. The failure half is <see cref="ProblemHttpResult"/>, which carries no
/// static status — that is exactly the hole the generator's per-endpoint contract fills.
/// </summary>
public static class TypedResultHttpExtensions
{
    /// <summary>Maps success to <c>200 OK</c> with the value, and failure to <c>ProblemDetails</c>.</summary>
    public static Results<Ok<T>, ProblemHttpResult> ToTypedResult<T>(this Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();

    /// <summary>Maps success to <c>204 No Content</c>, and failure to <c>ProblemDetails</c>.</summary>
    public static Results<NoContent, ProblemHttpResult> ToTypedResult(this Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();

    /// <summary>Maps success to <c>201 Created</c> at a fixed <paramref name="location"/>, and failure to <c>ProblemDetails</c>.</summary>
    public static Results<Created<T>, ProblemHttpResult> ToTypedCreated<T>(this Result<T> result, string location) =>
        result.IsSuccess ? TypedResults.Created(location, result.Value) : result.Error.ToProblem();

    /// <inheritdoc cref="ResultHttpExtensions.ToCreated{T}(Result{T}, Func{T, string})"/>
    public static Results<Created<T>, ProblemHttpResult> ToTypedCreated<T>(this Result<T> result, Func<T, string> location) =>
        result.IsSuccess ? TypedResults.Created(location(result.Value), result.Value) : result.Error.ToProblem();

    /// <inheritdoc cref="ResultHttpExtensions.ToCreated{T}(Result{T}, Func{T, string})"/>
    public static Results<Created<T>, ProblemHttpResult> ToTypedCreated<T>(this Result<T> result, Func<T, Uri> location) =>
        result.IsSuccess ? TypedResults.Created(location(result.Value), result.Value) : result.Error.ToProblem();

    /// <inheritdoc cref="ResultHttpExtensions.ToCreatedAtRoute{T}(Result{T}, string, Func{T, RouteValueDictionary})"/>
    public static Results<CreatedAtRoute<T>, ProblemHttpResult> ToTypedCreatedAtRoute<T>(
        this Result<T> result, string routeName, Func<T, RouteValueDictionary> routeValues) =>
        result.IsSuccess
            ? TypedResults.CreatedAtRoute(result.Value, routeName, routeValues(result.Value))
            : result.Error.ToProblem();

    /// <inheritdoc cref="ResultHttpExtensions.ToCreatedAtRoute{T}(Result{T}, string, Func{T, RouteValueDictionary})"/>
    public static Results<CreatedAtRoute<T>, ProblemHttpResult> ToTypedCreatedAtRoute<T>(this Result<T> result, string routeName) =>
        result.IsSuccess
            // The null is typed so this binds to the RouteValueDictionary overload; the object one
            // reflects over its argument and would drop the AOT guarantee.
            ? TypedResults.CreatedAtRoute(result.Value, routeName, (RouteValueDictionary?)null)
            : result.Error.ToProblem();

    /// <inheritdoc cref="ToTypedResult{T}(Result{T})"/>
    public static async Task<Results<Ok<T>, ProblemHttpResult>> ToTypedResult<T>(this Task<Result<T>> result) =>
        (await result.ConfigureAwait(false)).ToTypedResult();

    /// <inheritdoc cref="ToTypedResult(Result)"/>
    public static async Task<Results<NoContent, ProblemHttpResult>> ToTypedResult(this Task<Result> result) =>
        (await result.ConfigureAwait(false)).ToTypedResult();

    /// <inheritdoc cref="ToTypedResult{T}(Result{T})"/>
    public static async ValueTask<Results<Ok<T>, ProblemHttpResult>> ToTypedResult<T>(this ValueTask<Result<T>> result) =>
        (await result.ConfigureAwait(false)).ToTypedResult();

    /// <inheritdoc cref="ToTypedResult(Result)"/>
    public static async ValueTask<Results<NoContent, ProblemHttpResult>> ToTypedResult(this ValueTask<Result> result) =>
        (await result.ConfigureAwait(false)).ToTypedResult();

    /// <inheritdoc cref="ToTypedCreated{T}(Result{T}, string)"/>
    public static async Task<Results<Created<T>, ProblemHttpResult>> ToTypedCreated<T>(this Task<Result<T>> result, string location) =>
        (await result.ConfigureAwait(false)).ToTypedCreated(location);

    /// <inheritdoc cref="ToTypedCreated{T}(Result{T}, Func{T, string})"/>
    public static async Task<Results<Created<T>, ProblemHttpResult>> ToTypedCreated<T>(this Task<Result<T>> result, Func<T, string> location) =>
        (await result.ConfigureAwait(false)).ToTypedCreated(location);

    /// <inheritdoc cref="ToTypedCreatedAtRoute{T}(Result{T}, string, Func{T, RouteValueDictionary})"/>
    public static async Task<Results<CreatedAtRoute<T>, ProblemHttpResult>> ToTypedCreatedAtRoute<T>(
        this Task<Result<T>> result, string routeName, Func<T, RouteValueDictionary> routeValues) =>
        (await result.ConfigureAwait(false)).ToTypedCreatedAtRoute(routeName, routeValues);
}
