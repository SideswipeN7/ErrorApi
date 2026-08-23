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
/// <para>
/// Pass only the status code and the wire code is worked out for you: from a <c>code:</c> argument in
/// the member's own body when there is one, otherwise from the declaration's name. See
/// <see cref="ErrorCatalogAttribute"/> for the prefix, and the repository README for the exact rules.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // The code lives in the body already, so the attribute only adds the status.
/// [ErrorCatalog("Orders")]
/// public static class OrderErrors
/// {
///     [Error(404)]  // -> "Orders.NotFound", title "Not found"
///     public static ErrorOr.Error NotFound => ErrorOr.Error.NotFound("Orders.NotFound", "No such order.");
/// }
///
/// // Spell the code out when it is not derivable, or when you want it to differ from the name.
/// public static partial class BillingErrors
/// {
///     [Error("Billing.CardDeclined", 402, Title = "Card declined")]
///     public static partial Error CardDeclined { get; }
/// }
///
/// // On a type, when the failure is modelled as its own case in a union.
/// [Error(410)]  // -> "Orders.OrderCancelled" under [assembly: ErrorCatalog("Orders")]
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
    /// <summary>
    /// Declares an entry whose wire code is inferred. The generator takes the code from a <c>code:</c>
    /// argument in the member's own body when it finds one, and otherwise from the declaration's name
    /// prefixed by the catalog it belongs to.
    /// </summary>
    /// <param name="statusCode">HTTP status code this error maps to.</param>
    public ErrorAttribute(int statusCode) => StatusCode = statusCode;

    /// <summary>Declares an entry with an explicit wire code.</summary>
    /// <param name="code">Stable machine-readable code, e.g. <c>Orders.NotFound</c>.</param>
    /// <param name="statusCode">HTTP status code this error maps to.</param>
    public ErrorAttribute(string code, int statusCode)
    {
        Code = code;
        StatusCode = statusCode;
    }

    /// <summary>
    /// Stable machine-readable code, e.g. <c>Orders.NotFound</c>, or <see langword="null"/> when it is
    /// left to the generator to infer.
    /// </summary>
    public string? Code { get; }

    /// <summary>HTTP status code this error maps to.</summary>
    public int StatusCode { get; }

    /// <summary>
    /// Short human-readable summary, emitted as <c>ProblemDetails.title</c>. Left unset, the declaration's
    /// name is used: <c>AlreadyPaid</c> becomes <c>Already paid</c>.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Explanation emitted as <c>ProblemDetails.detail</c>. On a catalog method the value is a
    /// <see cref="string.Format(string, object[])"/> template whose placeholders bind to the method parameters in order.
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>Longer prose used for the OpenAPI response description and the generated TypeScript doc comment.</summary>
    public string? Description { get; set; }
}

/// <summary>
/// Sets the prefix that inferred error codes are built from. Apply it to a catalog type, or to the whole
/// assembly when the failures are modelled as their own types.
/// </summary>
/// <remarks>
/// Without it the prefix for a member is its containing type's name with a trailing <c>Errors</c> or
/// <c>Error</c> removed, so <c>OrderErrors.NotFound</c> yields <c>Order.NotFound</c>. An annotated type
/// with no prefix in scope uses its own name unchanged.
/// </remarks>
/// <example>
/// <code>
/// [ErrorCatalog("Orders")]
/// public static class OrderErrors
/// {
///     [Error(404)]  // -> "Orders.NotFound"
///     public static ErrorOr.Error NotFound => ErrorOr.Error.NotFound("Orders.NotFound", "No such order.");
/// }
/// </code>
/// </example>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Assembly,
    AllowMultiple = false,
    Inherited = false)]
public sealed class ErrorCatalogAttribute : Attribute
{
    /// <param name="prefix">The prefix inferred codes are built from, without a trailing separator.</param>
    public ErrorCatalogAttribute(string prefix) => Prefix = prefix;

    /// <summary>The prefix inferred codes are built from.</summary>
    public string Prefix { get; }
}
