using ErrorApi.Interop;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// The language-ext <b>v5</b> adapter: same contract as the v4 package, compiled against the 5.x API.
/// The package is prerelease until 5.0.0 leaves beta, and this suite is what says the surface still
/// holds when the beta churns.
/// </summary>
public sealed class LanguageExtV5AdapterTests
{
    private const string Source = """
        using System;
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

    [Fact]
    public void A_success_maps_to_the_value_and_created_builds_its_location()
    {
        LanguageExt.Fin<int> ok = 7;
        Assert.Equal(7, Assert.IsType<Ok<int>>(ok.ToHttpResult()).Value);

        LanguageExt.Fin<int> created = 7;
        Assert.Equal("/things/7", Assert.IsType<Created<int>>(created.ToCreated(v => $"/things/{v}")).Location);
    }

    [Fact]
    public void A_failure_carries_its_code_as_a_problem_extension()
    {
        var metadata = new FakeMetadata();
        LanguageExt.Fin<int> result = LanguageExt.Common.Error.New(404, "Orders.NotFound");

        ErrorApiRuntime.Metadata = metadata;
        try
        {
            var problem = Assert.IsType<ProblemHttpResult>(result.ToHttpResult());
            Assert.Equal(404, problem.StatusCode);
        }
        finally
        {
            ErrorApiRuntime.Metadata = null;
        }
    }
}

/// <summary>
/// The v5 runtime mapping over the same shapes the v4 suite covers — the beta must hold the whole
/// surface, not just the discovery path.
/// </summary>
[Collection("ambient-metadata")]
public sealed class LanguageExtV5MappingTests
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

            LanguageExt.Fin<int> failed = LanguageExt.Prelude.FinFail<int>(new OrderNotFound(Guid.Empty));
            var problem = Assert.IsType<ProblemHttpResult>(failed.ToHttpResult());

            Assert.Equal(404, problem.StatusCode);
        }
    }

    [Fact]
    public void The_valueless_form_the_Either_and_the_created_family_all_answer()
    {
        using (ErrorApiRuntime.Use(BuildMetadata()))
        {
            LanguageExt.Fin<int> ok = 7;
            Assert.IsType<NoContent>(ok.ToNoContentResult());
            Assert.Equal("/fixed", Assert.IsType<Created<int>>(ok.ToCreated("/fixed")).Location);
            Assert.Equal("/things/7", Assert.IsType<Created<int>>(
                ok.ToCreatedAtUri(v => new Uri($"/things/{v}", UriKind.Relative))).Location);
            Assert.IsType<CreatedAtRoute<int>>(
                ok.ToCreatedAtRoute("GetThing", v => new Microsoft.AspNetCore.Routing.RouteValueDictionary { ["id"] = v }));

            LanguageExt.Either<LanguageExt.Common.Error, int> right = 7;
            Assert.Equal(7, Assert.IsType<Ok<int>>(right.ToHttpResult()).Value);
        }
    }

    [Fact]
    public async Task The_awaited_forms_map_the_same_way()
    {
        using (ErrorApiRuntime.Use(BuildMetadata()))
        {
            Assert.IsType<Ok<int>>(await Task.FromResult<LanguageExt.Fin<int>>(7).ToHttpResult());
            Assert.IsType<NoContent>(await Task.FromResult<LanguageExt.Fin<int>>(7).ToNoContentResult());
        }
    }
}
