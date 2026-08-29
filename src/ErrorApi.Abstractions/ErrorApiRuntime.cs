using System;

namespace ErrorApi;

/// <summary>
/// The compile-time error model of the running application, published here by <c>AddErrorApi()</c>.
/// </summary>
/// <remarks>
/// Adapter packages need the model inside plain extension methods such as <c>result.ToHttpResult()</c>,
/// where there is no service provider to ask. One process hosts one set of endpoints, so a single
/// ambient model is the honest shape. Every adapter also exposes an overload taking the model explicitly.
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

    /// <summary>
    /// Swaps the ambient model in and restores the previous one on dispose. This is the test-friendly
    /// way to use the static: a suite that stands up several hosts one after another wraps each in a
    /// scope instead of remembering to null the property in a <c>finally</c>.
    /// </summary>
    /// <remarks>
    /// The model is still one per process — a scope does not make parallel hosts safe. Tests that run
    /// hosts concurrently should pass <see cref="IErrorApiMetadata"/> to the adapter overloads
    /// explicitly instead.
    /// </remarks>
    public static IDisposable Use(IErrorApiMetadata metadata)
    {
        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        var scope = new RestoreScope(Metadata);
        Metadata = metadata;
        return scope;
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly IErrorApiMetadata? _previous;
        private bool _disposed;

        public RestoreScope(IErrorApiMetadata? previous) => _previous = previous;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Metadata = _previous;
            }
        }
    }
}


