using System.Threading.Tasks;
using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ErrorApi.Interop;

/// <summary>
/// Bridges <see href="https://github.com/amantinband/error-or">ErrorOr</see> onto ErrorApi.
/// </summary>
/// <remarks>
/// <para>
/// Declare the catalog on the members that produce ErrorOr errors and keep returning
/// <c>ErrorOr&lt;T&gt;</c> everywhere else:
/// </para>
/// <code>
/// public static class OrderErrors
/// {
///     [ErrorApi.Error("Orders.NotFound", 404, Title = "Order not found")]
///     public static ErrorOr.Error NotFound => ErrorOr.Error.NotFound("Orders.NotFound", "Order not found");
/// }
///
/// orders.MapGet("/{id:guid}", (Guid id, IOrderService s) =&gt; s.GetById(id).ToHttpResult());
/// </code>
/// <para>
/// The generator sees <c>OrderErrors.NotFound</c> in the call graph and documents 404 on that endpoint;
/// at runtime the code is looked up in the generated catalog, so the status and title come from the same
/// declaration the OpenAPI document was built from.
/// </para>
/// </remarks>
public static class ErrorOrHttpExtensions
{
    /// <summary>
    /// Resolves an ErrorOr error against the generated catalog. A code the catalog knows wins, because
    /// that is the one the OpenAPI document promised; otherwise the mapping falls back to
    /// <see cref="ErrorType"/>, or to a custom numeric type when it is already an HTTP status.
    /// </summary>
    /// <param name="error">The ErrorOr error.</param>
    /// <param name="metadata">The model to resolve against. Defaults to the one <c>AddErrorApi()</c> registered.</param>
    public static ErrorApi.Error ToErrorApiError(this ErrorOr.Error error, IErrorApiMetadata? metadata = null)
    {
        var descriptor = (metadata ?? ErrorApiRuntime.Metadata)?.FindError(error.Code);

        if (descriptor is not null)
        {
            var detail = string.IsNullOrEmpty(error.Description) || error.Description == error.Code
                ? descriptor.Detail
                : error.Description;

            return new ErrorApi.Error(descriptor.Code, descriptor.StatusCode, descriptor.Title, detail);
        }

        // No catalog entry: keep ErrorOr's description as the detail and let the status supply the title.
        return new ErrorApi.Error(error.Code, StatusFor(error), title: null, detail: error.Description);
    }

    /// <summary>Maps an ErrorOr error onto <c>application/problem+json</c>.</summary>
    public static IResult ToProblem(this ErrorOr.Error error) => error.ToErrorApiError().ToProblem();

    /// <summary>Maps success to <c>200 OK</c> and the first error to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult<TValue>(this ErrorOr<TValue> result) =>
        result.IsError ? result.FirstError.ToProblem() : TypedResults.Ok(result.Value);

    /// <summary>Maps success through <paramref name="onSuccess"/> and the first error to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult<TValue>(this ErrorOr<TValue> result, Func<TValue, IResult> onSuccess) =>
        result.IsError ? result.FirstError.ToProblem() : onSuccess(result.Value);

    /// <summary>Maps success to <c>204 No Content</c> and the first error to <c>ProblemDetails</c>.</summary>
    public static IResult ToNoContentResult<TValue>(this ErrorOr<TValue> result) =>
        result.IsError ? result.FirstError.ToProblem() : TypedResults.NoContent();

    /// <summary>Maps success to <c>201 Created</c> at a fixed location, and the first error to <c>ProblemDetails</c>.</summary>
    public static IResult ToCreated<TValue>(this ErrorOr<TValue> result, string location) =>
        result.IsError ? result.FirstError.ToProblem() : TypedResults.Created(location, result.Value);

    /// <summary>
    /// Maps success to <c>201 Created</c> at a location built from the created value, and the first error
    /// to <c>ProblemDetails</c>. The identifier usually only exists once the operation has succeeded,
    /// which is why the location is a function of the value.
    /// </summary>
    /// <example>
    /// <code>
    /// orders.MapPost("/", (CreateOrderRequest request, IOrderService service) =&gt;
    ///     service.Create(request).ToCreated(order =&gt; $"/orders/{order.Id}"));
    /// </code>
    /// </example>
    public static IResult ToCreated<TValue>(this ErrorOr<TValue> result, Func<TValue, string> location) =>
        result.IsError ? result.FirstError.ToProblem() : TypedResults.Created(location(result.Value), result.Value);

    /// <inheritdoc cref="ToCreated{TValue}(ErrorOr{TValue}, Func{TValue, string})"/>
    public static IResult ToCreated<TValue>(this ErrorOr<TValue> result, Func<TValue, Uri> location) =>
        result.IsError ? result.FirstError.ToProblem() : TypedResults.Created(location(result.Value), result.Value);

    /// <summary>
    /// Maps success to <c>201 Created</c> pointing at a named endpoint, and the first error to
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
    public static IResult ToCreatedAtRoute<TValue>(this ErrorOr<TValue> result, string routeName, Func<TValue, RouteValueDictionary> routeValues) =>
        result.IsError
            ? result.FirstError.ToProblem()
            : TypedResults.CreatedAtRoute(result.Value, routeName, routeValues(result.Value));

    /// <inheritdoc cref="ToCreated{TValue}(ErrorOr{TValue}, Func{TValue, string})"/>
    public static async Task<IResult> ToCreated<TValue>(this Task<ErrorOr<TValue>> result, Func<TValue, string> location) =>
        (await result.ConfigureAwait(false)).ToCreated(location);

    /// <inheritdoc cref="ToCreatedAtRoute{TValue}(ErrorOr{TValue}, string, Func{TValue, RouteValueDictionary})"/>
    public static async Task<IResult> ToCreatedAtRoute<TValue>(this Task<ErrorOr<TValue>> result, string routeName, Func<TValue, RouteValueDictionary> routeValues) =>
        (await result.ConfigureAwait(false)).ToCreatedAtRoute(routeName, routeValues);

    /// <inheritdoc cref="ToHttpResult{TValue}(ErrorOr{TValue})"/>
    public static async Task<IResult> ToHttpResult<TValue>(this Task<ErrorOr<TValue>> result) =>
        (await result.ConfigureAwait(false)).ToHttpResult();

    /// <inheritdoc cref="ToHttpResult{TValue}(ErrorOr{TValue}, Func{TValue, IResult})"/>
    public static async Task<IResult> ToHttpResult<TValue>(this Task<ErrorOr<TValue>> result, Func<TValue, IResult> onSuccess) =>
        (await result.ConfigureAwait(false)).ToHttpResult(onSuccess);

    /// <inheritdoc cref="ToNoContentResult{TValue}(ErrorOr{TValue})"/>
    public static async Task<IResult> ToNoContentResult<TValue>(this Task<ErrorOr<TValue>> result) =>
        (await result.ConfigureAwait(false)).ToNoContentResult();

    /// <summary>
    /// The status an ErrorOr error maps to when the catalog has never heard of its code. A custom
    /// numeric type that is already an HTTP status is honoured, which is how <c>Error.Custom(409, …)</c>
    /// is meant to be read.
    /// </summary>
    public static int StatusFor(ErrorOr.Error error) =>
        error.NumericType is >= 100 and <= 599 && !IsBuiltIn(error.Type)
            ? error.NumericType
            : StatusFor(error.Type);

    private static bool IsBuiltIn(ErrorType type) =>
        type is ErrorType.Failure or ErrorType.Unexpected or ErrorType.Validation
            or ErrorType.Conflict or ErrorType.NotFound or ErrorType.Unauthorized or ErrorType.Forbidden;

    /// <summary>The default status for each built-in <see cref="ErrorType"/>.</summary>
    public static int StatusFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError,
    };
}
