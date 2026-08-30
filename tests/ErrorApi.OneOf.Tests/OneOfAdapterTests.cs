using Microsoft.AspNetCore.Routing;
using ErrorApi.Interop;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace ErrorApi.Generator.Tests;

public sealed class OneOfAdapterTests
{
    private const string Source = """
        using System;
        using ErrorApi;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using OneOf;

        namespace Shop;

        [Error("Orders.NotFound", 404, Title = "Order not found")]
        public sealed record OrderNotFound(Guid Id);

        [Error("Orders.AlreadyPaid", 409, Title = "Order already paid")]
        public sealed record OrderAlreadyPaid(Guid Id);

        public sealed record Order(Guid Id);

        public interface IOrderService
        {
            OneOf<Order, OrderNotFound, OrderAlreadyPaid> Pay(Guid id);
        }

        public sealed class OrderService : IOrderService
        {
            public OneOf<Order, OrderNotFound, OrderAlreadyPaid> Pay(Guid id)
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
                app.MapPost("/orders/{id:guid}/pay", (Guid id, IOrderService service) =>
                    ErrorApi.Interop.OneOfHttpExtensions.ToHttpResult(service.Pay(id)));
        }
        """;

    [Fact]
    public void Union_cases_become_the_documented_errors_of_the_endpoint()
    {
        var metadata = GeneratorHarness.RunAndCompile(Source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"POST\", \"/orders/{id}/pay\", "
            + "new global::ErrorApi.ErrorDescriptor[] { _errors[0], _errors[1] })",
            metadata,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Union_cases_are_resolved_by_type_without_reflection()
    {
        var metadata = GeneratorHarness.RunAndCompile(Source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("global::Shop.OrderNotFound => _errors[1],", metadata, StringComparison.Ordinal);
    }
}

public sealed class AdapterProblemTests
{
    [Fact]
    public void The_generic_union_failure_branch_produces_a_problem_response()
    {
        var problem = Assert.IsType<ProblemHttpResult>(OneOfHttpExtensions.Problem(new UnknownFailure()));

        Assert.Equal(500, problem.StatusCode);
    }

    private sealed record UnknownFailure;
}

/// <summary>
/// The runtime mapping across the arity ladder: unions of two, three and four arms, the valueless
/// shapes, and the awaited forms — each resolving its failure case by instance type through the model.
/// </summary>
[Collection("ambient-metadata")]
public sealed class OneOfMappingTests
{
    private sealed record Order(Guid Id);

    private sealed record OrderNotFound(Guid Id);

    private sealed record OrderConflict(Guid Id);

    private static FakeMetadata BuildMetadata()
    {
        var metadata = new FakeMetadata();
        metadata.ByType[typeof(OrderNotFound)] = FakeMetadata.NotFound;
        metadata.ByType[typeof(OrderConflict)] = FakeMetadata.AlreadyPaid;
        return metadata;
    }

    [Fact]
    public void A_two_arm_union_maps_both_ways()
    {
        using (ErrorApiRuntime.Use(BuildMetadata()))
        {
            OneOf.OneOf<Order, OrderNotFound> ok = new Order(Guid.Empty);
            Assert.IsType<Ok<Order>>(ok.ToHttpResult());

            OneOf.OneOf<Order, OrderNotFound> failed = new OrderNotFound(Guid.Empty);
            var problem = Assert.IsType<ProblemHttpResult>(failed.ToHttpResult());

            Assert.Equal(404, problem.StatusCode);
            Assert.Equal("Orders.NotFound", Assert.Contains(ResultHttpExtensions.CodeExtensionName, problem.ProblemDetails.Extensions));
        }
    }

    [Fact]
    public void Wider_unions_resolve_whichever_failure_arm_is_active()
    {
        using (ErrorApiRuntime.Use(BuildMetadata()))
        {
            OneOf.OneOf<Order, OrderNotFound, OrderConflict> three = new OrderConflict(Guid.Empty);
            Assert.Equal(409, Assert.IsType<ProblemHttpResult>(three.ToHttpResult()).StatusCode);

            OneOf.OneOf<Order, OrderNotFound, OrderConflict, string> four = new OrderNotFound(Guid.Empty);
            Assert.Equal(404, Assert.IsType<ProblemHttpResult>(four.ToHttpResult()).StatusCode);
        }
    }

    [Fact]
    public void The_success_arm_can_be_shaped_and_the_valueless_form_is_no_content()
    {
        using (ErrorApiRuntime.Use(BuildMetadata()))
        {
            OneOf.OneOf<Order, OrderNotFound> ok = new Order(Guid.Empty);
            Assert.IsType<NoContent>(ok.ToHttpResult(_ => Microsoft.AspNetCore.Http.TypedResults.NoContent()));
            Assert.IsType<NoContent>(ok.ToNoContentResult());

            OneOf.OneOf<Order, OrderNotFound> failed = new OrderNotFound(Guid.Empty);
            Assert.IsType<ProblemHttpResult>(failed.ToNoContentResult());
        }
    }

    [Fact]
    public void The_Uri_created_twin_builds_the_location_from_the_value()
    {
        OneOf.OneOf<Order, OrderNotFound> result = new Order(Guid.Empty);

        var created = Assert.IsType<Created<Order>>(
            result.ToCreatedAtUri(o => new Uri($"/orders/{o.Id}", UriKind.Relative)));

        Assert.Equal("/orders/00000000-0000-0000-0000-000000000000", created.Location);
    }

    [Fact]
    public async Task The_awaited_forms_map_the_same_way()
    {
        using (ErrorApiRuntime.Use(BuildMetadata()))
        {
            Assert.IsType<Ok<Order>>(
                await Task.FromResult<OneOf.OneOf<Order, OrderNotFound>>(new Order(Guid.Empty)).ToHttpResult());
            Assert.IsType<ProblemHttpResult>(
                await Task.FromResult<OneOf.OneOf<Order, OrderNotFound, OrderConflict>>(new OrderConflict(Guid.Empty)).ToHttpResult());
            Assert.IsType<NoContent>(
                await Task.FromResult<OneOf.OneOf<Order, OrderNotFound>>(new Order(Guid.Empty)).ToNoContentResult());
        }
    }
}

public sealed class OneOfCreatedTests
{
    private sealed record Order(Guid Id);

    private sealed record OrderRejected;

    [Fact]
    public void The_location_is_built_from_the_created_value()
    {
        OneOf.OneOf<Order, OrderRejected> result = new Order(Guid.Empty);

        var created = Assert.IsType<Created<Order>>(result.ToCreated(o => $"/orders/{o.Id}"));

        Assert.Equal("/orders/00000000-0000-0000-0000-000000000000", created.Location);
    }

    [Fact]
    public void A_named_route_carries_the_route_values_from_the_value()
    {
        OneOf.OneOf<Order, OrderRejected> result = new Order(Guid.Empty);

        var created = Assert.IsType<CreatedAtRoute<Order>>(
            result.ToCreatedAtRoute("GetOrder", o => new RouteValueDictionary { ["id"] = o.Id }));

        Assert.Equal("GetOrder", created.RouteName);
    }

    [Fact]
    public void A_failure_case_produces_a_problem_instead()
    {
        OneOf.OneOf<Order, OrderRejected> result = new OrderRejected();

        Assert.IsType<ProblemHttpResult>(result.ToCreated(o => $"/orders/{o.Id}"));
    }
}
