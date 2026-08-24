using Xunit;

namespace ErrorApi.Generator.Tests;

public sealed class EndpointDiscoveryTests
{
    [Fact]
    public void Errors_are_discovered_through_group_prefixes_and_interface_dispatch()
    {
        var output = GeneratorHarness.RunAndCompile(TestSources.Catalog, TestSources.Service, TestSources.Endpoints);

        Snapshot.Match(output.ToSnapshot(), nameof(Errors_are_discovered_through_group_prefixes_and_interface_dispatch));
    }

    [Fact]
    public void A_transitively_reachable_error_reaches_the_endpoint_contract()
    {
        var metadata = GeneratorHarness
            .RunAndCompile(TestSources.Catalog, TestSources.Service, TestSources.Endpoints)
            .Source("ErrorApi.Metadata.g.cs");

        // Pay -> IOrderService.Pay -> OrderService.Pay -> GetById -> Orders.NotFound
        //                                             -> Orders.AlreadyPaid
        //                                             -> Charge  -> Billing.CardDeclined
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"POST\", \"/orders/{id}/pay\", new global::ErrorApi.ErrorDescriptor[] { _errors[0], _errors[1], _errors[2] })",
            metadata,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProducesError_declares_what_the_walker_cannot_see()
    {
        var metadata = GeneratorHarness
            .RunAndCompile(TestSources.Catalog, TestSources.Service, TestSources.Endpoints)
            .Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("new global::ErrorApi.EndpointErrors(\"GET\", \"/orders\",", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void Route_constraints_are_stripped_so_lookups_match_the_runtime_template()
    {
        var metadata = GeneratorHarness
            .RunAndCompile(TestSources.Catalog, TestSources.Service, TestSources.Endpoints)
            .Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("case \"/orders/{id}\":", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain(":guid", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void MapMethods_documents_every_listed_verb()
    {
        const string source = """
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            public static partial class Errors
            {
                [Error("Thing.Gone", 410, Title = "Gone")]
                public static partial Error Gone { get; }
            }

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapMethods("/things", new[] { "PUT", "PATCH" }, () => Errors.Gone.ToProblem());
            }
            """;

        var metadata = GeneratorHarness.RunAndCompile(source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("case \"PATCH\": errors = _endpoints[0].Errors; return true;", metadata, StringComparison.Ordinal);
        Assert.Contains("case \"PUT\": errors = _endpoints[0].Errors; return true;", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_without_a_verb_answers_every_method()
    {
        const string source = """
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            public static partial class Errors
            {
                [Error("Thing.Gone", 410, Title = "Gone")]
                public static partial Error Gone { get; }
            }

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.Map("/things", () => Errors.Gone.ToProblem());
            }
            """;

        var metadata = GeneratorHarness.RunAndCompile(source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("default: errors = _endpoints[0].Errors; return true;", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_groups_compose_their_prefixes()
    {
        const string source = """
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            public static partial class Errors
            {
                [Error("Thing.Gone", 410, Title = "Gone")]
                public static partial Error Gone { get; }
            }

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app)
                {
                    var v1 = app.MapGroup("/api/v1");
                    var things = v1.MapGroup("/things").WithTags("Things");
                    things.MapGet("/{id}", (string id) => Errors.Gone.ToProblem());
                }
            }
            """;

        var metadata = GeneratorHarness.RunAndCompile(source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("case \"/api/v1/things/{id}\":", metadata, StringComparison.Ordinal);
    }
}
