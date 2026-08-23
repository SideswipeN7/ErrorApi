using System;

namespace ErrorApi;

/// <summary>
/// Adds a catalog entry for a type you do not own, so a failure coming out of a referenced package can
/// be documented and answered like any of your own.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ErrorAttribute"/> has to sit on the declaration, which rules out anything from a NuGet
/// package. This says the same thing from the outside: the type is the identity, and the attribute
/// supplies the wire code and the status it has no way to carry.
/// </para>
/// <para>
/// The mapping puts the entry in the catalog and makes it resolvable by type at runtime. Attaching it to
/// a particular endpoint is a separate question: the generator can only follow what your code does, so
/// an error a library raises on its own still needs <see cref="ProducesErrorAttribute"/> on the endpoints
/// that surface it. Where your own code constructs the type, the walk finds it without help.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [assembly: ErrorMapping(typeof(StripeCardError), "Payments.CardDeclined", 402, Title = "Card declined")]
/// [assembly: ErrorMapping(typeof(RateLimitedException), 429)]   // code inferred: "RateLimited"
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class ErrorMappingAttribute : Attribute
{
    /// <summary>Maps a type to an entry whose wire code is inferred from the type's name.</summary>
    /// <param name="errorType">The type identifying the failure.</param>
    /// <param name="statusCode">HTTP status code this error maps to.</param>
    public ErrorMappingAttribute(Type errorType, int statusCode)
    {
        ErrorType = errorType;
        StatusCode = statusCode;
    }

    /// <summary>Maps a type to an entry with an explicit wire code.</summary>
    /// <param name="errorType">The type identifying the failure.</param>
    /// <param name="code">Stable machine-readable code, e.g. <c>Payments.CardDeclined</c>.</param>
    /// <param name="statusCode">HTTP status code this error maps to.</param>
    public ErrorMappingAttribute(Type errorType, string code, int statusCode)
    {
        ErrorType = errorType;
        Code = code;
        StatusCode = statusCode;
    }

    /// <summary>The type identifying the failure.</summary>
    public Type ErrorType { get; }

    /// <summary>Stable machine-readable code, or <see langword="null"/> when it is left to be inferred.</summary>
    public string? Code { get; }

    /// <summary>HTTP status code this error maps to.</summary>
    public int StatusCode { get; }

    /// <summary>Short human-readable summary, emitted as <c>ProblemDetails.title</c>.</summary>
    public string? Title { get; set; }

    /// <summary>Explanation emitted as <c>ProblemDetails.detail</c>.</summary>
    public string? Detail { get; set; }

    /// <summary>Longer prose used for the OpenAPI response description and the TypeScript doc comment.</summary>
    public string? Description { get; set; }
}
