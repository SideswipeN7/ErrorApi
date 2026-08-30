using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace ErrorApi.Shared;

/// <summary>
/// Builds the error half of an operation — the response object, its problem schema with the
/// <c>code</c> enum, and one example per code — against Microsoft.OpenApi 2.x. Linked source, shared
/// by the built-in-pipeline transformer (net10) and the Swashbuckle operation filter (all TFMs), so
/// both document generators produce the identical contract.
/// </summary>
internal static class ErrorResponseBuilder
{
    /// <summary>The media type every error response is documented with.</summary>
    public const string ProblemMediaType = "application/problem+json";

    public static OpenApiResponse BuildResponse(int status, IReadOnlyList<ErrorDescriptor> errors) => new()
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
    public static string BuildDescription(int status, IReadOnlyList<ErrorDescriptor> errors)
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

    public static IOpenApiSchema BuildSchema(int status, IReadOnlyList<ErrorDescriptor> errors)
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
                ["errors"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Description = "Secondary failures accompanying the primary one, when the server attaches them.",
                    Items = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
                        {
                            ["code"] = new OpenApiSchema { Type = JsonSchemaType.String },
                            ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String },
                        },
                        Required = new HashSet<string>(StringComparer.Ordinal) { "code" },
                    },
                },
            },
            Required = new HashSet<string>(StringComparer.Ordinal) { "status", "code" },
        };
    }

    public static Dictionary<string, IOpenApiExample> BuildExamples(int status, IReadOnlyList<ErrorDescriptor> errors)
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

    /// <summary>Groups an endpoint's errors by status, writing one response per status via <paramref name="write"/>.</summary>
    public static void WriteResponses(IReadOnlyList<ErrorDescriptor> errors, Action<string, OpenApiResponse> write)
    {
        foreach (var group in errors.GroupBy(e => e.StatusCode).OrderBy(g => g.Key))
        {
            var status = group.Key.ToString(CultureInfo.InvariantCulture);
            write(status, BuildResponse(group.Key, [.. group.OrderBy(e => e.Code, StringComparer.Ordinal)]));
        }
    }

    private static string Escape(string value) => value.Replace("|", "\\|").Replace("\n", " ");

    public static string ReasonPhrase(int status) => status switch
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
