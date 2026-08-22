using System;

namespace ErrorApi;

/// <summary>
/// Declares one entry of the error catalog.
/// <para>
/// On a <c>static partial</c> property or method returning <see cref="Error"/> the generator writes the
/// implementation for you. On anything else — a type, a field, a member you implement yourself — the
/// declaration is recorded but not implemented, which is how a catalog written in another library's
/// error type (ErrorOr, OneOf, language-ext, a hand-rolled union) still reaches OpenAPI.
/// </para>
/// </summary>
/// <example>
/// <code>
/// public static partial class OrderErrors
/// {
///     [Error("Orders.NotFound", 404, Title = "Order not found")]
///     public static partial Error NotFound { get; }
///
///     [Error("Orders.AlreadyPaid", 409, Detail = "Order {0} was already paid.")]
///     public static partial Error AlreadyPaid(Guid orderId);
/// }
///
/// // On a type, when the failure is modelled as its own case in a union.
/// [Error("Orders.Cancelled", 410, Title = "Order was cancelled")]
/// public sealed record OrderCancelled(Guid OrderId);
/// </code>
/// </example>
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Method | AttributeTargets.Field
    | AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = false,
    Inherited = false)]
public sealed class ErrorAttribute : Attribute
{
    /// <param name="code">Stable machine-readable code, e.g. <c>Orders.NotFound</c>.</param>
    /// <param name="statusCode">HTTP status code this error maps to.</param>
    public ErrorAttribute(string code, int statusCode)
    {
        Code = code;
        StatusCode = statusCode;
    }

    /// <summary>Stable machine-readable code, e.g. <c>Orders.NotFound</c>.</summary>
    public string Code { get; }

    /// <summary>HTTP status code this error maps to.</summary>
    public int StatusCode { get; }

    /// <summary>Short human-readable summary, emitted as <c>ProblemDetails.title</c>.</summary>
    public string? Title { get; set; }

    /// <summary>
    /// Explanation emitted as <c>ProblemDetails.detail</c>. On a catalog method the value is a
    /// <see cref="string.Format(string, object[])"/> template whose placeholders bind to the method parameters in order.
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>Longer prose used for the OpenAPI response description and the generated TypeScript doc comment.</summary>
    public string? Description { get; set; }
}
