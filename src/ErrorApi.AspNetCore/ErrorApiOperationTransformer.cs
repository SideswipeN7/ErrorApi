using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace ErrorApi.AspNetCore;

/// <summary>
/// Fills in the error half of every operation. The set of codes per endpoint is decided at compile
/// time by the source generator, so the document says exactly which failures a client has to handle
/// instead of stopping at "200 OK".
/// </summary>
public sealed class ErrorApiOperationTransformer : IOpenApiOperationTransformer
{
    /// <summary>The media type every error response is documented with.</summary>
    public const string ProblemMediaType = "application/problem+json";

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

        if (!metadata.TryGetEndpointErrors(method, route, out var errors) || errors.Count == 0)
        {
            return Task.CompletedTask;
        }

        operation.Responses ??= new OpenApiResponses();

        foreach (var group in errors.GroupBy(e => e.StatusCode).OrderBy(g => g.Key))
        {
            var status = group.Key.ToString(CultureInfo.InvariantCulture);
            operation.Responses[status] = BuildResponse(group.Key, [.. group.OrderBy(e => e.Code, StringComparer.Ordinal)]);
        }

        return Task.CompletedTask;
    }

    private static OpenApiResponse BuildResponse(int status, IReadOnlyList<ErrorDescriptor> errors) => new()
    {
        Description = BuildDescription(status, errors),
        Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
        {
            [ProblemMediaType] = new OpenApiMediaType
            {
                Schema = BuildSchema(status, errors),
                Examples = BuildExamples(status, errors),
            },
        },
    };

    /// <summary>
    /// Leads with a single line naming the codes, then repeats them as a Markdown table. Readers that
    /// render Markdown (Swagger UI) show the table; readers that flatten the description to one line
    /// (Scalar) still show something that reads as a sentence.
    /// </summary>
    private static string BuildDescription(int status, IReadOnlyList<ErrorDescriptor> errors)
    {
        var builder = new StringBuilder();

        builder.Append(ReasonPhrase(status)).Append(" — ")
               .Append(string.Join(", ", errors.Select(e => e.Code)))
               .Append("\n\n");

        builder.Append("| code | title |\n| --- | --- |\n");

        foreach (var error in errors)
        {
            builder.Append("| `").Append(error.Code).Append("` | ")
                   .Append(Escape(error.Description ?? error.Title ?? string.Empty))
                   .Append(" |\n");
        }

        return builder.ToString();
    }

    private static IOpenApiSchema BuildSchema(int status, IReadOnlyList<ErrorDescriptor> errors)
    {
        var codes = new List<JsonNode>(errors.Count);
        foreach (var error in errors)
        {
            codes.Add(JsonValue.Create(error.Code)!);
        }

        return new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Title = "ProblemDetails",
            Description = "RFC 9457 problem document extended with the ErrorApi `code` member.",
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["type"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri" },
                ["title"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["status"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer,
                    Format = "int32",
                    Enum = [JsonValue.Create(status)!],
                },
                ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["instance"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["code"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Description = "Stable machine-readable error code. Switch on this, not on the message.",
                    Enum = codes,
                },
            },
            Required = new HashSet<string>(StringComparer.Ordinal) { "status", "code" },
        };
    }

    private static Dictionary<string, IOpenApiExample> BuildExamples(int status, IReadOnlyList<ErrorDescriptor> errors)
    {
        var examples = new Dictionary<string, IOpenApiExample>(StringComparer.Ordinal);

        foreach (var error in errors)
        {
            var value = new JsonObject
            {
                ["title"] = error.Title ?? ReasonPhrase(status),
                ["status"] = status,
                ["code"] = error.Code,
            };

            if (error.Detail is not null)
            {
                value["detail"] = error.Detail;
            }

            examples[error.Code] = new OpenApiExample
            {
                Summary = error.Title ?? error.Code,
                Description = error.Description,
                Value = value,
            };
        }

        return examples;
    }

    private static string Escape(string value) => value.Replace("|", "\\|").Replace("\n", " ");

    private static string ReasonPhrase(int status) => status switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        409 => "Conflict",
        410 => "Gone",
        412 => "Precondition Failed",
        422 => "Unprocessable Content",
        423 => "Locked",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        501 => "Not Implemented",
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        504 => "Gateway Timeout",
        _ => "Error",
    };
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
