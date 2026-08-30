using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// The fast-catalog rules: <c>[ErrorCatalog(prefix, statusCode)]</c> gives every entry inside a
/// default status, a bare <c>[Error]</c> on a type reads the status (and title) out of its base
/// constructor when the wrapped library already carries them, and <c>[ErrorStatusCode]</c> /
/// <c>[ErrorDescription]</c> override anything less specific. One line per entry, no repetition.
/// </summary>
public sealed class CatalogDefaultsTests
{
    [Fact]
    public void A_catalog_default_status_covers_every_bare_entry()
    {
        const string source = """
            using ErrorApi;

            namespace Shop;

            [ErrorCatalog("Order.Validation", 422)]
            public static partial class ValidationErrors
            {
                [Error] public static partial Error InvalidOrder { get; }
                [Error] public static partial Error MissingCustomer { get; }
                [Error, ErrorStatusCode(400)] public static partial Error MalformedId { get; }
                [Error, ErrorDescription("The order total must be positive.")]
                public static partial Error InvalidTotal { get; }
            }
            """;

        var output = GeneratorHarness.RunAndCompile(source);
        var metadata = output.Source("ErrorApi.Metadata.g.cs");

        Assert.Empty(output.GeneratorDiagnostics);

        // Prefix + name, catalog status — the one-line-per-entry catalog.
        Assert.Contains("\"Order.Validation.InvalidOrder\", 422", metadata, StringComparison.Ordinal);
        Assert.Contains("\"Order.Validation.MissingCustomer\", 422", metadata, StringComparison.Ordinal);

        // The overrides beat the parent info.
        Assert.Contains("\"Order.Validation.MalformedId\", 400", metadata, StringComparison.Ordinal);
        Assert.Contains("The order total must be positive.", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorStatusCode_beats_even_an_explicit_Error_argument()
    {
        const string source = """
            using ErrorApi;

            [ErrorCatalog("Orders")]
            public static partial class OrderErrors
            {
                [Error(404), ErrorStatusCode(410)] public static partial Error Retired { get; }
            }
            """;

        var metadata = GeneratorHarness.RunAndCompile(source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("\"Orders.Retired\", 410", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bare_Error_on_a_type_reads_the_base_constructor()
    {
        // The language-ext shape, stubbed: the library's own type already carries the message and the
        // status, so the attribute adds nothing but the catalog membership.
        const string source = """
            using System;
            using ErrorApi;

            namespace Shop;

            public abstract record Expected(string Message, int Code);

            [ErrorCatalog("Orders")]
            public static class OrderErrors
            {
                [Error]
                public sealed record NotFound(Guid Id) : Expected("Order not found", 404);
            }

            public abstract class LegacyError
            {
                protected LegacyError(string message, int code) { }
            }

            public sealed class ClassicStyle
            {
                [Error]
                public sealed class Gone : LegacyError
                {
                    public Gone() : base("Order is gone", 410) { }
                }
            }
            """;

        var output = GeneratorHarness.RunAndCompile(source);
        var metadata = output.Source("ErrorApi.Metadata.g.cs");

        Assert.Empty(output.GeneratorDiagnostics);

        // Status and title both read from the primary-constructor base call…
        Assert.Contains("\"Orders.NotFound\", 404, \"Order not found\"", metadata, StringComparison.Ordinal);

        // …and from a classic `: base(...)` initializer.
        Assert.Contains("\"Gone\", 410, \"Order is gone\"", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void A_base_inferred_status_survives_the_assembly_boundary()
    {
        const string library = """
            using System;
            using ErrorApi;

            namespace Shared;

            public abstract record Expected(string Message, int Code);

            [ErrorCatalog("Orders")]
            public static class OrderErrors
            {
                [Error]
                public sealed record NotFound(Guid Id) : Expected("Order not found", 404);
            }
            """;

        // The declaring assembly bakes the full export, because a consumer cannot read the base call.
        var baked = GeneratorHarness.RunAndCompile(library).Source("ErrorApi.Metadata.g.cs");
        Assert.Contains(
            "[assembly: global::ErrorApi.CatalogExport(\"T:Shared.OrderErrors.NotFound\", \"Orders.NotFound\", 404, \"Order not found\")]",
            baked,
            StringComparison.Ordinal);

        var reference = GeneratorHarness.CompileToReference("Shared.Errors", library);

        const string api = """
            using System;
            using ErrorApi;
            using Shared;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/orders/{id:guid}", (Guid id) =>
                    {
                        if (id == Guid.Empty)
                        {
                            return Results.NotFound(new Shared.OrderErrors.NotFound(id));
                        }

                        return Results.Ok();
                    });
            }
            """;

        var output = GeneratorHarness.Run([reference], api);

        // The consumer resolves the constructed foreign type through the export: right code, right status.
        Assert.Contains(
            "\"Orders.NotFound\", 404, \"Order not found\"",
            output.Source("ErrorApi.Metadata.g.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_ErrorCatalog_claims_its_partial_Error_members_without_any_attribute()
    {
        // The fastest catalog there is: the class declares membership, the members declare names.
        const string source = """
            using ErrorApi;

            namespace Shop;

            [ErrorCatalog("Order.Validation", 422)]
            public static partial class ValidationErrors
            {
                public static partial Error InvalidOrder { get; }
                public static partial Error MissingCustomer { get; }

                [ErrorStatusCode(400)]
                public static partial Error MalformedId { get; }

                // Not partial: the author's own helper, never an entry.
                public static Error Wrap(string code) => new(code, 400);
            }
            """;

        var output = GeneratorHarness.RunAndCompile(source);
        var metadata = output.Source("ErrorApi.Metadata.g.cs");

        Assert.Empty(output.GeneratorDiagnostics);

        Assert.Contains("\"Order.Validation.InvalidOrder\", 422", metadata, StringComparison.Ordinal);
        Assert.Contains("\"Order.Validation.MissingCustomer\", 422", metadata, StringComparison.Ordinal);
        Assert.Contains("\"Order.Validation.MalformedId\", 400", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Order.Validation.Wrap\"", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void An_implicit_member_is_discovered_by_the_walk_like_any_entry()
    {
        const string source = """
            using System;
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            namespace Shop;

            [ErrorCatalog("Orders", 404)]
            public static partial class OrderErrors
            {
                public static partial Error NotFound { get; }
            }

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/orders/{id:guid}", (Guid id) =>
                    {
                        if (id == Guid.Empty)
                        {
                            return (IResult)OrderErrors.NotFound.ToProblem();
                        }

                        return Results.Ok();
                    });
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
    public void An_implicit_catalog_survives_the_assembly_boundary()
    {
        const string library = """
            using ErrorApi;

            namespace Shared;

            [ErrorCatalog("Orders", 404)]
            public static partial class OrderErrors
            {
                public static partial Error NotFound { get; }
            }

            public interface IOrderService { Result<int> GetById(System.Guid id); }

            public sealed class OrderService : IOrderService
            {
                public Result<int> GetById(System.Guid id) => id == System.Guid.Empty ? OrderErrors.NotFound : 1;
            }
            """;

        var reference = GeneratorHarness.CompileToReference("Shared.Orders", library);

        const string api = """
            using System;
            using ErrorApi;
            using Shared;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/orders/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
            }
            """;

        var output = GeneratorHarness.Run([reference], api);

        // The foreign catalog member carries no [Error] at all; membership plus the catalog default
        // resolve it on the consumer's side, through the export the library baked.
        Assert.Empty(output.GeneratorDiagnostics);
        Assert.Contains("\"Orders.NotFound\", 404", output.Source("ErrorApi.Metadata.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_bare_Error_with_nothing_to_infer_from_is_invalid()
    {
        const string source = """
            using ErrorApi;

            public static partial class OrphanErrors
            {
                [Error] public static partial Error Mystery { get; }
            }
            """;

        var reported = Assert.Single(GeneratorHarness.Run(source).GeneratorDiagnostics, d => d.Id == "EAPI003");
        Assert.Contains("no status code", reported.GetMessage(), StringComparison.Ordinal);
    }
}
