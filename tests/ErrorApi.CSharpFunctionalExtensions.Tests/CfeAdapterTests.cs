using ErrorApi.Interop;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

using CfeResult = CSharpFunctionalExtensions.Result;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// Runtime mapping of the CSharpFunctionalExtensions adapter. The typed error of
/// <c>Result&lt;T, E&gt;</c> resolves by its type through the catalog; a string error resolves only
/// when the string is a known code, and is otherwise a 500 carrying the message.
/// </summary>
public sealed class CfeAdapterTests
{
    private sealed record OrderNotFound(Guid Id);

    private static FakeMetadata BuildMetadata()
    {
        var metadata = new FakeMetadata();
        metadata.ByType[typeof(OrderNotFound)] = FakeMetadata.NotFound;
        return metadata;
    }

    [Fact]
    public void A_typed_failure_resolves_by_its_type()
    {
        var error = CfeHttpExtensions.Resolve(new OrderNotFound(Guid.Empty), BuildMetadata());

        Assert.Equal("Orders.NotFound", error.Code);
        Assert.Equal(404, error.StatusCode);
        Assert.Equal("Order not found", error.Title);
    }

    [Fact]
    public void A_string_that_is_a_known_code_resolves_fully()
    {
        var error = CfeHttpExtensions.Resolve("Orders.AlreadyPaid", BuildMetadata());

        Assert.Equal("Orders.AlreadyPaid", error.Code);
        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public void A_bare_message_is_a_500_because_a_message_is_not_a_contract()
    {
        var error = CfeHttpExtensions.Resolve("something went wrong", BuildMetadata());

        Assert.Equal(500, error.StatusCode);
        Assert.Equal("something went wrong", error.Detail);
    }

    [Fact]
    public void A_success_maps_to_the_value_and_a_failure_to_a_problem()
    {
        ErrorApiRuntime.Metadata = BuildMetadata();
        try
        {
            var ok = global::CSharpFunctionalExtensions.Result.Success<int, OrderNotFound>(7);
            Assert.Equal(7, Assert.IsType<Ok<int>>(ok.ToHttpResult()).Value);

            var failed = global::CSharpFunctionalExtensions.Result.Failure<int, OrderNotFound>(new OrderNotFound(Guid.Empty));
            var problem = Assert.IsType<ProblemHttpResult>(failed.ToHttpResult());

            Assert.Equal(404, problem.StatusCode);
            Assert.Equal("Orders.NotFound", Assert.Contains(ResultHttpExtensions.CodeExtensionName, problem.ProblemDetails.Extensions));
        }
        finally
        {
            ErrorApiRuntime.Metadata = null;
        }
    }

    [Fact]
    public void The_valueless_shapes_map_to_no_content()
    {
        Assert.IsType<NoContent>(CfeResult.Success().ToHttpResult());
        Assert.IsType<NoContent>(global::CSharpFunctionalExtensions.UnitResult.Success<OrderNotFound>().ToHttpResult());
    }

    [Fact]
    public void Created_builds_the_location_from_the_value()
    {
        var result = global::CSharpFunctionalExtensions.Result.Success<int, OrderNotFound>(7);

        var created = Assert.IsType<Created<int>>(result.ToCreated(v => $"/things/{v}"));

        Assert.Equal("/things/7", created.Location);
    }
}

/// <summary>
/// The generator's view: the annotated error type of <c>Result&lt;T, E&gt;</c> is discovered where it
/// is constructed, like any other type-identified failure.
/// </summary>
public sealed class CfeDiscoveryTests
{
    private const string Source = """
        using System;
        using ErrorApi.Interop;
        using CSharpFunctionalExtensions;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;

        namespace Shop;

        [ErrorApi.Error("Orders.NotFound", 404, Title = "Order not found")]
        public sealed record OrderNotFound(Guid Id);

        public sealed record Order(Guid Id);

        public interface IOrderService { Result<Order, OrderNotFound> GetById(Guid id); }

        public sealed class OrderService : IOrderService
        {
            public Result<Order, OrderNotFound> GetById(Guid id) =>
                id == Guid.Empty
                    ? Result.Failure<Order, OrderNotFound>(new OrderNotFound(id))
                    : Result.Success<Order, OrderNotFound>(new Order(id));
        }

        public static class Endpoints
        {
            public static void Map(IEndpointRouteBuilder app) =>
                app.MapGet("/orders/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
        }
        """;

    [Fact]
    public void The_error_type_reaches_the_endpoint_contract()
    {
        var output = GeneratorHarness.RunAndCompile(Source);

        Assert.Empty(output.GeneratorDiagnostics);
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[0] })",
            output.Source("ErrorApi.Metadata.g.cs"),
            StringComparison.Ordinal);
        Assert.Contains("global::Shop.OrderNotFound => _errors[0],", output.Source("ErrorApi.Metadata.g.cs"), StringComparison.Ordinal);
    }
}


