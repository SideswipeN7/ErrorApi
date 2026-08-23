using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ErrorApi.AspNetCore;

/// <summary>How an annotated exception is turned into a response.</summary>
public sealed class ErrorApiExceptionOptions
{
    /// <summary>
    /// Whether <see cref="System.Exception.Message"/> becomes <c>ProblemDetails.detail</c> when the catalog
    /// entry does not carry one. On by default: only exception types you annotated yourself are handled at
    /// all, so their messages are already written for a client. Turn it off if your domain exceptions carry
    /// text you would rather not put on the wire.
    /// </summary>
    public bool UseExceptionMessageAsDetail { get; set; } = true;
}

/// <summary>
/// Answers a thrown, <c>[Error]</c>-annotated exception with the same problem document its endpoint was
/// documented with.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of ErrorApi for a codebase that never adopted a result type. Annotate the exception,
/// throw it as usual, and the generator documents the endpoints that can reach it while this handler
/// produces the matching response:
/// </para>
/// <code>
/// [Error(404, Title = "Order not found")]
/// public sealed class OrderNotFoundException(Guid id) : Exception($"No order {id}.");
/// </code>
/// <para>
/// An exception the catalog does not know is left alone, so whatever handled it before still does.
/// The response body comes from the same <c>Error.ToProblem()</c> the result path uses, which is what
/// keeps the two indistinguishable to a client.
/// </para>
/// </remarks>
public sealed class ErrorApiExceptionHandler : IExceptionHandler
{
    private readonly IErrorApiMetadata _metadata;
    private readonly ErrorApiExceptionOptions _options;

    /// <param name="metadata">The compile-time error model, registered by <c>AddErrorApi()</c>.</param>
    /// <param name="options">How the response is built.</param>
    public ErrorApiExceptionHandler(IErrorApiMetadata metadata, IOptions<ErrorApiExceptionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);

        _metadata = metadata;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (!exception.TryGetCatalogError(out var error, _metadata))
        {
            return false;
        }

        if (_options.UseExceptionMessageAsDetail && error.Detail is null && !string.IsNullOrEmpty(exception.Message))
        {
            error = error.WithDetail(exception.Message);
        }

        await error.ToProblem().ExecuteAsync(httpContext).ConfigureAwait(false);
        return true;
    }
}

/// <summary>Reads the catalog entry an exception was annotated with.</summary>
public static class ExceptionExtensions
{
    /// <summary>
    /// Resolves a thrown exception to its catalog entry by matching its type against the
    /// <c>[Error]</c>-annotated types of the application. The lookup is a generated pattern switch, so
    /// nothing here reflects over the exception.
    /// </summary>
    /// <param name="exception">The thrown exception.</param>
    /// <param name="error">The catalog entry, when the exception's type carries one.</param>
    /// <param name="metadata">The model to resolve against. Defaults to the one <c>AddErrorApi()</c> registered.</param>
    /// <returns><see langword="true"/> when the exception's type is in the catalog.</returns>
    public static bool TryGetCatalogError(this Exception exception, out Error error, IErrorApiMetadata? metadata = null)
    {
        var descriptor = exception is null ? null : (metadata ?? ErrorApiRuntime.Metadata)?.FindErrorForInstance(exception);

        error = descriptor?.ToError() ?? default;
        return descriptor is not null;
    }
}

/// <summary>Registers the exception handler.</summary>
public static class ErrorApiExceptionRegistration
{
    /// <summary>
    /// Registers <see cref="ErrorApiExceptionHandler"/>. It runs inside the ASP.NET Core exception
    /// handler pipeline, so the app still needs <c>app.UseExceptionHandler()</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately not folded into <c>AddErrorApi()</c>: taking over an application's exception handling
    /// is not something a call named "add error api" should do behind your back.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddErrorApi();
    /// builder.Services.AddErrorApiExceptionHandler();
    ///
    /// var app = builder.Build();
    /// app.UseExceptionHandler();
    /// </code>
    /// </example>
    [SuppressMessage("ApiDesign", "RS0016", Justification = "Extension point for consuming applications.")]
    public static IServiceCollection AddErrorApiExceptionHandler(
        this IServiceCollection services, Action<ErrorApiExceptionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IExceptionHandler, ErrorApiExceptionHandler>();
        services.AddOptions<ErrorApiExceptionOptions>();

        if (configure is not null)
        {
            services.Configure(configure);
        }

        return services;
    }
}
