using Microsoft.CodeAnalysis;
using Xunit;

namespace ErrorApi.Generator.Tests;

public sealed class DiagnosticTests
{
    [Fact]
    public void EAPI001_reports_a_duplicated_code()
    {
        const string source = """
            using ErrorApi;

            public static partial class A
            {
                [Error("Dup.Code", 404, Title = "One")]
                public static partial Error One { get; }
            }

            public static partial class B
            {
                [Error("Dup.Code", 409, Title = "Two")]
                public static partial Error Two { get; }
            }
            """;

        var diagnostic = Assert.Single(GeneratorHarness.Run(source).GeneratorDiagnostics, d => d.Id == "EAPI001");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Dup.Code", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void EAPI002_reports_a_route_that_is_not_a_constant()
    {
        const string source = """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app, string pattern) =>
                    app.MapGet(pattern, () => Results.Ok());
            }
            """;

        Assert.Contains(GeneratorHarness.Run(source).GeneratorDiagnostics, d => d.Id == "EAPI002");
    }

    [Fact]
    public void EAPI003_reports_a_partial_catalog_member_that_is_not_static()
    {
        const string source = """
            using ErrorApi;

            public partial class A
            {
                [Error("A.Broken", 500)]
                public partial Error Broken { get; }
            }
            """;

        var diagnostic = Assert.Single(GeneratorHarness.Run(source).GeneratorDiagnostics, d => d.Id == "EAPI003");

        Assert.Contains("static", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void EAPI003_reports_a_partial_catalog_member_that_does_not_return_Error()
    {
        const string source = """
            using ErrorApi;

            public static partial class A
            {
                [Error("A.Broken", 500)]
                public static partial string Broken { get; }
            }
            """;

        var diagnostic = Assert.Single(GeneratorHarness.Run(source).GeneratorDiagnostics, d => d.Id == "EAPI003");

        Assert.Contains("ErrorApi.Error", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_member_that_implements_itself_is_declarative_and_needs_no_diagnostic()
    {
        const string source = """
            using ErrorApi;

            public static class A
            {
                [Error("A.Handled", 500, Title = "Handled")]
                public static Error Handled => new("A.Handled", 500, "Handled");
            }
            """;

        var output = GeneratorHarness.RunAndCompile(source);

        Assert.Empty(output.GeneratorDiagnostics);
        Assert.Contains("\"A.Handled\"", output.Source("ErrorApi.Metadata.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void EAPI003_reports_a_catalog_member_in_a_non_partial_type()
    {
        const string source = """
            using ErrorApi;

            public static class A
            {
                [Error("A.Broken", 500)]
                public static partial Error Broken { get; }
            }
            """;

        Assert.Contains(GeneratorHarness.Run(source).GeneratorDiagnostics, d => d.Id == "EAPI003");
    }

    [Fact]
    public void EAPI004_reports_a_status_outside_the_http_range()
    {
        const string source = """
            using ErrorApi;

            public static partial class A
            {
                [Error("A.Odd", 42)]
                public static partial Error Odd { get; }
            }
            """;

        var diagnostic = Assert.Single(GeneratorHarness.Run(source).GeneratorDiagnostics, d => d.Id == "EAPI004");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void EAPI005_reports_a_declared_code_that_is_not_in_the_catalog()
    {
        const string source = """
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/things", [ProducesError("Nope.Missing")] () => Results.Ok());
            }
            """;

        var diagnostic = Assert.Single(GeneratorHarness.Run(source).GeneratorDiagnostics, d => d.Id == "EAPI005");

        Assert.Contains("Nope.Missing", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void EAPI006_only_fires_for_handlers_that_return_a_result()
    {
        const string source = """
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app)
                {
                    app.MapGet("/plain", () => "hello");
                    app.MapGet("/result", () => Result.Success());
                }
            }
            """;

        var diagnostic = Assert.Single(GeneratorHarness.Run(source).GeneratorDiagnostics, d => d.Id == "EAPI006");

        Assert.Contains("/result", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_clean_api_produces_no_diagnostics()
    {
        var output = GeneratorHarness.RunAndCompile(TestSources.Catalog, TestSources.Service, TestSources.Endpoints);

        Assert.Empty(output.GeneratorDiagnostics);
    }
}
