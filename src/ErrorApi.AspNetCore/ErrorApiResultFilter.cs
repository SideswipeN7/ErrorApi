using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

namespace ErrorApi.AspNetCore;

/// <summary>
/// Lets a handler return a <see cref="Result"/> or <see cref="Result{T}"/> <em>directly</em> —
/// <c>return await sender.Send(new GetOrder(id));</c> — with no <c>ToHttpResult()</c> at the call
/// site. C# forbids user-defined conversions to an interface, so a result can never implicitly become
/// an <see cref="IResult"/>; this endpoint filter is the sanctioned way to the same ergonomics: it
/// maps the returned result after the handler runs (success → <c>200</c>/<c>204</c>, failure →
/// <c>application/problem+json</c> with the code), exactly as <c>ToHttpResult()</c> would.
/// </summary>
public sealed class ErrorApiResultFilter : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        Map(await next(context).ConfigureAwait(false));

    /// <summary>The mapping, shaped exactly like <c>ToHttpResult()</c>.</summary>
    internal static object? Map(object? value) =>
        value is IErrorApiResult result
            ? result.IsSuccess
                ? result.HasValue ? Results.Ok(result.ValueOrNull) : TypedResults.NoContent()
                : result.Error.ToProblem()
            : value;
}

/// <summary>Wires <see cref="ErrorApiResultFilter"/> onto endpoints.</summary>
public static class ErrorApiResultFilterExtensions
{
    private static readonly ErrorApiResultFilter Filter = new();

    /// <summary>
    /// Lets every endpoint under <paramref name="builder"/> return <see cref="Result"/> /
    /// <see cref="Result{T}"/> directly, without <c>ToHttpResult()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things happen: the filter maps the returned result at runtime, and the endpoint's response
    /// metadata is rewritten so the document describes <c>T</c> (or <c>204</c>) instead of the result
    /// wrapper — the generator's error responses stay exactly as they were, because the walk reads the
    /// handler body either way.
    /// </para>
    /// <para>
    /// The success value is serialized from its runtime type, which is the one part of ErrorApi that
    /// native AOT cannot see through statically — under trimming or AOT, prefer the explicit
    /// <c>ToHttpResult()</c> / <c>ToTypedResult()</c> mapping.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var orders = app.MapGroup("/orders").AddErrorApiResults();
    ///
    /// orders.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
    ///     await sender.Send(new GetOrder(id)));   // Result&lt;Order&gt; — mapped by the filter
    /// </code>
    /// </example>
    public static TBuilder AddErrorApiResults<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(Filter);

        builder.Add(endpoint =>
        {
            var handler = endpoint.Metadata.OfType<MethodInfo>().FirstOrDefault();
            if (handler is null || Unwrap(handler.ReturnType) is not { } returned)
            {
                return;
            }

            // The default metadata described the raw Result<T> as the 200 body; replace it with what
            // the filter actually writes.
            foreach (var stale in endpoint.Metadata
                         .OfType<IProducesResponseTypeMetadata>()
                         .Where(m => m.Type is { } type && IsErrorApiResult(type))
                         .ToList())
            {
                endpoint.Metadata.Remove(stale);
            }

            endpoint.Metadata.Add(returned == typeof(Result)
                ? new SuccessMetadata(StatusCodes.Status204NoContent, null, [])
                : new SuccessMetadata(StatusCodes.Status200OK, returned.GetGenericArguments()[0], ["application/json"]));
        });

        return builder;
    }

    /// <summary>The handler's result type with <c>Task</c>/<c>ValueTask</c> peeled off, when it is one of ours.</summary>
    private static Type? Unwrap(Type returnType)
    {
        var type = returnType;
        if (type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            type = type.GetGenericArguments()[0];
        }

        return IsErrorApiResult(type) ? type : null;
    }

    private static bool IsErrorApiResult(Type type) =>
        type == typeof(Result) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>));

    // Our own record rather than ProducesResponseTypeMetadata, so the shape is identical on every
    // supported framework regardless of that type's constructor surface.
    private sealed record SuccessMetadata(int StatusCode, Type? Type, string[] ContentTypes) : IProducesResponseTypeMetadata
    {
        Type? IProducesResponseTypeMetadata.Type => Type;

        int IProducesResponseTypeMetadata.StatusCode => StatusCode;

        System.Collections.Generic.IEnumerable<string> IProducesResponseTypeMetadata.ContentTypes => ContentTypes;
    }
}
