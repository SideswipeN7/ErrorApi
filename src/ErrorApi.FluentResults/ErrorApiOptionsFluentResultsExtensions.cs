using ErrorApi.AspNetCore;
using ErrorApi.Interop;

namespace ErrorApi;

/// <summary>
/// FluentResults' knob, reachable from the one <c>AddErrorApi(x =&gt; ...)</c> call — adapters extend
/// the same options lambda instead of asking for a second configuration site.
/// </summary>
public static class ErrorApiOptionsFluentResultsExtensions
{
    /// <summary>
    /// Attaches the secondary failures of a multi-error result as the documented optional
    /// <c>errors</c> member — the lambda-form of
    /// <see cref="FluentResultsHttpExtensions.IncludeAllErrors"/>. The first error still decides the
    /// status and the code, which is what keeps the response matching the document.
    /// </summary>
    public static ErrorApiOptions IncludeAllFluentResultErrors(this ErrorApiOptions options, bool isEnabled = true)
    {
        FluentResultsHttpExtensions.IncludeAllErrors = isEnabled;
        return options;
    }
}
