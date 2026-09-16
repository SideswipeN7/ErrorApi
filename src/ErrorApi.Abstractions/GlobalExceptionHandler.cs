using System;

namespace ErrorApi;

/// <summary>
/// Maps an exception the catalog does not know onto a catalog entry, by whatever the instance carries —
/// a driver's error number, the status on a client exception. Handlers are registered in order inside
/// <c>AddErrorApi(x =&gt; x.HandleExceptions(h =&gt; h.Add&lt;T&gt;()))</c>; an annotated exception never
/// reaches one, and the first handler that answers with something other than <see cref="Error.None"/>
/// decides. What every handler passes on goes to <c>MapUnhandledException</c>, or stays untouched.
/// </summary>
/// <remarks>
/// Return catalog entries — <c>DbErrors.Duplicate</c> — rather than values built on the spot: the
/// generator walks <see cref="Map"/> and documents what it reads there on every endpoint, because a
/// global handler is reachable from everywhere by definition.
/// </remarks>
public interface IGlobalExceptionHandler
{
    /// <summary>Maps <paramref name="exception"/> to a catalog entry, or <see cref="Error.None"/> to pass it on.</summary>
    /// <param name="exception">The thrown exception, already known not to be in the catalog.</param>
    Error Map(Exception exception);
}

/// <summary>
/// The typed form of <see cref="IGlobalExceptionHandler"/>: ErrorApi does the type test, and the handler
/// receives the exception it asked for. Derived types match too.
/// </summary>
/// <typeparam name="TException">The exception type this handler answers for.</typeparam>
public interface IGlobalExceptionHandler<in TException> where TException : Exception
{
    /// <summary>Maps <paramref name="exception"/> to a catalog entry, or <see cref="Error.None"/> to pass it on.</summary>
    /// <param name="exception">The thrown exception, already known not to be in the catalog.</param>
    Error Map(TException exception);
}
