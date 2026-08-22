using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// The catalog does not have to be written in ErrorApi's own <c>Error</c> type. These tests pin the
/// shapes the adapter packages rely on: <c>[Error]</c> on a type, on a field, and on a member the
/// caller implements with another library's error value.
/// </summary>
public sealed class DeclarativeCatalogTests
{
    private const string UnionSource = """
        using System;
        using ErrorApi;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;

        namespace Shop;

        [Error("Orders.NotFound", 404, Title = "Order not found")]
        public sealed record OrderNotFound(Guid Id);

        [Error("Orders.AlreadyPaid", 409, Title = "Order already paid")]
        public sealed record OrderAlreadyPaid(Guid Id);

        public abstract record OrderResult;

        public interface IOrderService
        {
            object Pay(Guid id);
        }

        public sealed class OrderService : IOrderService
        {
            public object Pay(Guid id)
            {
                if (id == Guid.Empty)
                {
                    return new OrderNotFound(id);
                }

                return new OrderAlreadyPaid(id);
            }
        }

        public static class Endpoints
        {
            public static void Map(IEndpointRouteBuilder app) =>
                app.MapPost("/orders/{id:guid}/pay", (Guid id, IOrderService service) => Results.Ok(service.Pay(id)));
        }
        """;

    [Fact]
    public void An_error_declared_on_a_type_is_discovered_where_it_is_constructed()
    {
        var metadata = GeneratorHarness.RunAndCompile(UnionSource).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"POST\", \"/orders/{id}/pay\", "
            + "new global::ErrorApi.ErrorDescriptor[] { _errors[0], _errors[1] })",
            metadata,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Error_types_become_a_pattern_switch_so_adapters_need_no_reflection()
    {
        var metadata = GeneratorHarness.RunAndCompile(UnionSource).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("public global::ErrorApi.ErrorDescriptor? FindErrorForInstance(object? instance) => instance switch", metadata, StringComparison.Ordinal);
        Assert.Contains("global::Shop.OrderAlreadyPaid => _errors[0],", metadata, StringComparison.Ordinal);
        Assert.Contains("global::Shop.OrderNotFound => _errors[1],", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_is_generated_for_a_declarative_catalog()
    {
        var output = GeneratorHarness.RunAndCompile(UnionSource);

        Assert.DoesNotContain(output.Sources.Keys, name => name.EndsWith(".Catalog.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void An_abstract_error_type_is_rejected_because_it_cannot_identify_one_failure()
    {
        const string source = """
            using ErrorApi;

            [Error("Orders.Any", 400)]
            public abstract record AnyFailure;
            """;

        var diagnostic = Assert.Single(GeneratorHarness.Run(source).GeneratorDiagnostics, d => d.Id == "EAPI003");

        Assert.Contains("abstract", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_error_declared_on_a_field_is_discovered_where_it_is_read()
    {
        const string source = """
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            public static class Catalog
            {
                [Error("Things.Gone", 410, Title = "Gone")]
                public static readonly Error Gone = new("Things.Gone", 410, "Gone");
            }

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/things", () => Catalog.Gone.ToProblem());
            }
            """;

        var metadata = GeneratorHarness.RunAndCompile(source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("new global::ErrorApi.EndpointErrors(\"GET\", \"/things\", new global::ErrorApi.ErrorDescriptor[] { _errors[0] })", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void A_catalog_written_in_another_libraries_error_type_still_reaches_the_contract()
    {
        // Stands in for ErrorOr, FluentResults or language-ext: the catalog member returns their type,
        // implements itself, and only the [Error] attribute ties it to the documented contract.
        const string source = """
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            namespace Foreign
            {
                public readonly struct ForeignError(string code, string description)
                {
                    public string Code { get; } = code;
                    public string Description { get; } = description;
                }
            }

            namespace Shop
            {
                using Foreign;

                public static class OrderErrors
                {
                    [Error("Orders.NotFound", 404, Title = "Order not found")]
                    public static ForeignError NotFound => new("Orders.NotFound", "Order not found");
                }

                public static class Endpoints
                {
                    public static void Map(IEndpointRouteBuilder app) =>
                        app.MapGet("/orders/{id}", (string id) => Results.BadRequest(OrderErrors.NotFound.Code));
                }
            }
            """;

        var metadata = GeneratorHarness.RunAndCompile(source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("\"Orders.NotFound\", 404, \"Order not found\"", metadata, StringComparison.Ordinal);
        Assert.Contains("new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\",", metadata, StringComparison.Ordinal);
    }
}
