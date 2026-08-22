using System;

namespace ErrorApi;

/// <summary>
/// The compile-time error model of the running application, published here by <c>AddErrorApi()</c>.
/// </summary>
/// <remarks>
/// Adapter packages need the model inside plain extension methods such as <c>result.ToHttpResult()</c>,
/// where there is no service provider to ask. One process hosts one set of endpoints, so a single
/// ambient model is the honest shape; every adapter also exposes an overload taking the model explicitly.
/// </remarks>
public static class ErrorApiRuntime
{
    /// <summary>The registered model, or <see langword="null"/> before <c>AddErrorApi()</c> has run.</summary>
    public static IErrorApiMetadata? Metadata { get; set; }

    /// <summary>The registered model.</summary>
    /// <exception cref="InvalidOperationException">No model has been registered.</exception>
    public static IErrorApiMetadata Current =>
        Metadata ?? throw new InvalidOperationException(
            "No ErrorApi model is registered. Call builder.Services.AddErrorApi() during start-up.");

    /// <summary>
    /// Resolves an error object of any shape into an <see cref="Error"/>: first through the type map,
    /// then through the code, falling back to a 500 when the catalog has never seen it.
    /// </summary>
    public static Error Resolve(object? instance, string? code = null, string? fallbackTitle = null, int fallbackStatus = 500)
    {
        var metadata = Metadata;

        var descriptor = instance is null ? null : metadata?.FindErrorForInstance(instance);
        if (descriptor is null && code is not null)
        {
            descriptor = metadata?.FindError(code);
        }

        if (descriptor is not null)
        {
            return descriptor.ToError();
        }

        return new Error(code ?? instance?.GetType().Name ?? "Unknown", fallbackStatus, fallbackTitle);
    }
}
