using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// <c>[ProducesError(typeof(X))]</c>: declaring the failure by its type rather than its code — the
/// natural form for an exception a referenced library throws, where no call in source ever surfaces it.
/// </summary>
public sealed class ProducesErrorTypeTests
{
    [Fact]
    public void A_mapped_library_exception_can_be_declared_by_type()
    {
        const string source = """
            using System;
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            [assembly: ErrorMapping(typeof(TimeoutException), "Payments.GatewayTimeout", 504)]

            namespace Shop;

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapPost("/payments", [ProducesError(typeof(TimeoutException))] () => Results.Ok());
            }
            """;

        var output = GeneratorHarness.RunAndCompile(source);

        Assert.Empty(output.GeneratorDiagnostics);
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"POST\", \"/payments\", new global::ErrorApi.ErrorDescriptor[] { _errors[0] })",
            output.Source("ErrorApi.Metadata.g.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_annotated_type_can_be_declared_by_type_too()
    {
        const string source = """
            using System;
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            namespace Shop;

            [ErrorCatalog("Orders")]
            public static class OrderErrors
            {
                [Error(410)]
                public sealed class GoneException(Guid id) : Exception($"Order {id} is gone.");
            }

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/orders/{id:guid}", [ProducesError(typeof(OrderErrors.GoneException))] (Guid id) => Results.Ok());
            }
            """;

        var output = GeneratorHarness.RunAndCompile(source);

        Assert.Empty(output.GeneratorDiagnostics);
        Assert.Contains("\"Orders.Gone\"", output.Source("ErrorApi.Metadata.g.cs"), StringComparison.Ordinal);
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[0] })",
            output.Source("ErrorApi.Metadata.g.cs"),
            StringComparison.Ordinal);
    }
}
