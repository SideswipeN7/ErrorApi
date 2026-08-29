using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// A catalog shipped as a package: the consumer sees attributes and signatures, never bodies. Codes
/// resolved from names re-derive identically; a code resolved from a member's body cannot be
/// re-derived, so the declaring assembly exports its resolution and the consumer reads it back.
/// </summary>
public sealed class CrossAssemblyTests
{
    private const string CatalogLibrary = """
        using ErrorApi;

        namespace Shared;

        [ErrorCatalog("Common")]
        public static partial class CommonErrors
        {
            // Name-inferred: any compilation re-derives "Common.RateLimited" from the metadata alone.
            [Error(429)] public static partial Error RateLimited { get; }
        }

        public static class LegacyErrors
        {
            // Body-inferred: the code lives in the implementation, which a consumer cannot read.
            // Without the export, a consumer would fall back to the name and invent "Legacy.Gone".
            [Error(410)]
            public static Error Gone { get; } = new("Very.Old.Gone", 410, "Gone");
        }
        """;

    private const string Application = """
        using System;
        using ErrorApi;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Shared;

        namespace App;

        public static class Endpoints
        {
            public static void Map(IEndpointRouteBuilder app) =>
                app.MapGet("/things", () =>
                    DateTime.UtcNow.Second == 0
                        ? LegacyErrors.Gone.ToProblem()
                        : CommonErrors.RateLimited.ToProblem());
        }
        """;

    [Fact]
    public void A_body_inferred_code_survives_the_assembly_boundary()
    {
        var library = GeneratorHarness.CompileToReference("Shared.Catalog", CatalogLibrary);

        var metadata = GeneratorHarness.Run([library], Application).Source("ErrorApi.Metadata.g.cs");

        // The wire code the declaring assembly resolved — not the name-derived "Legacy.Gone".
        Assert.Contains("\"Very.Old.Gone\"", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Legacy.Gone\"", metadata, StringComparison.Ordinal);

        // And the name-inferred one still re-derives the same way on both sides.
        Assert.Contains("\"Common.RateLimited\"", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void The_export_is_written_into_the_declaring_assembly()
    {
        var output = GeneratorHarness.RunAndCompile(CatalogLibrary);

        Assert.Contains(
            "[assembly: global::ErrorApi.CatalogExport(\"P:Shared.LegacyErrors.Gone\", \"Very.Old.Gone\")]",
            output.Source("ErrorApi.Metadata.g.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>An application layer: catalog, service, and a mediator whose handler lives here too.</summary>
    private const string ApplicationLibrary = """
        using System;
        using System.Threading.Tasks;
        using ErrorApi;

        namespace App;

        [ErrorCatalog("Orders")]
        public static partial class OrderErrors
        {
            [Error(404)] public static partial Error NotFound { get; }
            [Error(409)] public static partial Error AlreadyPaid { get; }
        }

        public sealed record Order(Guid Id);

        public interface IOrderService { Result<Order> GetById(Guid id); }

        public sealed class OrderService : IOrderService
        {
            public Result<Order> GetById(Guid id) =>
                id == Guid.Empty ? OrderErrors.NotFound : new Order(id);
        }

        public interface IRequest<TResponse>;

        public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TResponse>
        {
            Task<TResponse> Handle(TRequest request);
        }

        public interface ISender
        {
            Task<TResponse> Send<TResponse>(IRequest<TResponse> request);
        }

        public sealed record PayOrder(Guid Id) : IRequest<Result>;

        public sealed class PayOrderHandler : IRequestHandler<PayOrder, Result>
        {
            public Task<Result> Handle(PayOrder request) =>
                Task.FromResult<Result>(OrderErrors.AlreadyPaid);
        }
        """;

    [Fact]
    public void The_library_exports_what_its_members_and_messages_can_reach()
    {
        var metadata = GeneratorHarness.RunAndCompile(ApplicationLibrary).Source("ErrorApi.Metadata.g.cs");

        // The interface method, for direct calls into the library…
        Assert.Contains(
            "[assembly: global::ErrorApi.ReachabilityExport(\"M:App.IOrderService.GetById(System.Guid)\", \"Orders.NotFound\")]",
            metadata,
            StringComparison.Ordinal);

        // …and the message type, for dispatches whose handler lives here.
        Assert.Contains(
            "[assembly: global::ErrorApi.ReachabilityExport(\"T:App.PayOrder\", \"Orders.AlreadyPaid\")]",
            metadata,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_service_implemented_in_another_assembly_still_documents_its_failures()
    {
        var library = GeneratorHarness.CompileToReference("App.Library", ApplicationLibrary);

        const string api = """
            using System;
            using ErrorApi;
            using App;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/orders/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
            }
            """;

        var output = GeneratorHarness.Run([library], api);

        Assert.Empty(output.GeneratorDiagnostics);
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[0] })",
            output.Source("ErrorApi.Metadata.g.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_consumer_names_the_layers_it_reads_from()
    {
        // The layered shape: Domain knows nothing about the API; the API decides which referenced
        // assemblies it trusts the contract to come from, in its own project file.
        var library = GeneratorHarness.CompileToReference("MyProject.Domain", ApplicationLibrary);

        const string api = """
            using System;
            using ErrorApi;
            using App;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/orders/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
            }
            """;

        // Included — by prefix wildcard, the way a layered solution would write it.
        var included = GeneratorHarness.Run(
            [library],
            new Dictionary<string, string> { ["build_property.ErrorApiIncludeAssemblies"] = "MyProject.*" },
            api);
        Assert.Contains("\"Orders.NotFound\"", included.Source("ErrorApi.Metadata.g.cs"), StringComparison.Ordinal);

        // Excluded — the same reference, but the filter names a different world: the exports are not
        // read and the endpoint honestly documents nothing from that assembly.
        var excluded = GeneratorHarness.Run(
            [library],
            new Dictionary<string, string> { ["build_property.ErrorApiIncludeAssemblies"] = "SomeoneElse.*" },
            api);
        Assert.DoesNotContain("\"Orders.NotFound\"", excluded.Source("ErrorApi.Metadata.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_library_opts_out_of_exporting_in_its_own_project_file()
    {
        var output = GeneratorHarness.Run(
            [],
            new Dictionary<string, string> { ["build_property.ErrorApiExportReachability"] = "false" },
            ApplicationLibrary);

        Assert.DoesNotContain("ReachabilityExport", output.Source("ErrorApi.Metadata.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_dispatch_whose_handler_lives_in_another_assembly_is_resolved_through_the_export()
    {
        var library = GeneratorHarness.CompileToReference("App.Library", ApplicationLibrary);

        const string api = """
            using System;
            using ErrorApi;
            using App;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapPost("/orders/{id:guid}/pay", async (Guid id, ISender sender) =>
                        (await sender.Send(new PayOrder(id))).ToHttpResult());
            }
            """;

        var output = GeneratorHarness.Run([library], api);

        // The 409 comes through the message export — no EAPI009, no [ProducesError] anywhere.
        Assert.Empty(output.GeneratorDiagnostics);
        Assert.Contains("\"Orders.AlreadyPaid\"", output.Source("ErrorApi.Metadata.g.cs"), StringComparison.Ordinal);
    }
}
