using System;

namespace ErrorApi;

/// <summary>
/// Overrides the HTTP status of one catalog entry, beating whatever the entry would otherwise get —
/// the catalog's default, a status inferred from the base constructor, or the <c>[Error]</c>
/// argument itself. The most specific declaration wins, always.
/// </summary>
/// <example>
/// <code>
/// [ErrorCatalog("Order.Validation", 422)]
/// public static partial class ValidationErrors
/// {
///     [Error] public static partial Error InvalidOrder { get; }                       // 422
///     [Error, ErrorStatusCode(400)] public static partial Error MissingId { get; }    // 400
/// }
/// </code>
/// </example>
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Method | AttributeTargets.Field
    | AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = false,
    Inherited = false)]
public sealed class ErrorStatusCodeAttribute : Attribute
{
    /// <param name="statusCode">HTTP status code this entry maps to.</param>
    public ErrorStatusCodeAttribute(int statusCode) => StatusCode = statusCode;

    /// <summary>HTTP status code this entry maps to.</summary>
    public int StatusCode { get; }
}

/// <summary>
/// Overrides the documentation prose of one catalog entry — the same value as
/// <c>[Error(Description = ...)]</c>, as its own attribute so an entry that inherits everything else
/// can still carry one line of documentation.
/// </summary>
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Method | AttributeTargets.Field
    | AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = false,
    Inherited = false)]
public sealed class ErrorDescriptionAttribute : Attribute
{
    /// <param name="description">Longer prose for documentation output.</param>
    public ErrorDescriptionAttribute(string description) => Description = description;

    /// <summary>Longer prose for documentation output.</summary>
    public string Description { get; }
}
