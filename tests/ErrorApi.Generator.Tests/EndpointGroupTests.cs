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

        // …and the resolver switches on the normalized group, so runtime "v1" and "1.0" both match.
        Assert.Contains("switch (global::ErrorApi.EndpointGroup.Normalize(group))", metadata, StringComparison.Ordinal);
        Assert.Contains("case \"1\":", metadata, StringComparison.Ordinal);
        Assert.Contains("case \"2\":", metadata, StringComparison.Ordinal);
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
    public void Asp_Versioning_literals_are_recognised_as_groups()
    {
        // Stand-ins with Asp.Versioning's shapes: the scanner recognises the calls by name and reads
        // the literals, so the package itself is not needed to test the recognition.
        const string source = """
            using System;
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            namespace Shop;

            public sealed class ApiVersion
            {
                public ApiVersion(int major) { }
                public ApiVersion(int major, int minor) { }
            }

            public static class VersioningStubs
            {
                public static RouteHandlerBuilder MapToApiVersion(this RouteHandlerBuilder builder, int version) => builder;
                public static RouteGroupBuilder HasApiVersion(this RouteGroupBuilder builder, ApiVersion version) => builder;
            }

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app)
                {
                    app.MapGet("/orders/{id:guid}", (Guid id, IOrdersV1 s) => s.Get(id).ToHttpResult())
                        .MapToApiVersion(1);

                    app.MapGet("/orders/{id:guid}", (Guid id, IOrdersV2 s) => s.Get(id).ToHttpResult())
                        .MapToApiVersion(2);

                    var reporting = app.MapGroup("/reports").HasApiVersion(new ApiVersion(1, 1));
                    reporting.MapGet("/", (IOrdersV1 s) => s.Get(Guid.Empty).ToHttpResult());
                }
            }
            """;

        var output = GeneratorHarness.RunAndCompile(Domain, source);
        var metadata = output.Source("ErrorApi.Metadata.g.cs");

        // No EAPI011: the versions tell the two mappings apart.
        Assert.Empty(output.GeneratorDiagnostics);

        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[1] }, \"v1\")",
            metadata,
            StringComparison.Ordinal);
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[0] }, \"v2\")",
            metadata,
            StringComparison.Ordinal);

        // The group-builder version applies to the endpoints mapped on it.
        Assert.Contains("\"/reports\", new global::ErrorApi.ErrorDescriptor[] { _errors[1] }, \"v1.1\")", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_route_mapped_twice_without_groups_is_reported()
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
                    app.MapGet("/orders/{id:guid}", (Guid id, IOrdersV1 s) => s.Get(id).ToHttpResult());
                    app.MapGet("/orders/{id:guid}", (Guid id, IOrdersV2 s) => s.Get(id).ToHttpResult());
                }
            }
            """;

        var output = GeneratorHarness.RunAndCompile(Domain, source);

        // The contracts merged into one entry — and EAPI011 says so, because if these are two API
        // versions, each version's document now lists the union.
        Assert.Single(output.GeneratorDiagnostics, d => d.Id == "EAPI011");
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[0], _errors[1] })",
            output.Source("ErrorApi.Metadata.g.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_group_normalizer_twins_agree()
    {
        // The generated switch labels are normalized at emit time; the incoming group is normalized at
        // runtime. Same inputs, same answers — or a version's document loses its contracts.
        foreach (var group in new[] { null, "", "  ", "v1", "V1", "1.0", "v1.0", "1", "v1.1", "2.10", "admin", "Admin", "v9beta", ".0" })
        {
            Assert.Equal(EndpointGroup.Normalize(group), Generator.Helpers.GroupNormalizer.Normalize(group));
        }

        // And the rules themselves: one version, many spellings.
        Assert.Equal("1", EndpointGroup.Normalize("v1"));
        Assert.Equal("1", EndpointGroup.Normalize("1.0"));
        Assert.Equal("1", EndpointGroup.Normalize("V1.0"));
        Assert.Equal("1.1", EndpointGroup.Normalize("v1.1"));
        Assert.Equal("admin", EndpointGroup.Normalize("Admin"));
        Assert.Null(EndpointGroup.Normalize("  "));
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

            var normalized = EndpointGroup.Normalize(group);
            var match = (normalized is null ? null : candidates.FirstOrDefault(e => EndpointGroup.Normalize(e.Group) == normalized))
                ?? candidates.FirstOrDefault(e => e.Group is null)
                ?? (group is null && candidates.Count == 1 ? candidates[0] : null);

            errors = match?.Errors ?? [];
            return match is not null;
        }
    }
}
