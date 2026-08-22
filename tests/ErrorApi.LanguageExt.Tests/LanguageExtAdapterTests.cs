using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using ErrorApi.Interop;
using Xunit;

namespace ErrorApi.Generator.Tests;

public sealed class LanguageExtAdapterTests
{
    private const string Source = """
        using System;
        using ErrorApi;
        using LanguageExt;
        using LanguageExt.Common;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;

        namespace Shop;

        [ErrorApi.Error("Orders.NotFound", 404, Title = "Order not found")]
        public sealed record OrderNotFound(Guid Id) : Expected("Order not found", 404);

        public sealed record Order(Guid Id);

        public interface IOrderService
        {
            Fin<Order> GetById(Guid id);
        }

        public sealed class OrderService : IOrderService
        {
            public Fin<Order> GetById(Guid id) =>
                id == Guid.Empty ? new OrderNotFound(id) : new Order(id);
        }

        public static class Endpoints
        {
            public static void Map(IEndpointRouteBuilder app) =>
                app.MapGet("/orders/{id:guid}", (Guid id, IOrderService service) =>
                    ErrorApi.Interop.LanguageExtHttpExtensions.ToHttpResult(service.GetById(id)));
        }
        """;

    [Fact]
    public void An_annotated_Expected_subclass_reaches_the_endpoint_contract()
    {
        var metadata = GeneratorHarness.RunAndCompile(Source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[0] })",
            metadata,
            StringComparison.Ordinal);
        Assert.Contains("global::Shop.OrderNotFound => _errors[0],", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unrecognized_error_keeps_its_numeric_code_when_it_is_an_http_status()
    {
        var mapped = LanguageExt.Common.Error.New(418, "I am a teapot").ToErrorApiError(new FakeMetadata());

        Assert.Equal(418, mapped.StatusCode);
        Assert.Equal("I am a teapot", mapped.Detail);
    }

    [Fact]
    public void An_error_without_an_http_code_becomes_a_500()
    {
        var mapped = LanguageExt.Common.Error.New(7, "internal").ToErrorApiError(new FakeMetadata());

        Assert.Equal(500, mapped.StatusCode);
    }
}

public sealed class LanguageExtCreatedTests
{
    private sealed record Order(Guid Id);

    [Fact]
    public void The_location_is_built_from_the_created_value()
    {
        LanguageExt.Fin<Order> result = new Order(Guid.Empty);

        var created = Assert.IsType<Created<Order>>(result.ToCreated(o => $"/orders/{o.Id}"));

        Assert.Equal("/orders/00000000-0000-0000-0000-000000000000", created.Location);
    }

    [Fact]
    public void A_named_route_carries_the_route_values_from_the_value()
    {
        LanguageExt.Fin<Order> result = new Order(Guid.Empty);

        var created = Assert.IsType<CreatedAtRoute<Order>>(
            result.ToCreatedAtRoute("GetOrder", o => new RouteValueDictionary { ["id"] = o.Id }));

        Assert.Equal("GetOrder", created.RouteName);
    }

    [Fact]
    public void A_failure_produces_a_problem_instead()
    {
        LanguageExt.Fin<Order> result = LanguageExt.Common.Error.New(418, "I am a teapot");

        var problem = Assert.IsType<ProblemHttpResult>(result.ToCreated(o => $"/orders/{o.Id}"));

        Assert.Equal(418, problem.StatusCode);
    }
}
