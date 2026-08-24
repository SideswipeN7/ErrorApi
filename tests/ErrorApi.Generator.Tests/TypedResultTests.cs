using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// The <c>ToTypedResult</c> family answers with <c>Results&lt;…, ProblemHttpResult&gt;</c> so ASP.NET
/// documents the success schema from the signature; these tests pin the mapping of both arms.
/// </summary>
public sealed class TypedResultMappingTests
{
    private sealed record Order(Guid Id);

    [Fact]
    public void A_success_takes_the_ok_arm()
    {
        Result<int> result = 7;

        var ok = Assert.IsType<Ok<int>>(result.ToTypedResult().Result);

        Assert.Equal(7, ok.Value);
    }

    [Fact]
    public void A_failure_takes_the_problem_arm_and_keeps_its_code()
    {
        Result<int> result = new Error("Orders.NotFound", 404, "Order not found");

        var problem = Assert.IsType<ProblemHttpResult>(result.ToTypedResult().Result);

        Assert.Equal(404, problem.StatusCode);
        Assert.Equal("Orders.NotFound", Assert.Contains(ResultHttpExtensions.CodeExtensionName, problem.ProblemDetails.Extensions));
    }

    [Fact]
    public void A_valueless_success_maps_to_no_content() =>
        Assert.IsType<NoContent>(Result.Success().ToTypedResult().Result);

    [Fact]
    public void Created_builds_the_location_from_the_value()
    {
        Result<Order> result = new Order(Guid.Parse("11111111-2222-3333-4444-555555555555"));

        var created = Assert.IsType<Created<Order>>(result.ToTypedCreated(o => $"/orders/{o.Id}").Result);

        Assert.Equal("/orders/11111111-2222-3333-4444-555555555555", created.Location);
    }

    [Fact]
    public void A_failure_never_reaches_the_location_lambda()
    {
        Result<Order> result = new Error("Orders.Invalid", 400);

        var problem = Assert.IsType<ProblemHttpResult>(
            result.ToTypedCreated((Func<Order, string>)(_ => throw new InvalidOperationException("must not be called"))).Result);

        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public void A_named_route_carries_the_route_values_from_the_value()
    {
        var order = new Order(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        Result<Order> result = order;

        var created = Assert.IsType<CreatedAtRoute<Order>>(
            result.ToTypedCreatedAtRoute("GetOrder", o => new RouteValueDictionary { ["id"] = o.Id }).Result);

        Assert.Equal("GetOrder", created.RouteName);
        Assert.Equal(order.Id, Assert.Contains("id", (IDictionary<string, object?>)created.RouteValues!));
    }

    [Fact]
    public async Task The_awaited_forms_map_the_same_way()
    {
        Assert.IsType<Ok<int>>((await Task.FromResult(Result<int>.Success(7)).ToTypedResult()).Result);
        Assert.IsType<NoContent>((await new ValueTask<Result>(Result.Success()).ToTypedResult()).Result);
        Assert.IsType<Created<Order>>(
            (await Task.FromResult(Result<Order>.Success(new Order(Guid.Empty))).ToTypedCreated(o => $"/orders/{o.Id}")).Result);
    }
}

/// <summary>
/// The generator's view of a typed endpoint: the walk follows the handler body exactly as it does for
/// <c>IResult</c>, and the union's <c>ProblemHttpResult</c> arm counts as promising a failure path.
/// </summary>
public sealed class TypedResultDiscoveryTests
{
    [Fact]
    public void Errors_are_discovered_through_a_typed_handler()
    {
        const string source = """
            using System;
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Http.HttpResults;
            using Microsoft.AspNetCore.Routing;

            namespace Shop;

            [ErrorCatalog("Orders")]
            public static partial class OrderErrors
            {
                [Error(404)] public static partial Error NotFound { get; }
            }

            public interface IOrderService { Result<int> GetById(Guid id); }

            public sealed class OrderService : IOrderService
            {
                public Result<int> GetById(Guid id) => id == Guid.Empty ? OrderErrors.NotFound : 1;
            }

            public static class Endpoints
            {
                public static Results<Ok<int>, ProblemHttpResult> GetById(Guid id, IOrderService s) =>
                    s.GetById(id).ToTypedResult();

                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/orders/{id:guid}", GetById);
            }
            """;

        var output = GeneratorHarness.RunAndCompile(source);

        Assert.Empty(output.GeneratorDiagnostics);
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[0] })",
            output.Source("ErrorApi.Metadata.g.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_problem_arm_that_reaches_no_catalog_entry_is_reported()
    {
        // The union says "this can fail", the walk finds nothing behind it — same promise-vs-findings
        // mismatch EAPI006 exists for on Result-returning handlers.
        const string source = """
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Http.HttpResults;
            using Microsoft.AspNetCore.Routing;

            public static class Endpoints
            {
                public static Results<Ok<int>, ProblemHttpResult> Get() => TypedResults.Ok(1);

                public static void Map(IEndpointRouteBuilder app) => app.MapGet("/things", Get);
            }
            """;

        Assert.Single(GeneratorHarness.Run(source).GeneratorDiagnostics, d => d.Id == "EAPI006");
    }
}
