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

    /// <summary>The declared error code.</summary>
    public string Code { get; }
}
