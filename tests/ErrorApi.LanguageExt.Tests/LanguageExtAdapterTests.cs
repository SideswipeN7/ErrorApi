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

/// <summary>
/// The runtime mapping over language-ext shapes: Fin both ways, the shaped-success and valueless
/// forms, Either, the Created family and the awaited forms — the typed failure resolving by instance.
/// </summary>
[Collection("ambient-metadata")]
public sealed class LanguageExtMappingTests
{
    private sealed record OrderNotFound(Guid Id) : LanguageExt.Common.Expected("Order not found", 404);

    private static FakeMetadata BuildMetadata()
    {
        var metadata = new FakeMetadata();
        metadata.ByType[typeof(OrderNotFound)] = FakeMetadata.NotFound;
        return metadata;
    }

    [Fact]
    public void Fin_maps_both_ways_and_the_typed_failure_resolves_by_instance()
    {
        using (ErrorApiRuntime.Use(BuildMetadata()))
        {
            LanguageExt.Fin<int> ok = 7;
            Assert.Equal(7, Assert.IsType<Ok<int>>(ok.ToHttpResult()).Value);

            LanguageExt.Fin<int> failed = new OrderNotFound(Guid.Empty);
            var problem = Assert.IsType<ProblemHttpResult>(failed.ToHttpResult());

            Assert.Equal(404, problem.StatusCode);
            Assert.Equal("Orders.NotFound", Assert.Contains(ResultHttpExtensions.CodeExtensionName, problem.ProblemDetails.Extensions));
        }
    }

    [Fact]
    public void The_shaped_success_the_valueless_form_and_ToProblem_all_answer()
    {
        using (ErrorApiRuntime.Use(BuildMetadata()))
        {
            LanguageExt.Fin<int> ok = 7;
            Assert.IsType<NoContent>(ok.ToHttpResult(_ => Microsoft.AspNetCore.Http.TypedResults.NoContent()));
            Assert.IsType<NoContent>(ok.ToNoContentResult());

            LanguageExt.Fin<int> failed = new OrderNotFound(Guid.Empty);
            Assert.IsType<ProblemHttpResult>(failed.ToNoContentResult());
            Assert.IsType<ProblemHttpResult>(((LanguageExt.Common.Error)new OrderNotFound(Guid.Empty)).ToProblem());
        }
    }

    [Fact]
    public void Either_maps_right_to_the_value_and_left_to_a_problem()
    {
        using (ErrorApiRuntime.Use(BuildMetadata()))
        {
            LanguageExt.Either<LanguageExt.Common.Error, int> right = 7;
            Assert.Equal(7, Assert.IsType<Ok<int>>(right.ToHttpResult()).Value);

            LanguageExt.Either<LanguageExt.Common.Error, int> left = (LanguageExt.Common.Error)new OrderNotFound(Guid.Empty);
            Assert.Equal(404, Assert.IsType<ProblemHttpResult>(left.ToHttpResult()).StatusCode);
        }
    }

    [Fact]
    public void The_created_family_builds_locations_from_the_value()
    {
        LanguageExt.Fin<int> ok = 7;

        Assert.Equal("/fixed", Assert.IsType<Created<int>>(ok.ToCreated("/fixed")).Location);
        Assert.Equal("/things/7", Assert.IsType<Created<int>>(ok.ToCreated(v => $"/things/{v}")).Location);
        Assert.Equal("/things/7", Assert.IsType<Created<int>>(ok.ToCreatedAtUri(v => new Uri($"/things/{v}", UriKind.Relative))).Location);
        Assert.Equal("GetThing", Assert.IsType<CreatedAtRoute<int>>(
            ok.ToCreatedAtRoute("GetThing", v => new RouteValueDictionary { ["id"] = v })).RouteName);
    }

    [Fact]
    public async Task The_awaited_forms_map_the_same_way()
    {
        using (ErrorApiRuntime.Use(BuildMetadata()))
        {
            Assert.IsType<Ok<int>>(await Task.FromResult<LanguageExt.Fin<int>>(7).ToHttpResult());
            Assert.IsType<NoContent>(await Task.FromResult<LanguageExt.Fin<int>>(7).ToNoContentResult());
            Assert.Equal("/things/7", Assert.IsType<Created<int>>(
                await Task.FromResult<LanguageExt.Fin<int>>(7).ToCreated(v => $"/things/{v}")).Location);
            Assert.IsType<CreatedAtRoute<int>>(
                await Task.FromResult<LanguageExt.Fin<int>>(7).ToCreatedAtRoute("GetThing", v => new RouteValueDictionary { ["id"] = v }));
        }
    }
}
