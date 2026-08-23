using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

// FluentResults and ErrorApi both export a type called Result, and this namespace sits inside ErrorApi,
// so the FluentResults one is spelled out throughout. An alias cannot cover the open generic.
using IError = FluentResults.IError;
using ResultBase = FluentResults.ResultBase;

namespace ErrorApi.Interop;

/// <summary>
/// Bridges <see href="https://github.com/altmann/FluentResults">FluentResults</see> onto ErrorApi.
/// </summary>
/// <remarks>
/// <para>
/// <c>Result.Fail("message")</c> carries neither a code nor a status, so there is nothing to read from
/// it. The way to give a FluentResults failure an identity is the one the library already recommends:
/// model it as its own <c>Error</c> subclass. That type is then the catalog entry.
/// </para>
/// <code>
/// [ErrorCatalog("Orders")]
/// public static class OrderErrors
/// {
///     [ErrorApi.Error(404)]
///     public sealed class NotFound(Guid id) : FluentResults.Error($"No order {id}.");
/// }
///
/// public Result&lt;Order&gt; GetById(Guid id) =&gt;
///     _orders.TryGetValue(id, out var order) ? Result.Ok(order) : Result.Fail(new OrderErrors.NotFound(id));
///
/// orders.MapGet("/{id:guid}", (Guid id, IOrderService s) =&gt; s.GetById(id).ToHttpResult());
/// </code>
/// <para>
/// A failure built with a bare message still answers, as a 500 carrying the message — deliberately
/// unhelpful as a contract, because a message is not one.
/// </para>
/// </remarks>
public static class FluentResultsHttpExtensions
{
    /// <summary>The metadata key a FluentResults error can carry its wire code in.</summary>
    public const string CodeMetadataKey = "code";

    /// <summary>
    /// Whether a result carrying more than one error adds an <c>errors</c> member listing the rest.
    /// </summary>
    /// <remarks>
    /// Off by default, because the documented schema says <c>code</c> and <c>status</c> and this adds a
    /// member that is in neither — and "what the document promises is what the client gets" is the point
    /// of this project. Turn it on where accumulated validation failures matter more than that.
    /// </remarks>
    public static bool IncludeAllErrors { get; set; }

    /// <summary>
    /// Resolves a FluentResults error against the generated catalog: by its type first, then by a
    /// <c>code</c> metadata entry, and failing both as a 500 carrying its message.
    /// </summary>
    /// <param name="error">The FluentResults error.</param>
    /// <param name="metadata">The model to resolve against. Defaults to the one <c>AddErrorApi()</c> registered.</param>
    public static ErrorApi.Error ToErrorApiError(this IError error, IErrorApiMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        var model = metadata ?? ErrorApiRuntime.Metadata;
        var descriptor = model?.FindErrorForInstance(error);

        if (descriptor is null
            && error.Metadata.TryGetValue(CodeMetadataKey, out var declared)
            && declared is string code)
        {
            descriptor = model?.FindError(code);
        }

        if (descriptor is null)
        {
            return new ErrorApi.Error(error.GetType().Name, StatusCodes.Status500InternalServerError, title: null, detail: error.Message);
        }

        return new ErrorApi.Error(
            descriptor.Code,
            descriptor.StatusCode,
            descriptor.Title,
            string.IsNullOrEmpty(error.Message) ? descriptor.Detail : error.Message);
    }

    /// <summary>
    /// Resolves the failure a result answers with. The first error decides the status and the code,
    /// which is what keeps the response matching the document that listed them.
    /// </summary>
    /// <param name="result">A failed result.</param>
    /// <param name="metadata">The model to resolve against. Defaults to the one <c>AddErrorApi()</c> registered.</param>
    public static ErrorApi.Error ToErrorApiError(this ResultBase result, IErrorApiMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var errors = result.Errors;
        if (errors.Count == 0)
        {
            return new ErrorApi.Error("Unknown", StatusCodes.Status500InternalServerError);
        }

        var first = errors[0].ToErrorApiError(metadata);

        if (!IncludeAllErrors || errors.Count == 1)
        {
            return first;
        }

        var rest = errors
            .Skip(1)
            .Select(e => e.ToErrorApiError(metadata))
            .Select(e => new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = e.Code, ["detail"] = e.Detail })
            .ToArray();

        return first.WithExtension("errors", rest);
    }

    /// <summary>Maps a failed result onto <c>application/problem+json</c>.</summary>
    public static IResult ToProblem(this ResultBase result) => result.ToErrorApiError().ToProblem();

    /// <summary>Maps success to <c>200 OK</c> and failure to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult<TValue>(this global::FluentResults.Result<TValue> result) =>
        result.IsFailed ? result.ToProblem() : TypedResults.Ok(result.Value);

    /// <summary>Maps success through <paramref name="onSuccess"/> and failure to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult<TValue>(this global::FluentResults.Result<TValue> result, Func<TValue, IResult> onSuccess) =>
        result.IsFailed ? result.ToProblem() : onSuccess(result.Value);

    /// <summary>Maps success to <c>204 No Content</c> and failure to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult(this global::FluentResults.Result result) =>
        result.IsFailed ? result.ToProblem() : TypedResults.NoContent();

    /// <summary>Maps success to <c>204 No Content</c> and failure to <c>ProblemDetails</c>.</summary>
    public static IResult ToNoContentResult<TValue>(this global::FluentResults.Result<TValue> result) =>
        result.IsFailed ? result.ToProblem() : TypedResults.NoContent();

    /// <summary>Maps success to <c>201 Created</c> at a fixed location, and failure to <c>ProblemDetails</c>.</summary>
    public static IResult ToCreated<TValue>(this global::FluentResults.Result<TValue> result, string location) =>
        result.IsFailed ? result.ToProblem() : TypedResults.Created(location, result.Value);

    /// <summary>
    /// Maps success to <c>201 Created</c> at a location built from the created value, and failure to
    /// <c>ProblemDetails</c>.
    /// </summary>
    public static IResult ToCreated<TValue>(this global::FluentResults.Result<TValue> result, Func<TValue, string> location) =>
        result.IsFailed ? result.ToProblem() : TypedResults.Created(location(result.Value), result.Value);

    /// <inheritdoc cref="ToCreated{TValue}(FluentResults.Result{TValue}, Func{TValue, string})"/>
    public static IResult ToCreated<TValue>(this global::FluentResults.Result<TValue> result, Func<TValue, Uri> location) =>
        result.IsFailed ? result.ToProblem() : TypedResults.Created(location(result.Value), result.Value);

    /// <summary>
    /// Maps success to <c>201 Created</c> pointing at a named endpoint, and failure to
    /// <c>ProblemDetails</c>. Route values are a <see cref="RouteValueDictionary"/> rather than an
    /// anonymous object, because reading an anonymous object needs reflection.
    /// </summary>
    public static IResult ToCreatedAtRoute<TValue>(
        this global::FluentResults.Result<TValue> result, string routeName, Func<TValue, RouteValueDictionary> routeValues) =>
        result.IsFailed
            ? result.ToProblem()
            : TypedResults.CreatedAtRoute(result.Value, routeName, routeValues(result.Value));

    /// <inheritdoc cref="ToHttpResult{TValue}(FluentResults.Result{TValue})"/>
    public static async Task<IResult> ToHttpResult<TValue>(this Task<global::FluentResults.Result<TValue>> result) =>
        (await result.ConfigureAwait(false)).ToHttpResult();

    /// <inheritdoc cref="ToHttpResult{TValue}(FluentResults.Result{TValue}, Func{TValue, IResult})"/>
    public static async Task<IResult> ToHttpResult<TValue>(this Task<global::FluentResults.Result<TValue>> result, Func<TValue, IResult> onSuccess) =>
        (await result.ConfigureAwait(false)).ToHttpResult(onSuccess);

    /// <inheritdoc cref="ToHttpResult(FluentResults.Result)"/>
    public static async Task<IResult> ToHttpResult(this Task<global::FluentResults.Result> result) =>
        (await result.ConfigureAwait(false)).ToHttpResult();

    /// <inheritdoc cref="ToCreated{TValue}(FluentResults.Result{TValue}, Func{TValue, string})"/>
    public static async Task<IResult> ToCreated<TValue>(this Task<global::FluentResults.Result<TValue>> result, Func<TValue, string> location) =>
        (await result.ConfigureAwait(false)).ToCreated(location);

    /// <inheritdoc cref="ToCreatedAtRoute{TValue}(FluentResults.Result{TValue}, string, Func{TValue, RouteValueDictionary})"/>
    public static async Task<IResult> ToCreatedAtRoute<TValue>(
        this Task<global::FluentResults.Result<TValue>> result, string routeName, Func<TValue, RouteValueDictionary> routeValues) =>
        (await result.ConfigureAwait(false)).ToCreatedAtRoute(routeName, routeValues);
}
