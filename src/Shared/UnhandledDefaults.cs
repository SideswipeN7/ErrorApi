namespace ErrorApi.Shared;

/// <summary>
/// The built-in entry the parameterless <c>MapUnhandledException()</c> answers with when the
/// application names none of its own. Compiled into <c>ErrorApi.AspNetCore</c> (the response) and
/// <c>ErrorApi.Generator</c> (the documentation) as a linked source, so the wire and the document
/// cannot drift.
/// </summary>
internal static class UnhandledDefaults
{
    public const string Code = "Server.Unhandled";

    public const int StatusCode = 500;

    public const string Title = "Unexpected server error";

    public const string Description =
        "The request failed for a reason the server did not anticipate. Retry later, or quote the traceId to support.";

    public const string DeclaringMember = "ErrorApi.AspNetCore.ErrorApiExceptionOptions.MapUnhandledException";
}
