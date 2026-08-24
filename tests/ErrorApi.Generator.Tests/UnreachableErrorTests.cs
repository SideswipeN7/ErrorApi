using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// A catalog entry no endpoint can return is either dead code or a contract that lost a failure on the
/// way. The two want opposite fixes, so the rule reports and lets the author decide.
/// </summary>
public sealed class UnreachableErrorTests
{
    [Fact]
    public void Only_the_entries_no_endpoint_reaches_are_reported()
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
                [Error(400)] public static partial Error Validation { get; }
                [Error(403)] public static partial Error Forbidden { get; }
                [Error(404)] public static partial Error NotFound { get; }
                [Error(429)] public static partial Error RateLimited { get; }
            }

            public interface IOrderService { Result<int> GetById(Guid id); }

            public sealed class OrderService : IOrderService
            {
                public Result<int> GetById(Guid id) =>
                    id == Guid.Empty ? CommonErrors.NotFound : CommonErrors.Forbidden;
            }

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/orders/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
            }
            """;

        var reported = GeneratorHarness.RunAndCompile(source).GeneratorDiagnostics
            .Where(d => d.Id == "EAPI010")
            .Select(d => d.GetMessage())
            .ToList();

        Assert.Equal(2, reported.Count);
        Assert.Contains(reported, m => m.Contains("Common.Validation", StringComparison.Ordinal));
        Assert.Contains(reported, m => m.Contains("Common.RateLimited", StringComparison.Ordinal));
        Assert.DoesNotContain(reported, m => m.Contains("Common.NotFound", StringComparison.Ordinal));
        Assert.DoesNotContain(reported, m => m.Contains("Common.Forbidden", StringComparison.Ordinal));
    }

    [Fact]
    public void A_catalog_with_no_endpoints_to_check_against_stays_quiet()
    {
        // A shared catalog project is not an API, so there is nothing to be unreachable from.
        const string source = """
            using ErrorApi;

            [ErrorCatalog("Common")]
            public static partial class CommonErrors
            {
                [Error(400)] public static partial Error Validation { get; }
                [Error(429)] public static partial Error RateLimited { get; }
            }
            """;

        Assert.Empty(GeneratorHarness.RunAndCompile(source).GeneratorDiagnostics);
    }

    [Fact]
    public void ProducesError_counts_as_reaching_it()
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
                [Error(429)] public static partial Error RateLimited { get; }
            }

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/things", [ProducesError("Common.RateLimited")] () => Results.Ok());
            }
            """;

        Assert.Empty(GeneratorHarness.RunAndCompile(source).GeneratorDiagnostics);
    }

    [Fact]
    public void A_type_identified_entry_counts_when_it_is_constructed()
    {
        const string source = """
            using System;
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            namespace Shop;

            [Error(404)]
            public sealed record OrderNotFound(Guid Id);

            [Error(410)]
            public sealed record OrderGone(Guid Id);

            public interface IOrderService { object GetById(Guid id); }

            public sealed class OrderService : IOrderService
            {
                public object GetById(Guid id) => new OrderNotFound(id);
            }

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/orders/{id:guid}", (Guid id, IOrderService s) => Results.Ok(s.GetById(id)));
            }
            """;

        var reported = Assert.Single(
            GeneratorHarness.RunAndCompile(source).GeneratorDiagnostics, d => d.Id == "EAPI010");

        Assert.Contains("OrderGone", reported.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_failure_raised_behind_a_boundary_the_walk_cannot_cross_shows_up_here()
    {
        // This is the case the rule really earns its keep on. The behaviour is generic over the request,
        // so nothing constructs it with a concrete message and the walk never reaches it — the endpoint
        // contract silently loses the 400. EAPI009 stays quiet because the contract is partial, not
        // empty, which is exactly why a second signal is worth having.
        const string source = """
            using System;
            using System.Threading.Tasks;
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            namespace Shop;

            [ErrorCatalog("Common")]
            public static partial class CommonErrors
            {
                [Error(400)] public static partial Error Validation { get; }
            }

            [ErrorCatalog("Orders")]
            public static partial class OrderErrors
            {
                [Error(404)] public static partial Error NotFound { get; }
            }

            public interface IPipelineBehavior<TRequest, TResponse>
            {
                Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next);
            }

            public sealed record GetOrder(Guid Id);

            public sealed class ValidationBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
            {
                public Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next)
                {
                    _ = CommonErrors.Validation;
                    return next();
                }
            }

            public interface IOrderService { Result<int> GetById(Guid id); }

            public sealed class OrderService : IOrderService
            {
                public Result<int> GetById(Guid id) => OrderErrors.NotFound;
            }

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/orders/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
            }
            """;

        var output = GeneratorHarness.RunAndCompile(source);

        var reported = Assert.Single(output.GeneratorDiagnostics, d => d.Id == "EAPI010");
        Assert.Contains("Common.Validation", reported.GetMessage(), StringComparison.Ordinal);

        // The endpoint kept the failure it could reach, so nothing else complains.
        Assert.DoesNotContain(output.GeneratorDiagnostics, d => d.Id == "EAPI009");
    }
}
