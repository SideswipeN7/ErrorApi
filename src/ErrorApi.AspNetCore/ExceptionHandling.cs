using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace ErrorApi.AspNetCore;

/// <summary>
/// The <c>HandleExceptions(h =&gt; ...)</c> block: what happens to a thrown exception, written in the
/// order it happens. The catalog answers first and needs no configuring. Then the handlers added here,
/// in the order they were added — the first one to answer with something other than
/// <see cref="Error.None"/> decides. Then <see cref="MapUnhandledException(Error)"/>, or nothing.
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddErrorApi(x => x.HandleExceptions(h => h
///     .Add&lt;SqlException, SqlExceptionHandler&gt;()                         // by instance data, with DI
///     .Add(e => e is OperationCanceledException ? ApiErrors.Cancelled : Error.None)
///     .MapUnhandledException()));                                       // nothing escapes as a bare 500
/// </code>
/// </example>
/// <remarks>
/// Order is the whole configuration, so keep it readable top to bottom. A handler for a base type
/// placed before one for a derived type is not an error — it may pass most instances on — but it is
/// the first to be asked, and that is what the order says.
/// </remarks>
public sealed class ExceptionHandlingBuilder
{
    private readonly List<Action<IServiceCollection>> _registrations = [];
    private Action<ErrorApiExceptionOptions>? _tuning;
    private bool _fallbackSet;

    internal ExceptionHandlingBuilder()
    {
    }

    /// <summary>
    /// Whether an annotated exception's <see cref="Exception.Message"/> becomes <c>ProblemDetails.detail</c>
    /// when its catalog entry carries none. On by default; see
    /// <see cref="ErrorApiExceptionOptions.UseExceptionMessageAsDetail"/> for when to turn it off.
    /// </summary>
    /// <param name="enabled">Pass <see langword="false"/> to keep every message off the wire.</param>
    public ExceptionHandlingBuilder UseExceptionMessageAsDetail(bool enabled = true) =>
        Tune(o => o.UseExceptionMessageAsDetail = enabled);

    /// <summary>Adds a handler that is asked about every exception the catalog does not know.</summary>
    /// <typeparam name="THandler">The handler, resolved from the container as a singleton.</typeparam>
    public ExceptionHandlingBuilder Add<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>()
        where THandler : class, IGlobalExceptionHandler
    {
        _registrations.Add(static services => services.AddSingleton<IGlobalExceptionHandler, THandler>());
        return this;
    }

    /// <summary>
    /// Adds a handler that is asked about exceptions of one type (derived types included); every
    /// other exception skips it.
    /// </summary>
    /// <typeparam name="TException">The exception type the handler answers for.</typeparam>
    /// <typeparam name="THandler">The handler, resolved from the container as a singleton.</typeparam>
    public ExceptionHandlingBuilder Add<TException, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>()
        where TException : Exception
        where THandler : class, IGlobalExceptionHandler<TException>
    {
        _registrations.Add(static services =>
        {
            services.AddSingleton<THandler>();
            services.AddSingleton<IGlobalExceptionHandler>(
                provider => new TypedGlobalExceptionHandler<TException>(provider.GetRequiredService<THandler>()));
        });
        return this;
    }

    /// <summary>The one-line form: a rule with no dependencies, written in place.</summary>
    /// <param name="map">Returns the entry to answer with, or <see cref="Error.None"/> to pass the exception on.</param>
    public ExceptionHandlingBuilder Add(Func<Exception, Error> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var handler = new DelegateGlobalExceptionHandler(map);
        _registrations.Add(services => services.AddSingleton<IGlobalExceptionHandler>(handler));
        return this;
    }

    /// <summary>
    /// Answers whatever the catalog and every handler passed on with <paramref name="error"/> — the
    /// block form of <see cref="ErrorApiExceptionOptions.MapUnhandledException(Error)"/>, documented
    /// on every operation the same way.
    /// </summary>
    /// <param name="error">The catalog entry to answer with.</param>
    public ExceptionHandlingBuilder MapUnhandledException(Error error)
    {
        ClaimFallback();

        // Forwarded through the internal setter, not the public MapUnhandledException: the generator
        // runs on this assembly too, and a MapUnhandledException(error) call whose argument is a
        // parameter would read as an undocumentable fallback (EAPI014) in ErrorApi's own build.
        return Tune(o => o.SetUnhandledError(error));
    }

    /// <summary>
    /// Answers whatever the catalog and every handler passed on with the built-in
    /// <see cref="ErrorApiExceptionOptions.DefaultUnhandledError"/>.
    /// </summary>
    public ExceptionHandlingBuilder MapUnhandledException()
    {
        ClaimFallback();
        return Tune(o => o.SetUnhandledError(ErrorApiExceptionOptions.DefaultUnhandledError));
    }

    /// <summary>Composes a raw tuning of the options — what the obsolete <c>AddExceptionHandler(o =&gt; ...)</c> forwards.</summary>
    internal ExceptionHandlingBuilder Tune(Action<ErrorApiExceptionOptions> configure)
    {
        var existing = _tuning;
        _tuning = existing is null
            ? configure
            : options =>
            {
                existing(options);
                configure(options);
            };
        return this;
    }

    /// <summary>Registers the handler, its options and every handler added here — in order.</summary>
    internal void Apply(IServiceCollection services)
    {
        services.AddErrorApiExceptionHandler(_tuning);

        foreach (var register in _registrations)
        {
            register(services);
        }
    }

    private void ClaimFallback()
    {
        if (_fallbackSet)
        {
            throw new InvalidOperationException("MapUnhandledException may be called once; the fallback is one entry, not a chain.");
        }

        _fallbackSet = true;
    }
}

/// <summary>Wraps a typed handler so the pipeline can ask it like any other; the type test is here, once.</summary>
internal sealed class TypedGlobalExceptionHandler<TException>(IGlobalExceptionHandler<TException> inner) : IGlobalExceptionHandler
    where TException : Exception
{
    public Error Map(Exception exception) => exception is TException typed ? inner.Map(typed) : Error.None;
}

/// <summary>The lambda form of a handler.</summary>
internal sealed class DelegateGlobalExceptionHandler(Func<Exception, Error> map) : IGlobalExceptionHandler
{
    public Error Map(Exception exception) => map(exception);
}
