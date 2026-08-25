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
}
