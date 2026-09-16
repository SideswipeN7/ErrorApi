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
    /// Whether an annotated exception's <see cref="System.Exception.Message"/> becomes
    /// <c>ProblemDetails.detail</c> when its catalog entry does not carry one. On by default: a type you
    /// annotated with <c>[Error]</c> is one whose message you wrote, and an annotated exception's message is
    /// almost always composed for the caller. Turn it off when that is not true of your types — a message
    /// that quotes connection strings, paths or inner-exception text — or when a mapped foreign type
    /// (<c>[assembly: ErrorMapping(typeof(SomeLibraryException), ...)]</c>) carries a message you do not
    /// control. The entry's <c>Detail</c> always wins, and it is the documented place for client-facing
    /// text. Never applies to the unhandled-exception fallback.
    /// </summary>
    public bool UseExceptionMessageAsDetail { get; set; } = true;

    /// <summary>
    /// The entry every exception the catalog does not know is answered with, or <see langword="null"/>
    /// to leave such exceptions to whatever handled them before — the default. Set through
    /// <see cref="MapUnhandledException(Error)"/> or its parameterless form.
    /// </summary>
    public Error? UnhandledError { get; private set; }

    /// <summary>
    /// The entry the parameterless <see cref="MapUnhandledException()"/> answers with:
    /// <c>Server.Unhandled</c>, status 500. The generator carries the same constants, so the built-in
    /// entry is documented exactly like one of yours.
    /// </summary>
    public static readonly Error DefaultUnhandledError =
        new(Shared.UnhandledDefaults.Code, Shared.UnhandledDefaults.StatusCode, Shared.UnhandledDefaults.Title);

    /// <summary>
    /// The no-catalog form of <see cref="MapUnhandledException(Error)"/>: answers every exception the
    /// catalog does not know with the built-in <see cref="DefaultUnhandledError"/>. Reach for the other
    /// overload when the API has a name and a description of its own for "something went wrong".
    /// </summary>
    public ErrorApiExceptionOptions MapUnhandledException() => MapUnhandledException(DefaultUnhandledError);

    /// <summary>
    /// Answers every exception the catalog does not know with <paramref name="error"/>, so nothing
    /// escapes as an undocumented 500. Pass a catalog member — <c>ApiErrors.Failed</c> — and the
    /// generator lists it on every operation and in the TypeScript contract, because a fallback is
    /// reachable from everywhere by definition; a value built at runtime still answers, but is
    /// documented nowhere and <c>EAPI014</c> says so. The exception's message never becomes
    /// <c>detail</c> here, whatever <see cref="UseExceptionMessageAsDetail"/> says: an unknown
    /// exception is exactly the one nobody composed for a client.
    /// </summary>
    /// <param name="error">The catalog entry to answer with.</param>
    /// <exception cref="ArgumentException"><paramref name="error"/> is <see cref="Error.None"/>.</exception>
    /// <exception cref="InvalidOperationException">A fallback was already set; there is one, not a chain.</exception>
    public ErrorApiExceptionOptions MapUnhandledException(Error error)
    {
        SetUnhandledError(error);
        return this;
    }

    /// <summary>The setter behind both <c>MapUnhandledException</c> forms, here and on the block builder.</summary>
    internal void SetUnhandledError(Error error)
    {
        if (error.IsNone)
        {
            throw new ArgumentException("The fallback must be a catalog entry, not Error.None.", nameof(error));
        }

        if (UnhandledError is not null)
        {
            throw new InvalidOperationException("MapUnhandledException may be called once; the fallback is one entry, not a chain.");
        }

        UnhandledError = error;
    }
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
/// An exception the catalog does not know goes to the global handlers registered through
/// <c>HandleExceptions(h =&gt; h.Add(...))</c>, in order, and then to the fallback set with
/// <c>MapUnhandledException</c>; with neither it is left alone, so whatever handled it before still
/// does. The response body always comes from the same <c>Error.ToProblem()</c> the result path uses,
/// which is what keeps the styles indistinguishable to a client.
/// </para>
/// </remarks>
public sealed class ErrorApiExceptionHandler : IExceptionHandler
{
    private readonly IErrorApiMetadata _metadata;
    private readonly ErrorApiExceptionOptions _options;
    private readonly IGlobalExceptionHandler[] _handlers;

    /// <param name="metadata">The compile-time error model, registered by <c>AddErrorApi()</c>.</param>
    /// <param name="options">How the response is built.</param>
    /// <param name="handlers">
    /// The global handlers, asked in registration order about every exception the catalog does not
    /// know — what <c>HandleExceptions(h =&gt; h.Add(...))</c> registered.
    /// </param>
    public ErrorApiExceptionHandler(
        IErrorApiMetadata metadata,
        IOptions<ErrorApiExceptionOptions> options,
        IEnumerable<IGlobalExceptionHandler>? handlers = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);

        _metadata = metadata;
        _options = options.Value;
        _handlers = handlers?.ToArray() ?? [];
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (!exception.TryGetCatalogError(out var error, _metadata))
        {
            // The catalog answered nothing: the global handlers, in order, then the fallback.
            foreach (var handler in _handlers)
            {
                var mapped = handler.Map(exception);
                if (!mapped.IsNone)
                {
                    await mapped.ToProblem().ExecuteAsync(httpContext).ConfigureAwait(false);
                    return true;
                }
            }

            if (_options.UnhandledError is not { } fallback)
            {
                return false;
            }

            // Never the message: an unknown exception is the one nobody composed for a client.
            await fallback.ToProblem().ExecuteAsync(httpContext).ConfigureAwait(false);
            return true;
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
    /// <para>
    /// Deliberately not folded into <c>AddErrorApi()</c>: taking over an application's exception handling
    /// is not something a call named "add error api" should do behind your back.
    /// </para>
    /// <para>
    /// ASP.NET Core runs <see cref="IExceptionHandler"/>s in registration order, and this one sits
    /// beside any the application registers itself. With <see cref="ErrorApiExceptionOptions.MapUnhandledException(Error)"/>
    /// set it answers everything that reaches it, so register your own handlers before it.
    /// </para>
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

        // Enumerable, not TryAddSingleton: that one checks the service type alone, so an application with
        // an IExceptionHandler of its own would silently never get this one.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionHandler, ErrorApiExceptionHandler>());
        services.AddOptions<ErrorApiExceptionOptions>();

        if (configure is not null)
        {
            services.Configure(configure);
        }

        return services;
    }
}
