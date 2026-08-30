using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using ErrorApi.Interop;
using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// Each adapter package is proved twice: the generator has to discover the catalog through that
/// library's own error shape, and the runtime mapping has to resolve back to the same entry the
/// OpenAPI document was built from.
/// </summary>
public sealed class ErrorOrAdapterTests
{
    private const string Source = """
        using System;
        using ErrorOr;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;

        namespace Shop;

        public static class OrderErrors
        {
            [ErrorApi.Error("Orders.NotFound", 404, Title = "Order not found")]
            public static Error NotFound => Error.NotFound("Orders.NotFound", "No such order.");

            [ErrorApi.Error("Orders.AlreadyPaid", 409, Title = "Order already paid")]
            public static Error AlreadyPaid => Error.Conflict("Orders.AlreadyPaid", "Already paid.");
        }

        public sealed record Order(Guid Id);

        public interface IOrderService
        {
            ErrorOr<Order> GetById(Guid id);
        }

        public sealed class OrderService : IOrderService
        {
            public ErrorOr<Order> GetById(Guid id) =>
                id == Guid.Empty ? OrderErrors.NotFound : new Order(id);
        }

        public static class Endpoints
        {
            public static void Map(IEndpointRouteBuilder app) =>
                app.MapGet("/orders/{id:guid}", (Guid id, IOrderService service) =>
                    ErrorApi.Interop.ErrorOrHttpExtensions.ToHttpResult(service.GetById(id)));
        }
        """;

    [Fact]
    public void The_catalog_is_discovered_through_ErrorOr_errors()
    {
        var metadata = GeneratorHarness.RunAndCompile(Source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("\"Orders.NotFound\", 404, \"Order not found\"", metadata, StringComparison.Ordinal);
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[1] })",
            metadata,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_known_code_resolves_to_the_documented_status_and_title()
    {
        var mapped = ErrorOr.Error.Validation("Orders.NotFound", "No such order.")
            .ToErrorApiError(new FakeMetadata());

        // The catalog wins over ErrorOr's own ErrorType, because that is what the document promised.
        Assert.Equal(404, mapped.StatusCode);
        Assert.Equal("Order not found", mapped.Title);
        Assert.Equal("No such order.", mapped.Detail);
    }

    [Fact]
    public void An_unknown_code_falls_back_to_the_ErrorType_mapping()
    {
        var mapped = ErrorOr.Error.Conflict("Shipping.Late", "Too late.").ToErrorApiError(new FakeMetadata());

        Assert.Equal(409, mapped.StatusCode);
        Assert.Equal("Shipping.Late", mapped.Code);
    }
}

public sealed class ErrorOrCreatedTests
{
    private sealed record Order(Guid Id);

    [Fact]
    public void The_location_is_built_from_the_created_value()
    {
        var order = new Order(Guid.Empty);
        ErrorOr.ErrorOr<Order> result = order;

        var created = Assert.IsType<Created<Order>>(result.ToCreated(o => $"/orders/{o.Id}"));

        Assert.Equal("/orders/00000000-0000-0000-0000-000000000000", created.Location);
    }

    [Fact]
    public void A_named_route_carries_the_route_values_from_the_value()
    {
        ErrorOr.ErrorOr<Order> result = new Order(Guid.Empty);

        var created = Assert.IsType<CreatedAtRoute<Order>>(
            result.ToCreatedAtRoute("GetOrder", o => new RouteValueDictionary { ["id"] = o.Id }));

        Assert.Equal("GetOrder", created.RouteName);
    }

    [Fact]
    public void A_failure_produces_a_problem_instead()
    {
        ErrorOr.ErrorOr<Order> result = ErrorOr.Error.Conflict("Shipping.Late", "Too late.");

        var problem = Assert.IsType<ProblemHttpResult>(result.ToCreated(o => $"/orders/{o.Id}"));

        Assert.Equal(409, problem.StatusCode);
    }
}

public sealed class ErrorOrCodeInferenceTests
{
    internal const string SourceForDump = Source;

    private const string Source = """
        using System;
        using ErrorOr;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;

        namespace Shop;

        public static class OrderErrors
        {
            [ErrorApi.Error(404)]
            public static Error NotFound => Error.NotFound("Orders.NotFound", "No such order.");

            [ErrorApi.Error(409)]
            public static Error AlreadyPaid => Error.Conflict("Orders.AlreadyPaid", "Already paid.");
        }

        public sealed record Order(Guid Id);

        public interface IOrderService
        {
            ErrorOr<Order> GetById(Guid id);
            ErrorOr<Success> Pay(Guid id);
        }

        public sealed class OrderService : IOrderService
        {
            public ErrorOr<Order> GetById(Guid id) =>
                id == Guid.Empty ? OrderErrors.NotFound : new Order(id);

            public ErrorOr<Success> Pay(Guid id) => OrderErrors.AlreadyPaid;
        }

        public static class Endpoints
        {
            public static void Map(IEndpointRouteBuilder app)
            {
                app.MapGet("/orders/{id:guid}", (Guid id, IOrderService service) =>
                    ErrorApi.Interop.ErrorOrHttpExtensions.ToHttpResult(service.GetById(id)));

                app.MapPost("/orders/{id:guid}/pay", (Guid id, IOrderService service) =>
                    ErrorApi.Interop.ErrorOrHttpExtensions.ToNoContentResult(service.Pay(id)));
            }
        }
        """;

    [Fact]
    public void The_code_ErrorOr_already_carries_is_the_one_that_gets_documented()
    {
        var output = GeneratorHarness.RunAndCompile(Source);

        Assert.Empty(output.GeneratorDiagnostics);

        var metadata = output.Source("ErrorApi.Metadata.g.cs");
        Assert.Contains("\"Orders.NotFound\", 404, \"Not found\"", metadata, StringComparison.Ordinal);
        Assert.Contains("\"Orders.AlreadyPaid\", 409, \"Already paid\"", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void The_documented_code_still_resolves_at_runtime()
    {
        // The point of inferring from the body: the code the client receives is the code the document
        // promised, because both come from the same literal.
        var mapped = ErrorOr.Error.NotFound("Orders.NotFound", "No such order.")
            .ToErrorApiError(new FakeMetadata());

        Assert.Equal("Orders.NotFound", mapped.Code);
        Assert.Equal(404, mapped.StatusCode);
    }

    [Fact]
    public void The_endpoint_contract_is_unchanged_by_inferring_the_code()
    {
        var metadata = GeneratorHarness.RunAndCompile(Source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[1] })",
            metadata,
            StringComparison.Ordinal);
    }
}

/// <summary>
/// The runtime mapping over ErrorOr: the failure resolves through its code, the success shapes and the
/// Created family answer, and the awaited forms behave like their synchronous twins.
/// </summary>
[Collection("ambient-metadata")]
public sealed class ErrorOrMappingTests
{
    private static ErrorOr.Error KnownFailure() =>
        ErrorOr.Error.NotFound(code: "Orders.NotFound", description: "No such order.");

    [Fact]
    public void A_failure_resolves_through_its_code_and_a_success_through_its_value()
    {
        using (ErrorApiRuntime.Use(new FakeMetadata()))
        {
            ErrorOr.ErrorOr<int> ok = 7;
            Assert.Equal(7, Assert.IsType<Ok<int>>(ok.ToHttpResult()).Value);

            ErrorOr.ErrorOr<int> failed = KnownFailure();
            var problem = Assert.IsType<ProblemHttpResult>(failed.ToHttpResult());

            Assert.Equal(404, problem.StatusCode);
            Assert.Equal("Orders.NotFound", Assert.Contains(ResultHttpExtensions.CodeExtensionName, problem.ProblemDetails.Extensions));
        }
    }

    [Fact]
    public void The_shaped_success_the_valueless_form_and_ToProblem_all_answer()
    {
        using (ErrorApiRuntime.Use(new FakeMetadata()))
        {
            ErrorOr.ErrorOr<int> ok = 7;
            Assert.IsType<NoContent>(ok.ToHttpResult(_ => Microsoft.AspNetCore.Http.TypedResults.NoContent()));
            Assert.IsType<NoContent>(ok.ToNoContentResult());

            ErrorOr.ErrorOr<int> failed = KnownFailure();
            Assert.IsType<ProblemHttpResult>(failed.ToNoContentResult());
            Assert.Equal(404, Assert.IsType<ProblemHttpResult>(KnownFailure().ToProblem()).StatusCode);
        }
    }

    [Fact]
    public void The_created_family_builds_locations_from_the_value()
    {
        ErrorOr.ErrorOr<int> ok = 7;

        Assert.Equal("/fixed", Assert.IsType<Created<int>>(ok.ToCreated("/fixed")).Location);
        Assert.Equal("/things/7", Assert.IsType<Created<int>>(ok.ToCreated(v => $"/things/{v}")).Location);
        Assert.Equal("/things/7", Assert.IsType<Created<int>>(
            ok.ToCreatedAtUri(v => new Uri($"/things/{v}", UriKind.Relative))).Location);
        Assert.Equal("GetThing", Assert.IsType<CreatedAtRoute<int>>(
            ok.ToCreatedAtRoute("GetThing", v => new RouteValueDictionary { ["id"] = v })).RouteName);
    }

    [Fact]
    public async Task The_awaited_forms_map_the_same_way()
    {
        using (ErrorApiRuntime.Use(new FakeMetadata()))
        {
            Assert.IsType<Ok<int>>(await Task.FromResult<ErrorOr.ErrorOr<int>>(7).ToHttpResult());
            Assert.IsType<NoContent>(await Task.FromResult<ErrorOr.ErrorOr<int>>(7).ToHttpResult(_ => Microsoft.AspNetCore.Http.TypedResults.NoContent()));
            Assert.IsType<NoContent>(await Task.FromResult<ErrorOr.ErrorOr<int>>(7).ToNoContentResult());
            Assert.Equal("/things/7", Assert.IsType<Created<int>>(
                await Task.FromResult<ErrorOr.ErrorOr<int>>(7).ToCreated(v => $"/things/{v}")).Location);
            Assert.IsType<CreatedAtRoute<int>>(
                await Task.FromResult<ErrorOr.ErrorOr<int>>(7).ToCreatedAtRoute("GetThing", v => new RouteValueDictionary { ["id"] = v }));
        }
    }
}
