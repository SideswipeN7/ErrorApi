using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

// Ardalis.Result and ErrorApi both export a type called Result, and Ardalis also has its own IResult;
// inside this namespace the bare names bind to ours, so the Ardalis side is spelled out or aliased.
// An alias cannot cover the open generic, so Result<TValue> is written out with global::.
using ArdalisResult = Ardalis.Result.Result;
using IArdalisResult = Ardalis.Result.IResult;
using ResultStatus = Ardalis.Result.ResultStatus;

namespace ErrorApi.Interop;

/// <summary>
/// Bridges <see href="https://github.com/ardalis/Result">Ardalis.Result</see> onto ErrorApi.
/// </summary>
/// <remarks>
/// <para>
/// Ardalis models the failure as a <see cref="ResultStatus"/> plus message strings — there is no typed
/// error and no code slot of its own. The way to give a failure an identity the document can promise is
/// a catalog of factory members, with the code carried where Ardalis has room for it: an error message,
/// or <c>ValidationError.ErrorCode</c>.
/// </para>
/// <code>
/// [ErrorCatalog("Orders")]
/// public static class OrderErrors
/// {
///     [ErrorApi.Error("Orders.NotFound", 404, Title = "Order not found")]
///     public static Ardalis.Result.Result NotFound() => Ardalis.Result.Result.NotFound("Orders.NotFound");
///
///     [ErrorApi.Error("Orders.InvalidCustomer", 400)]
///     public static Ardalis.Result.Result InvalidCustomer() => Ardalis.Result.Result.Invalid(
///         new Ardalis.Result.ValidationError { ErrorCode = "Orders.InvalidCustomer", ErrorMessage = "Customer must not be empty." });
/// }
/// </code>
/// <para>
/// The generator documents the endpoints that reach those factories; at runtime the code is looked up in
/// the generated catalog, so status and title come from the same declaration the document was built
/// from. A result built without a catalog code still answers — status from
/// <see cref="ResultStatus"/>, the status's name as the code — but that is a weaker contract, and the
/// point of the catalog is not to need it.
/// </para>
/// </remarks>
public static class ArdalisResultHttpExtensions
{
    /// <summary>
    /// Resolves an Ardalis result's failure against the generated catalog: a
    /// <c>ValidationError.ErrorCode</c> the catalog knows wins, then an error message that is a known
    /// code, and failing both the <see cref="ResultStatus"/> supplies the status and its name the code.
    /// </summary>
    /// <param name="result">A failed Ardalis result.</param>
    /// <param name="metadata">The model to resolve against. Defaults to the one <c>AddErrorApi()</c> registered.</param>
    public static ErrorApi.Error ToErrorApiError(this IArdalisResult result, IErrorApiMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var model = metadata ?? ErrorApiRuntime.Metadata;

        var validation = result.ValidationErrors.FirstOrDefault(v => !string.IsNullOrEmpty(v.ErrorCode));
        var descriptor = validation is null ? null : model?.FindError(validation.ErrorCode);
        if (descriptor is not null)
        {
            var detail = string.IsNullOrEmpty(validation!.ErrorMessage) ? descriptor.Detail : validation.ErrorMessage;
            return new ErrorApi.Error(descriptor.Code, descriptor.StatusCode, descriptor.Title, detail);
        }

        foreach (var message in result.Errors)
        {
            descriptor = model?.FindError(message);
            if (descriptor is not null)
            {
                return descriptor.ToError();
            }
        }

        // No catalog identity: the status carries what Ardalis actually knows.
        var status = StatusFor(result.Status);
        var fallbackDetail = result.ValidationErrors.FirstOrDefault()?.ErrorMessage ?? result.Errors.FirstOrDefault();
        return new ErrorApi.Error(result.Status.ToString(), status, title: null, detail: fallbackDetail);
    }

    /// <summary>Maps a failed Ardalis result onto <c>application/problem+json</c>.</summary>
    public static IResult ToProblem(this IArdalisResult result) => result.ToErrorApiError().ToProblem();

    /// <summary>
    /// Maps an Ardalis result onto a Minimal API result: <c>Ok</c>/<c>Created</c>/<c>NoContent</c>
    /// statuses keep their Ardalis meaning, every failure status becomes <c>ProblemDetails</c>.
    /// </summary>
    public static IResult ToHttpResult<TValue>(this global::Ardalis.Result.Result<TValue> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Status switch
        {
            ResultStatus.Ok => TypedResults.Ok(result.Value),
            ResultStatus.Created => TypedResults.Created(NullIfEmpty(result.Location), result.Value),
            ResultStatus.NoContent => TypedResults.NoContent(),
            _ => result.ToProblem(),
        };
    }

    /// <summary>Maps success through <paramref name="onSuccess"/> and failure to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult<TValue>(this global::Ardalis.Result.Result<TValue> result, Func<TValue, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return result.IsSuccess ? onSuccess(result.Value) : result.ToProblem();
    }

    /// <summary>Maps a non-generic result: success to <c>204 No Content</c>, failure to <c>ProblemDetails</c>.</summary>
    public static IResult ToHttpResult(this ArdalisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Status switch
        {
            ResultStatus.Ok or ResultStatus.NoContent => TypedResults.NoContent(),
            ResultStatus.Created => TypedResults.Created(NullIfEmpty(result.Location)),
            _ => result.ToProblem(),
        };
    }

    /// <inheritdoc cref="ToHttpResult{TValue}(global::Ardalis.Result.Result{TValue})"/>
    public static async Task<IResult> ToHttpResult<TValue>(this Task<global::Ardalis.Result.Result<TValue>> result) =>
        (await result.ConfigureAwait(false)).ToHttpResult();

    /// <inheritdoc cref="ToHttpResult(ArdalisResult)"/>
    public static async Task<IResult> ToHttpResult(this Task<ArdalisResult> result) =>
        (await result.ConfigureAwait(false)).ToHttpResult();

    /// <summary>
    /// The HTTP status each failure <see cref="ResultStatus"/> maps to — the same mapping
    /// <c>Ardalis.Result.AspNetCore</c> applies, so nothing surprises an Ardalis user: <c>Invalid</c>
    /// is 400, <c>Error</c> is a 422 business failure, <c>CriticalError</c> is the 500.
    /// </summary>
    public static int StatusFor(ResultStatus status) => status switch
    {
        ResultStatus.Invalid => StatusCodes.Status400BadRequest,
        ResultStatus.Unauthorized => StatusCodes.Status401Unauthorized,
        ResultStatus.Forbidden => StatusCodes.Status403Forbidden,
        ResultStatus.NotFound => StatusCodes.Status404NotFound,
        ResultStatus.Conflict => StatusCodes.Status409Conflict,
        ResultStatus.Error => StatusCodes.Status422UnprocessableEntity,
        ResultStatus.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
}

