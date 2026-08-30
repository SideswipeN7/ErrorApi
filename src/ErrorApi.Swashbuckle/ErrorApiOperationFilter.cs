using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ErrorApi.Swashbuckle;

/// <summary>
/// Fills in the error half of every operation for Swagger/Swashbuckle documents — the same responses
/// the built-in OpenAPI transformer writes, built by the same shared code, so the two document
/// pipelines can never disagree. This is the road to ErrorApi documents on net8/net9 (where the
/// built-in pipeline predates Microsoft.OpenApi 2.x), and for any project that stays on Swashbuckle.
/// </summary>
public sealed class ErrorApiOperationFilter : IOperationFilter
{
    private readonly IErrorApiMetadata? _metadata;

    /// <summary>Resolves the model from DI — the one <c>AddErrorApi()</c> registered.</summary>
    public ErrorApiOperationFilter(IServiceProvider services) =>
        _metadata = services.GetService<IErrorApiMetadata>();

    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (_metadata is null || context.ApiDescription is not { } description)
        {
            return;
        }

        var route = RoutePattern.Normalize(description.RelativePath);
        var method = description.HttpMethod ?? "*";

        if (!_metadata.TryGetEndpointErrors(method, route, description.GroupName, out var errors) || errors.Count == 0)
        {
            return;
        }

        operation.Responses ??= new OpenApiResponses();

        Shared.ErrorResponseBuilder.WriteResponses(errors, (status, response) => operation.Responses[status] = response);
    }
}

/// <summary>Hooks ErrorApi into a Swashbuckle document.</summary>
public static class ErrorApiSwaggerGenExtensions
{
    /// <summary>
    /// Documents every error the generator discovered for each endpoint:
    /// <c>services.AddSwaggerGen(c =&gt; c.AddErrorApiResponses());</c> next to your
    /// <c>AddErrorApi()</c> call.
    /// </summary>
    public static SwaggerGenOptions AddErrorApiResponses(this SwaggerGenOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        options.OperationFilter<ErrorApiOperationFilter>();
        return options;
    }
}
