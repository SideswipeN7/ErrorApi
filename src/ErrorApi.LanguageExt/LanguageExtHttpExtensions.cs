using System.Threading.Tasks;
using LanguageExt;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

// Both libraries call their type `Error`, and this namespace sits inside `ErrorApi`, so the
// language-ext one is spelled out explicitly throughout.
using LangError = LanguageExt.Common.Error;

namespace ErrorApi.Interop;

/// <summary>
/// Bridges <see href="https://github.com/louthy/language-ext">language-ext</see> onto ErrorApi.
/// </summary>
/// <remarks>
/// <para>
/// A language-ext error carries a numeric code and a message, neither of which says anything about
/// HTTP. The catalog supplies the missing half. Annotate your own <c>Expected</c> subclasses, which is
/// the idiomatic way to model a domain failure there:
/// </para>
/// <code>
/// [ErrorApi.Error("Orders.NotFound", 404, Title = "Order not found")]
/// public sealed record OrderNotFound(Guid Id) : Expected("Order not found", 404);
///
/// public Fin&lt;Order&gt; GetById(Guid id) =&gt;
///     _orders.TryGetValue(id, out var order) ? order : new OrderNotFound(id);
///
/// orders.MapGet("/{id:guid}", (Guid id, IOrderService s) =&gt; s.GetById(id).ToHttpResult());
/// </code>
/// <para>
/// Resolution goes by type first, through the generated pattern switch. Failing that the numeric code
/// is used when it already looks like an HTTP status, and everything else becomes a 500.
/// </para>
/// </remarks>
public static class LanguageExtHttpExtensions
{
    /// <summary>Resolves a language-ext error against the generated catalog.</summary>
    /// <param name="error">The language-ext error.</param>
    /// <param name="metadata">The model to resolve against. Defaults to the one <c>AddErrorApi()</c> registered.</param>
    public static ErrorApi.Error ToErrorApiError(this LangError error, IErrorApiMetadata? metadata = null)
    {
        var descriptor = (metadata ?? ErrorApiRuntime.Metadata)?.FindErrorForInstance(error);

        if (descriptor is not null)
        {
            return new ErrorApi.Error(
                descriptor.Code,
                descriptor.StatusCode,
                descriptor.Title,
                string.IsNullOrEmpty(error.Message) ? descriptor.Detail : error.Message);
        }

        var status = error.Code is >= 100 and <= 599 ? error.Code : StatusCodes.Status500InternalServerError;
        // No catalog entry: the message is instance-specific, so it belongs in detail and the
        // title is left for the status reason phrase.
        return new ErrorApi.Error(error.GetType().Name, status, title: null, detail: error.Message);
    }

    /// <summary>Maps a language-ext error onto <c>application/problem+json</c>.</summary>
    public static IResult ToProblem(this LangError error) => error.ToErrorApiError().ToProblem();

    /// <summary>Maps <c>Succ</c> to <c>200 OK</c> and <c>Fail</c> to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult<TValue>(this Fin<TValue> result) =>
        result.Match(value => (IResult)TypedResults.Ok(value), error => error.ToProblem());

    /// <summary>Maps <c>Succ</c> through <paramref name="onSuccess"/> and <c>Fail</c> to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult<TValue>(this Fin<TValue> result, Func<TValue, IResult> onSuccess) =>
        result.Match(onSuccess, error => error.ToProblem());

    /// <summary>Maps <c>Succ</c> to <c>204 No Content</c> and <c>Fail</c> to <c>ProblemDetails</c>.</summary>
    public static IResult ToNoContentResult<TValue>(this Fin<TValue> result) =>
        result.Match(_ => (IResult)TypedResults.NoContent(), error => error.ToProblem());

    /// <summary>Maps <c>Succ</c> to <c>201 Created</c> at a fixed location, and <c>Fail</c> to <c>ProblemDetails</c>.</summary>
    public static IResult ToCreated<TValue>(this Fin<TValue> result, string location) =>
        result.Match(value => (IResult)TypedResults.Created(location, value), error => error.ToProblem());

    /// <summary>
    /// Maps <c>Succ</c> to <c>201 Created</c> at a location built from the created value, and <c>Fail</c>
    /// to <c>ProblemDetails</c>. The identifier usually only exists once the operation has succeeded,
    /// which is why the location is a function of the value.
    /// </summary>
    /// <example>
    /// <code>
    /// orders.MapPost("/", (CreateOrderRequest request, IOrderService service) =&gt;
    ///     service.Create(request).ToCreated(order =&gt; $"/orders/{order.Id}"));
    /// </code>
    /// </example>
    public static IResult ToCreated<TValue>(this Fin<TValue> result, Func<TValue, string> location) =>
        result.Match(value => (IResult)TypedResults.Created(location(value), value), error => error.ToProblem());

    /// <summary>The <see cref="Uri"/> twin of the string-location form — its own name, so a throwing lambda is never ambiguous between the two.</summary>
    public static IResult ToCreatedAtUri<TValue>(this Fin<TValue> result, Func<TValue, Uri> location) =>
        result.Match(value => (IResult)TypedResults.Created(location(value), value), error => error.ToProblem());

    /// <summary>
    /// Maps <c>Succ</c> to <c>201 Created</c> pointing at a named endpoint, and <c>Fail</c> to
    /// <c>ProblemDetails</c>. The route values are a <see cref="RouteValueDictionary"/> rather than an
    /// anonymous object, because reading an anonymous object needs reflection.
    /// </summary>
    /// <example>
    /// <code>
    /// orders.MapGet("/{id:guid}", GetById).WithName("GetOrder");
    ///
    /// orders.MapPost("/", (CreateOrderRequest request, IOrderService service) =&gt;
    ///     service.Create(request).ToCreatedAtRoute("GetOrder", order =&gt; new() { ["id"] = order.Id }));
    /// </code>
    /// </example>
    public static IResult ToCreatedAtRoute<TValue>(this Fin<TValue> result, string routeName, Func<TValue, RouteValueDictionary> routeValues) =>
        result.Match(
            value => (IResult)TypedResults.CreatedAtRoute(value, routeName, routeValues(value)),
            error => error.ToProblem());

    /// <inheritdoc cref="ToCreated{TValue}(Fin{TValue}, Func{TValue, string})"/>
    public static async Task<IResult> ToCreated<TValue>(this Task<Fin<TValue>> result, Func<TValue, string> location) =>
        (await result.ConfigureAwait(false)).ToCreated(location);

    /// <inheritdoc cref="ToCreatedAtRoute{TValue}(Fin{TValue}, string, Func{TValue, RouteValueDictionary})"/>
    public static async Task<IResult> ToCreatedAtRoute<TValue>(this Task<Fin<TValue>> result, string routeName, Func<TValue, RouteValueDictionary> routeValues) =>
        (await result.ConfigureAwait(false)).ToCreatedAtRoute(routeName, routeValues);

    /// <summary>Maps <c>Right</c> to <c>200 OK</c> and <c>Left</c> to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult<TValue>(this Either<LangError, TValue> result) =>
        result.Match(value => (IResult)TypedResults.Ok(value), error => error.ToProblem());

    /// <inheritdoc cref="ToHttpResult{TValue}(Fin{TValue})"/>
    public static async Task<IResult> ToHttpResult<TValue>(this Task<Fin<TValue>> result) =>
        (await result.ConfigureAwait(false)).ToHttpResult();

    /// <inheritdoc cref="ToNoContentResult{TValue}(Fin{TValue})"/>
    public static async Task<IResult> ToNoContentResult<TValue>(this Task<Fin<TValue>> result) =>
        (await result.ConfigureAwait(false)).ToNoContentResult();
}
