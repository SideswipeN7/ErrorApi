using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// Endpoint identity is route + method + API description group. Two versions of the same route — split
/// by <c>WithGroupName</c> or <c>[ApiExplorerSettings]</c>, the way header-versioned APIs split their
/// documents — keep separate contracts instead of silently sharing one entry.
/// </summary>
public sealed class EndpointGroupTests
{
    private const string Domain = """
        using System;
        using ErrorApi;

        namespace Shop;

        [ErrorCatalog("Orders")]
        public static partial class OrderErrors
        {
            [Error(404)] public static partial Error NotFound { get; }
            [Error(410)] public static partial Error Retired { get; }
        }

        public interface IOrdersV1 { Result<int> Get(Guid id); }
        public interface IOrdersV2 { Result<int> Get(Guid id); }

        public sealed class OrdersV1 : IOrdersV1
        {
            public Result<int> Get(Guid id) => id == Guid.Empty ? OrderErrors.Retired : 1;
        }

        public sealed class OrdersV2 : IOrdersV2
        {
            public Result<int> Get(Guid id) => id == Guid.Empty ? OrderErrors.NotFound : 1;
        }
        """;

    [Fact]
    public void Two_versions_of_one_route_keep_separate_contracts()
    {
        const string source = """
            using System;
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            namespace Shop;

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app)
                {
                    app.MapGet("/orders/{id:guid}", (Guid id, IOrdersV1 s) => s.Get(id).ToHttpResult())
                        .WithGroupName("v1");

                    app.MapGet("/orders/{id:guid}", (Guid id, IOrdersV2 s) => s.Get(id).ToHttpResult())
                        .WithGroupName("v2");
                }
            }
            """;

        var output = GeneratorHarness.RunAndCompile(Domain, source);
        var metadata = output.Source("ErrorApi.Metadata.g.cs");

        // Separate table entries, each carrying its group…
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[1] }, \"v1\")",
            metadata,
            StringComparison.Ordinal);
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[0] }, \"v2\")",
            metadata,
            StringComparison.Ordinal);

        // …and the resolver switches on the group.
        Assert.Contains("switch (group)", metadata, StringComparison.Ordinal);
        Assert.Contains("case \"v1\":", metadata, StringComparison.Ordinal);
        Assert.Contains("case \"v2\":", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void A_group_builder_passes_its_name_down_to_the_endpoints()
    {
        const string source = """
            using System;
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            namespace Shop;

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app)
                {
                    var v1 = app.MapGroup("/api").WithGroupName("v1");
                    v1.MapGet("/orders/{id:guid}", (Guid id, IOrdersV1 s) => s.Get(id).ToHttpResult());
                }
            }
            """;

        var metadata = GeneratorHarness.RunAndCompile(Domain, source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/api/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[1] }, \"v1\")",
            metadata,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_controller_reads_its_group_from_ApiExplorerSettings()
    {
        const string source = """
            using System;
            using ErrorApi;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;

            namespace Shop;

            [ApiController]
            [Route("api/orders")]
            [ApiExplorerSettings(GroupName = "admin")]
            public sealed class OrdersController(IOrdersV1 service) : ControllerBase
            {
                [HttpGet("{id:guid}")]
                public IResult Get(Guid id) => service.Get(id).ToHttpResult();
            }
            """;

        var metadata = GeneratorHarness.RunAndCompile(Domain, source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/api/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[1] }, \"admin\")",
            metadata,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_cosmetic_group_never_hides_the_errors()
    {
        // One grouped endpoint, looked up without a group — the compat rule: a null group matches a
        // route that lives in exactly one group.
        var metadata = new GroupedMetadata();

        Assert.True(metadata.TryGetEndpointErrors("GET", "/reports", null, out var errors));
        Assert.Single(errors);

        // And an unknown group misses rather than guessing.
        Assert.False(metadata.TryGetEndpointErrors("GET", "/reports", "v9", out _));
    }

    [Fact]
    public void The_exact_group_wins_and_the_ungrouped_entry_is_the_fallback()
    {
        var metadata = new GroupedMetadata();

        Assert.True(metadata.TryGetEndpointErrors("GET", "/orders/{id}", "v2", out var v2));
        Assert.Equal("Orders.NotFound", Assert.Single(v2).Code);

        // A group nobody declared falls back to the ungrouped entry for the same route.
        Assert.True(metadata.TryGetEndpointErrors("GET", "/things", "v9", out var fallback));
        Assert.Equal("Orders.Retired", Assert.Single(fallback).Code);
    }

    [Fact]
    public void The_TS_contract_tells_load_bearing_groups_apart()
    {
        var contract = ErrorApi.AspNetCore.TypeScriptContractWriter.Write(new GroupedMetadata());

        Assert.Contains("export type GetOrdersByIdV1Error", contract, StringComparison.Ordinal);
        Assert.Contains("export type GetOrdersByIdV2Error", contract, StringComparison.Ordinal);
        Assert.Contains("\"GET /orders/{id} @v1\"", contract, StringComparison.Ordinal);
        Assert.Contains("\"GET /orders/{id} @v2\"", contract, StringComparison.Ordinal);

        // A group that is not load-bearing changes neither the alias nor the key.
        Assert.Contains("export type GetReportsError", contract, StringComparison.Ordinal);
        Assert.Contains("\"GET /reports\"", contract, StringComparison.Ordinal);
    }

    /// <summary>Two versions of one route, one cosmetic group, one ungrouped entry.</summary>
    private sealed class GroupedMetadata : IErrorApiMetadata
    {
        private static readonly ErrorDescriptor Retired =
            new("Orders.Retired", 410, "Retired", null, null, "Shop.OrderErrors.Retired");

        private static readonly ErrorDescriptor NotFound =
            new("Orders.NotFound", 404, "Not found", null, null, "Shop.OrderErrors.NotFound");

        public IReadOnlyList<ErrorDescriptor> AllErrors { get; } = [Retired, NotFound];

        public IReadOnlyList<EndpointErrors> Endpoints { get; } =
        [
            new("GET", "/orders/{id}", [Retired], "v1"),
            new("GET", "/orders/{id}", [NotFound], "v2"),
            new("GET", "/reports", [Retired], "reporting"),
            new("GET", "/things", [Retired]),
        ];

        public ErrorDescriptor? FindError(string code) => AllErrors.FirstOrDefault(e => e.Code == code);

        public ErrorDescriptor? FindErrorForInstance(object? instance) => null;

        public bool TryGetEndpointErrors(string httpMethod, string routePattern, out IReadOnlyList<ErrorDescriptor> errors) =>
            TryGetEndpointErrors(httpMethod, routePattern, null, out errors);

        public bool TryGetEndpointErrors(string httpMethod, string routePattern, string? group, out IReadOnlyList<ErrorDescriptor> errors)
        {
            var candidates = Endpoints
                .Where(e => e.HttpMethod == httpMethod && e.RoutePattern == routePattern)
                .ToList();

            var match = candidates.FirstOrDefault(e => e.Group == group)
                ?? candidates.FirstOrDefault(e => e.Group is null)
                ?? (group is null && candidates.Count == 1 ? candidates[0] : null);

            errors = match?.Errors ?? [];
            return match is not null;
        }
    }
}
