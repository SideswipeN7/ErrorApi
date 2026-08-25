using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// Generator diagnostics ignore <c>#pragma warning</c>, so <c>[SuppressErrorApi]</c> is the
/// per-declaration lever — NoWarn silences a rule for the whole project, which is the wrong scope for
/// a deliberate one-off.
/// </summary>
public sealed class SuppressionTests
{
    [Fact]
    public void EAPI010_can_be_silenced_on_the_member_it_flags()
    {
        const string source = """
            using System;
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            namespace Shop;

            [ErrorCatalog("Common")]
            public static partial class CommonErrors
            {
                [Error(404)] public static partial Error NotFound { get; }

                // Kept for the next release; no endpoint returns it yet — and that is deliberate.
                [Error(429), SuppressErrorApi("EAPI010")]
                public static partial Error RateLimited { get; }
            }

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/things", () => CommonErrors.NotFound.ToProblem());
            }
            """;

        Assert.Empty(GeneratorHarness.RunAndCompile(source).GeneratorDiagnostics);
    }

    [Fact]
    public void EAPI009_can_be_silenced_on_the_mapping_method()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            namespace Shop;

            public interface ISender { Task<T> Send<T>(object message); }

            public sealed record GetOrder(Guid Id);

            public static class Endpoints
            {
                [SuppressErrorApi("EAPI009")]
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/orders/{id:guid}", async (Guid id, ISender sender) =>
                        Results.Ok(await sender.Send<object>(new GetOrder(id))));
            }
            """;

        Assert.Empty(GeneratorHarness.RunAndCompile(source).GeneratorDiagnostics);
    }

    [Fact]
    public void An_id_not_listed_still_fires()
    {
        const string source = """
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            namespace Shop;

            [ErrorCatalog("Common")]
            public static partial class CommonErrors
            {
                [Error(429), SuppressErrorApi("EAPI002")]
                public static partial Error RateLimited { get; }
            }

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/things", () => Results.Ok());
            }
            """;

        Assert.Single(GeneratorHarness.RunAndCompile(source).GeneratorDiagnostics, d => d.Id == "EAPI010");
    }
}
