using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OneOf;

namespace ErrorApi.Interop;

/// <summary>
/// Bridges <see href="https://github.com/mcintyre321/OneOf">OneOf</see> — and any other discriminated
/// union — onto ErrorApi.
/// </summary>
/// <remarks>
/// <para>
/// With a union the failure <em>is</em> a type, so that is where the catalog entry goes. The generator
/// then documents the endpoint wherever it sees the case constructed:
/// </para>
/// <code>
/// [Error("Orders.NotFound", 404, Title = "Order not found")]
/// public sealed record OrderNotFound(Guid Id);
///
/// public OneOf&lt;Order, OrderNotFound&gt; GetById(Guid id) =&gt;
///     _orders.TryGetValue(id, out var order) ? order : new OrderNotFound(id);
///
/// orders.MapGet("/{id:guid}", (Guid id, IOrderService s) =&gt; s.GetById(id).ToHttpResult());
/// </code>
/// <para>
/// The first type argument is the success value; every later one is treated as a failure and resolved
/// through the generated type switch, so no reflection is involved.
/// </para>
/// </remarks>
public static class OneOfHttpExtensions
{
    /// <summary>Maps the success case to <c>200 OK</c> and the failure case to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult<TValue, TError>(this OneOf<TValue, TError> result) =>
        result.Match(value => TypedResults.Ok(value), Problem);

    /// <summary>Maps the success case through <paramref name="onSuccess"/>, the failure case to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult<TValue, TError>(this OneOf<TValue, TError> result, Func<TValue, IResult> onSuccess) =>
        result.Match(onSuccess, Problem);

    /// <summary>Maps the success case to <c>200 OK</c> and either failure case to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult<TValue, TError1, TError2>(this OneOf<TValue, TError1, TError2> result) =>
        result.Match(value => TypedResults.Ok(value), Problem, Problem);

    /// <summary>Maps the success case to <c>200 OK</c> and any failure case to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult<TValue, TError1, TError2, TError3>(this OneOf<TValue, TError1, TError2, TError3> result) =>
        result.Match(value => TypedResults.Ok(value), Problem, Problem, Problem);

    /// <summary>Maps the success case to <c>204 No Content</c> and the failure case to <c>ProblemDetails</c>.</summary>
    public static IResult ToNoContentResult<TValue, TError>(this OneOf<TValue, TError> result) =>
        result.Match(_ => TypedResults.NoContent(), Problem);

    /// <summary>Maps the success case to <c>201 Created</c> at a fixed location, and the failure case to <c>ProblemDetails</c>.</summary>
    public static IResult ToCreated<TValue, TError>(this OneOf<TValue, TError> result, string location) =>
        result.Match(value => TypedResults.Created(location, value), Problem);

    /// <summary>
    /// Maps the success case to <c>201 Created</c> at a location built from the created value, and the
    /// failure case to <c>ProblemDetails</c>. The identifier usually only exists once the operation has
    /// succeeded, which is why the location is a function of the value.
    /// </summary>
    /// <example>
    /// <code>
    /// orders.MapPost("/", (CreateOrderRequest request, IOrderService service) =&gt;
    ///     service.Create(request).ToCreated(order =&gt; $"/orders/{order.Id}"));
    /// </code>
    /// </example>
    public static IResult ToCreated<TValue, TError>(this OneOf<TValue, TError> result, Func<TValue, string> location) =>
        result.Match(value => TypedResults.Created(location(value), value), Problem);

    /// <inheritdoc cref="ToCreated{TValue, TError}(OneOf{TValue, TError}, Func{TValue, string})"/>
    public static IResult ToCreated<TValue, TError>(this OneOf<TValue, TError> result, Func<TValue, Uri> location) =>
        result.Match(value => TypedResults.Created(location(value), value), Problem);

    /// <inheritdoc cref="ToCreated{TValue, TError}(OneOf{TValue, TError}, Func{TValue, string})"/>
    public static IResult ToCreated<TValue, TError1, TError2>(this OneOf<TValue, TError1, TError2> result, Func<TValue, string> location) =>
        result.Match(value => TypedResults.Created(location(value), value), Problem, Problem);

    /// <summary>
    /// Maps the success case to <c>201 Created</c> pointing at a named endpoint, and the failure case to
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
    public static IResult ToCreatedAtRoute<TValue, TError>(
        this OneOf<TValue, TError> result, string routeName, Func<TValue, RouteValueDictionary> routeValues) =>
        result.Match(value => TypedResults.CreatedAtRoute(value, routeName, routeValues(value)), Problem);

    /// <summary>
    /// Resolves any object that carries an <c>[Error]</c> attribute on its type into a problem response.
    /// Use it as the failure branch of a hand-rolled union, where OneOf is not involved at all.
    /// </summary>
    /// <example>
    /// <code>
    /// return outcome switch
    /// {
    ///     Order order =&gt; TypedResults.Ok(order),
    ///     var failure =&gt; OneOfHttpExtensions.Problem(failure),
    /// };
    /// </code>
    /// </example>
    public static IResult Problem<TError>(TError error) =>
        ErrorApiRuntime.Resolve(error, fallbackTitle: typeof(TError).Name).ToProblem();
}
