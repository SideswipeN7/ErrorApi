#if NET10_0_OR_GREATER
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace ErrorApi.AspNetCore;

/// <summary>
/// Fills in the error half of every operation. The set of codes per endpoint is decided at compile
/// time by the source generator, so the document says exactly which failures a client has to handle
/// instead of stopping at "200 OK". Rides the built-in OpenAPI pipeline, which writes to
/// Microsoft.OpenApi 2.x from .NET 10 — on earlier frameworks the identical responses come from the
/// <c>ErrorApi.Swashbuckle</c> operation filter instead.
/// </summary>
public sealed class ErrorApiOperationTransformer : IOpenApiOperationTransformer
{
    /// <summary>The media type every error response is documented with.</summary>
    public const string ProblemMediaType = Shared.ErrorResponseBuilder.ProblemMediaType;

    /// <inheritdoc />
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.ApplicationServices.GetService<IErrorApiMetadata>();
        if (metadata is null)
        {
            return Task.CompletedTask;
        }

        var route = RoutePattern.Normalize(context.Description.RelativePath);
        var method = context.Description.HttpMethod ?? "*";

        if (!metadata.TryGetEndpointErrors(method, route, context.Description.GroupName, out var errors) || errors.Count == 0)
        {
            return Task.CompletedTask;
        }

        operation.Responses ??= new OpenApiResponses();

        Shared.ErrorResponseBuilder.WriteResponses(errors, (status, response) => operation.Responses[status] = response);

        return Task.CompletedTask;
    }
}

/// <summary>Hooks ErrorApi into an OpenAPI document.</summary>
public static class ErrorApiOpenApiExtensions
{
    /// <summary>
    /// Documents every error the generator discovered for each endpoint. Calling
    /// <c>AddErrorApi()</c> already does this for all documents; use this overload to opt one
    /// document in explicitly.
    /// </summary>
    public static OpenApiOptions AddErrorResponses(this OpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AddOperationTransformer(new ErrorApiOperationTransformer());
        return options;
    }
}
#endif
