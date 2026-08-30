using ErrorApi.Swashbuckle;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// The Swashbuckle road to the same document: the operation filter writes the identical responses the
/// built-in transformer does, because both compile the one shared response builder. This package is
/// what carries ErrorApi documents on net8/net9 and on any project that stays on Swagger.
/// </summary>
public sealed class SwashbuckleFilterTests
{
    private static OperationFilterContext Context(string method, string relativePath, string? group = null) =>
        new(
            new ApiDescription { HttpMethod = method, RelativePath = relativePath, GroupName = group },
            schemaRegistry: null!,
            schemaRepository: new SchemaRepository(),
            document: new OpenApiDocument(),
            methodInfo: null!);

    private static ErrorApiOperationFilter Filter(IErrorApiMetadata? metadata)
    {
        var services = new ServiceCollection();
        if (metadata is not null)
        {
            services.AddSingleton(metadata);
        }

        return new ErrorApiOperationFilter(services.BuildServiceProvider());
    }

    [Fact]
    public void The_filter_documents_the_endpoints_failures()
    {
        var operation = new OpenApiOperation();

        Filter(new FakeMetadata()).Apply(operation, Context("POST", "orders/{id:guid}/pay"));

        Assert.True(operation.Responses!.TryGetValue("404", out var notFound));
        Assert.True(operation.Responses.TryGetValue("409", out _));

        var media = notFound!.Content!["application/problem+json"];
        Assert.Contains("Orders.NotFound", media.Schema!.Properties!["code"].Enum!.Select(e => e!.ToString()));
        Assert.True(media.Examples!.ContainsKey("Orders.NotFound"));
    }

    [Fact]
    public void An_unknown_route_and_a_missing_model_both_leave_the_operation_untouched()
    {
        var operation = new OpenApiOperation();

        Filter(new FakeMetadata()).Apply(operation, Context("GET", "nothing/here"));
        Filter(metadata: null).Apply(operation, Context("GET", "orders/{id:guid}"));

        Assert.True(operation.Responses is null or { Count: 0 });
    }
}
