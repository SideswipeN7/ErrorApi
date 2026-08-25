using System;

namespace ErrorApi;

/// <summary>
/// Declares that a method can surface an error the generator cannot see by walking source —
/// typically one raised inside a referenced assembly or behind a delegate.
/// The declared codes are merged into the discovered set for every endpoint that reaches this method.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class ProducesErrorAttribute : Attribute
{
    /// <param name="code">A code declared elsewhere with <see cref="ErrorAttribute"/>.</param>
    public ProducesErrorAttribute(string code) => Code = code;

    /// <summary>
    /// Declares the failure by its type instead of its code: an <c>[Error]</c>-annotated type, or one
    /// mapped with <c>[assembly: ErrorMapping]</c>. The natural form for an exception a library throws —
    /// <c>[ProducesError(typeof(StripeException))]</c> reads as what it is, and survives a rename.
    /// </summary>
    /// <param name="errorType">The type identifying the failure.</param>
    public ProducesErrorAttribute(Type errorType) => ErrorType = errorType;

    /// <summary>The declared error code, when declared by code.</summary>
    public string? Code { get; }

    /// <summary>The type identifying the failure, when declared by type.</summary>
    public Type? ErrorType { get; }
}
