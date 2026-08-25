using System;

namespace ErrorApi;

/// <summary>
/// Silences specific ErrorApi diagnostics on one declaration. Generator diagnostics ignore
/// <c>#pragma warning</c>, so without this the only lever is <c>NoWarn</c> — which silences the rule
/// for the whole project, exactly the wrong scope for a deliberate one-off.
/// </summary>
/// <example>
/// <code>
/// [Error(429), SuppressErrorApi("EAPI010")]   // kept for the next release; no endpoint returns it yet
/// public static partial Error RateLimited { get; }
/// </code>
/// </example>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field
    | AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = false,
    Inherited = false)]
public sealed class SuppressErrorApiAttribute : Attribute
{
    /// <param name="diagnosticIds">The diagnostic IDs to silence here, e.g. <c>"EAPI010"</c>.</param>
    public SuppressErrorApiAttribute(params string[] diagnosticIds) => DiagnosticIds = diagnosticIds;

    /// <summary>The silenced diagnostic IDs.</summary>
    public string[] DiagnosticIds { get; }
}
