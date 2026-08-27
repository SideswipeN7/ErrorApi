using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace ErrorApi;

/// <summary>
/// The controller-flavoured twins of <see cref="ResultHttpExtensions"/>. An action can simply return
/// <c>IResult</c> and use <c>ToHttpResult()</c> — MVC executes those since .NET 7 — but an action
/// written in MVC's own vocabulary (<c>ActionResult&lt;T&gt;</c>) gets the same mapping here, with the
/// identical problem body, so a client cannot tell which style the server chose.
/// </summary>
public static class ActionResultExtensions
{
    /// <summary>Maps a failure onto an <c>application/problem+json</c> <see cref="ObjectResult"/>.</summary>
    public static ObjectResult ToProblemActionResult(this Error error)
    {
        var problem = new ProblemDetails
        {
            Status = error.StatusCode,
            Title = error.Title,
            Detail = error.Detail,
            Type = ResultHttpExtensions.ProblemTypeUriFormat is { } format
                ? string.Format(format, error.Code)
                : null,
        };

        problem.Extensions[ResultHttpExtensions.CodeExtensionName] = error.Code;

        if (error.Extensions is not null)
        {
            foreach (var pair in error.Extensions)
            {
                problem.Extensions[pair.Key] = pair.Value;
            }
        }

        return new ObjectResult(problem)
        {
            StatusCode = error.StatusCode,
            ContentTypes = { "application/problem+json" },
        };
    }

    /// <summary>Maps success to <c>200 OK</c> with the value, and failure to <c>ProblemDetails</c>.</summary>
    public static ActionResult<T> ToActionResult<T>(this Result<T> result) =>
        result.IsSuccess ? result.Value : result.Error.ToProblemActionResult();

    /// <summary>Maps success to <c>204 No Content</c>, and failure to <c>ProblemDetails</c>.</summary>
    public static IActionResult ToActionResult(this Result result) =>
        result.IsSuccess ? new NoContentResult() : result.Error.ToProblemActionResult();

    /// <summary>Maps success to <c>201 Created</c> at a location built from the created value, and failure to <c>ProblemDetails</c>.</summary>
    public static ActionResult<T> ToCreatedActionResult<T>(this Result<T> result, Func<T, string> location) =>
        result.IsSuccess
            ? new CreatedResult(location(result.Value), result.Value)
            : result.Error.ToProblemActionResult();

    /// <inheritdoc cref="ToActionResult{T}(Result{T})"/>
    public static async Task<ActionResult<T>> ToActionResult<T>(this Task<Result<T>> result) =>
        (await result.ConfigureAwait(false)).ToActionResult();

    /// <inheritdoc cref="ToActionResult(Result)"/>
    public static async Task<IActionResult> ToActionResult(this Task<Result> result) =>
        (await result.ConfigureAwait(false)).ToActionResult();
}
