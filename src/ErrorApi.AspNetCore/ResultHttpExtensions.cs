using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ErrorApi;

/// <summary>
/// Maps <see cref="Result"/> and <see cref="Result{T}"/> onto Minimal API results.
/// The mapping is a plain branch over data the catalog already carries, so there is no reflection,
/// no runtime type scan, and nothing for the trimmer to keep alive.
/// </summary>
public static class ResultHttpExtensions
{
    /// <summary>The <c>ProblemDetails</c> extension member carrying the machine-readable error code.</summary>
    public const string CodeExtensionName = "code";

    /// <summary>
    /// Optional format string used to fill <c>ProblemDetails.type</c>, with <c>{0}</c> replaced by the
    /// error code — for example <c>https://errors.contoso.com/{0}</c>. Left unset, <c>type</c> is omitted.
    /// </summary>
    public static string? ProblemTypeUriFormat { get; set; }

    /// <summary>Maps a failure onto <c>application/problem+json</c>, carrying the error code as an extension member.</summary>
    public static ProblemHttpResult ToProblem(this Error error)
    {
        // Built as a ProblemDetails directly: the TypedResults.Problem(extensions:) overload would
        // copy a temporary dictionary into the one ProblemDetails already owns — one dictionary and
        // one copy per failure, measured on the benchmark's failure path, for nothing.
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = error.StatusCode,
            Title = error.Title,
            Detail = error.Detail,
            Type = ProblemTypeUriFormat is null ? null : string.Format(ProblemTypeUriFormat, error.Code),
        };

        problem.Extensions[CodeExtensionName] = error.Code;

        if (error.Extensions is not null)
        {
            foreach (var pair in error.Extensions)
            {
                problem.Extensions[pair.Key] = pair.Value;
            }
        }

        return TypedResults.Problem(problem);
    }

    /// <summary>Maps success to <c>200 OK</c> with the value, and failure to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult<T>(this Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();

    /// <summary>Maps success through <paramref name="onSuccess"/>, and failure to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : result.Error.ToProblem();

    /// <summary>Maps success to <c>204 No Content</c>, and failure to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();

    /// <summary>Maps success to <c>201 Created</c> at a fixed <paramref name="location"/>, and failure to <c>ProblemDetails</c>.</summary>
    public static IResult ToCreated<T>(this Result<T> result, string location) =>
        result.IsSuccess ? TypedResults.Created(location, result.Value) : result.Error.ToProblem();

    /// <summary>
    /// Maps success to <c>201 Created</c> at a location built from the created value, and failure to
    /// <c>ProblemDetails</c>. The identifier usually only exists once the operation has succeeded, which
    /// is why the location is a function of the value rather than a string known up front.
    /// </summary>
    /// <example>
    /// <code>
    /// orders.MapPost("/", (CreateOrderRequest request, IOrderService service) =&gt;
    ///     service.Create(request).ToCreated(order =&gt; $"/orders/{order.Id}"));
    /// </code>
    /// </example>
    public static IResult ToCreated<T>(this Result<T> result, Func<T, string> location) =>
        result.IsSuccess ? TypedResults.Created(location(result.Value), result.Value) : result.Error.ToProblem();

    /// <summary>
    /// The <see cref="Uri"/> twin of <see cref="ToCreated{T}(Result{T}, Func{T, string})"/>. Its own
    /// name rather than an overload, because a lambda that throws or returns a target-typed expression
    /// would otherwise be ambiguous between the string and Uri shapes.
    /// </summary>
    public static IResult ToCreatedAtUri<T>(this Result<T> result, Func<T, Uri> location) =>
        result.IsSuccess ? TypedResults.Created(location(result.Value), result.Value) : result.Error.ToProblem();

    /// <summary>
    /// Maps success to <c>201 Created</c> pointing at a named endpoint, and failure to <c>ProblemDetails</c>.
    /// Prefer this over a hand-built path when the route already has a name: the URL then survives a change
    /// to the route template.
    /// </summary>
    /// <param name="result">The result to map.</param>
    /// <param name="routeName">The <c>WithName(...)</c> of the endpoint the location should point at.</param>
    /// <param name="routeValues">
    /// Route values built from the created value. A <see cref="RouteValueDictionary"/> rather than an
    /// anonymous object, because reading an anonymous object's properties needs reflection and would cost
    /// this package its native-AOT guarantee.
    /// </param>
    /// <example>
    /// <code>
    /// orders.MapGet("/{id:guid}", GetById).WithName("GetOrder");
    ///
    /// orders.MapPost("/", (CreateOrderRequest request, IOrderService service) =&gt;
    ///     service.Create(request).ToCreatedAtRoute("GetOrder", order =&gt; new() { ["id"] = order.Id }));
    /// </code>
    /// </example>
    public static IResult ToCreatedAtRoute<T>(this Result<T> result, string routeName, Func<T, RouteValueDictionary> routeValues) =>
        result.IsSuccess
            ? TypedResults.CreatedAtRoute(result.Value, routeName, routeValues(result.Value))
            : result.Error.ToProblem();

    /// <inheritdoc cref="ToCreatedAtRoute{T}(Result{T}, string, Func{T, RouteValueDictionary})"/>
    public static IResult ToCreatedAtRoute<T>(this Result<T> result, string routeName) =>
        result.IsSuccess
            // The null is typed so this binds to the RouteValueDictionary overload; the object one
            // reflects over its argument and would drop the AOT guarantee.
            ? TypedResults.CreatedAtRoute(result.Value, routeName, (RouteValueDictionary?)null)
            : result.Error.ToProblem();

    /// <inheritdoc cref="ToCreated{T}(Result{T}, Func{T, string})"/>
    public static async Task<IResult> ToCreated<T>(this Task<Result<T>> result, Func<T, string> location) =>
        (await result.ConfigureAwait(false)).ToCreated(location);

    /// <inheritdoc cref="ToCreated{T}(Result{T}, string)"/>
    public static async Task<IResult> ToCreated<T>(this Task<Result<T>> result, string location) =>
        (await result.ConfigureAwait(false)).ToCreated(location);

    /// <inheritdoc cref="ToCreatedAtRoute{T}(Result{T}, string, Func{T, RouteValueDictionary})"/>
    public static async Task<IResult> ToCreatedAtRoute<T>(this Task<Result<T>> result, string routeName, Func<T, RouteValueDictionary> routeValues) =>
        (await result.ConfigureAwait(false)).ToCreatedAtRoute(routeName, routeValues);

    /// <inheritdoc cref="ToHttpResult{T}(Result{T})"/>
    public static async Task<IResult> ToHttpResult<T>(this Task<Result<T>> result) =>
        (await result.ConfigureAwait(false)).ToHttpResult();

    /// <inheritdoc cref="ToHttpResult{T}(Result{T}, Func{T, IResult})"/>
    public static async Task<IResult> ToHttpResult<T>(this Task<Result<T>> result, Func<T, IResult> onSuccess) =>
        (await result.ConfigureAwait(false)).ToHttpResult(onSuccess);

    /// <inheritdoc cref="ToHttpResult(Result)"/>
    public static async Task<IResult> ToHttpResult(this Task<Result> result) =>
        (await result.ConfigureAwait(false)).ToHttpResult();

    /// <inheritdoc cref="ToHttpResult{T}(Result{T})"/>
    public static async ValueTask<IResult> ToHttpResult<T>(this ValueTask<Result<T>> result) =>
        (await result.ConfigureAwait(false)).ToHttpResult();

    /// <inheritdoc cref="ToHttpResult(Result)"/>
    public static async ValueTask<IResult> ToHttpResult(this ValueTask<Result> result) =>
        (await result.ConfigureAwait(false)).ToHttpResult();
}

