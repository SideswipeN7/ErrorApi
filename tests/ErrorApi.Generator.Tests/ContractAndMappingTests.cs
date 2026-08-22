using Microsoft.AspNetCore.Routing;
using ErrorApi.AspNetCore;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace ErrorApi.Generator.Tests;

public sealed class TypeScriptContractTests
{
    [Fact]
    public void The_contract_module_is_stable()
    {
        var contract = TypeScriptContractWriter.Write(new FakeMetadata());

        Snapshot.Match(contract, nameof(The_contract_module_is_stable));
    }

    [Fact]
    public void Each_endpoint_gets_a_union_of_exactly_its_own_failures()
    {
        var contract = TypeScriptContractWriter.Write(new FakeMetadata());

        Assert.Contains("export type GetOrdersByIdError =\n  | ApiProblem<\"Orders.NotFound\">;", contract, StringComparison.Ordinal);
        Assert.Contains("export type GetHealthError = never;", contract, StringComparison.Ordinal);
    }
}

public sealed class ResultMappingTests
{
    [Fact]
    public void A_failure_carries_its_code_as_a_problem_extension()
    {
        var error = new Error("Orders.NotFound", 404, "Order not found", "No such order.");

        var problem = error.ToProblem();

        Assert.Equal(404, problem.StatusCode);
        Assert.Equal("Order not found", problem.ProblemDetails.Title);
        Assert.Equal("No such order.", problem.ProblemDetails.Detail);
        Assert.Equal("Orders.NotFound", Assert.Contains(ResultHttpExtensions.CodeExtensionName, problem.ProblemDetails.Extensions));
    }

    [Fact]
    public void Extra_extensions_survive_the_mapping()
    {
        var error = new Error("Orders.NotFound", 404).WithExtension("orderId", "42");

        var problem = error.ToProblem();

        Assert.Equal("42", Assert.Contains("orderId", problem.ProblemDetails.Extensions));
    }

    [Fact]
    public void The_problem_type_uri_is_filled_in_when_a_format_is_configured()
    {
        ResultHttpExtensions.ProblemTypeUriFormat = "https://errors.example/{0}";
        try
        {
            var problem = new Error("Orders.NotFound", 404).ToProblem();

            Assert.Equal("https://errors.example/Orders.NotFound", problem.ProblemDetails.Type);
        }
        finally
        {
            ResultHttpExtensions.ProblemTypeUriFormat = null;
        }
    }

    [Fact]
    public void A_success_maps_to_the_value()
    {
        Result<int> result = 7;

        var mapped = Assert.IsType<Ok<int>>(result.ToHttpResult());

        Assert.Equal(7, mapped.Value);
    }

    [Fact]
    public void A_valueless_success_maps_to_no_content() =>
        Assert.IsType<NoContent>(Result.Success().ToHttpResult());

    [Fact]
    public void An_error_converts_implicitly_into_a_failed_result()
    {
        Result<int> result = new Error("Orders.NotFound", 404);

        Assert.True(result.IsFailure);
        Assert.Equal("Orders.NotFound", result.Error.Code);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }
}

public sealed class CreatedMappingTests
{
    private sealed record Order(Guid Id);

    [Fact]
    public void The_location_is_built_from_the_created_value()
    {
        var order = new Order(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        Result<Order> result = order;

        var created = Assert.IsType<Created<Order>>(result.ToCreated(o => $"/orders/{o.Id}"));

        Assert.Equal("/orders/11111111-2222-3333-4444-555555555555", created.Location);
        Assert.Same(order, created.Value);
    }

    [Fact]
    public void A_uri_returning_lambda_works_the_same_way()
    {
        Result<Order> result = new Order(Guid.Empty);

        var created = Assert.IsType<Created<Order>>(result.ToCreated(o => new Uri($"https://api.test/orders/{o.Id}")));

        Assert.Equal("https://api.test/orders/00000000-0000-0000-0000-000000000000", created.Location);
    }

    [Fact]
    public void A_named_route_carries_the_route_values_from_the_value()
    {
        var order = new Order(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        Result<Order> result = order;

        var created = Assert.IsType<CreatedAtRoute<Order>>(
            result.ToCreatedAtRoute("GetOrder", o => new RouteValueDictionary { ["id"] = o.Id }));

        Assert.Equal("GetOrder", created.RouteName);
        Assert.Equal(order.Id, Assert.Contains("id", (IDictionary<string, object?>)created.RouteValues!));
    }

    [Fact]
    public void A_failure_never_reaches_the_location_lambda()
    {
        Result<Order> result = new Error("Orders.Invalid", 400, "Invalid order");

        var problem = Assert.IsType<ProblemHttpResult>(
            result.ToCreated((Func<Order, string>)(_ => throw new InvalidOperationException("must not be called"))));

        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task The_awaited_form_maps_the_same_way()
    {
        var order = new Order(Guid.Empty);
        var created = Assert.IsType<Created<Order>>(
            await Task.FromResult(Result<Order>.Success(order)).ToCreated(o => $"/orders/{o.Id}"));

        Assert.Equal("/orders/00000000-0000-0000-0000-000000000000", created.Location);
    }
}
