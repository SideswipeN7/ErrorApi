using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// A mediator ends the walk: its <c>Send</c> is implemented in a referenced assembly, while the handler
/// that raises the failures sits right there in the compilation. The bridge is the message type, and
/// nothing here names a library — MediatR, Wolverine and Brighter all share the shape.
/// </summary>
public sealed class DispatchTests
{
    /// <summary>Stands in for MediatR: a message marker, a sender, and a handler interface.</summary>
    private const string Mediator = """
        using System.Threading;
        using System.Threading.Tasks;

        namespace Mediator;

        public interface IRequest<TResponse>;

        public interface IRequestHandler<TRequest, TResponse>
            where TRequest : IRequest<TResponse>
        {
            Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
        }

        public interface ISender
        {
            Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
        }
        """;

    private const string Application = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using ErrorApi;
        using Mediator;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;

        namespace Shop;

        [ErrorCatalog("Orders")]
        public static partial class OrderErrors
        {
            [Error(404)]
            public static partial Error NotFound { get; }

            [Error(409)]
            public static partial Error AlreadyPaid { get; }
        }

        public sealed record Order(Guid Id);

        public sealed record GetOrder(Guid Id) : IRequest<Result<Order>>;

        public sealed class GetOrderHandler : IRequestHandler<GetOrder, Result<Order>>
        {
            public Task<Result<Order>> Handle(GetOrder request, CancellationToken cancellationToken) =>
                Task.FromResult<Result<Order>>(OrderErrors.NotFound);
        }

        public sealed record PayOrder(Guid Id) : IRequest<Result>;

        public sealed class PayOrderHandler : IRequestHandler<PayOrder, Result>
        {
            public Task<Result> Handle(PayOrder request, CancellationToken cancellationToken) =>
                Task.FromResult<Result>(OrderErrors.AlreadyPaid);
        }

        public static class Endpoints
        {
            public static void Map(IEndpointRouteBuilder app)
            {
                app.MapGet("/orders/{id:guid}", async (Guid id, ISender sender) =>
                    (await sender.Send(new GetOrder(id))).ToHttpResult());

                app.MapPost("/orders/{id:guid}/pay", async (Guid id, ISender sender) =>
                    (await sender.Send(new PayOrder(id))).ToHttpResult());
            }
        }
        """;

    [Fact]
    public void A_message_is_followed_into_the_handler_that_raises_the_failure()
    {
        var output = GeneratorHarness.RunAndCompile(Mediator, Application);

        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[1] })",
            output.Source("ErrorApi.Metadata.g.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Each_endpoint_gets_only_the_failures_of_its_own_message()
    {
        var metadata = GeneratorHarness.RunAndCompile(Mediator, Application).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"POST\", \"/orders/{id}/pay\", new global::ErrorApi.ErrorDescriptor[] { _errors[0] })",
            metadata,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Following_a_dispatch_reports_nothing_when_it_works()
    {
        var output = GeneratorHarness.RunAndCompile(Mediator, Application);

        Assert.Empty(output.GeneratorDiagnostics);
    }

    [Fact]
    public void EAPI009_reports_a_dispatch_with_no_handler_in_the_compilation()
    {
        // The handler lives elsewhere, so the endpoint would be documented as failure-free — which reads
        // as deliberate. That silence is the thing worth reporting.
        const string source = """
            using System;
            using System.Threading.Tasks;
            using ErrorApi;
            using Mediator;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            namespace Shop;

            public sealed record Order(Guid Id);
            public sealed record GetOrder(Guid Id) : IRequest<Order>;

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/orders/{id:guid}", async (Guid id, ISender sender) =>
                        Results.Ok(await sender.Send(new GetOrder(id))));
            }
            """;

        var diagnostic = Assert.Single(
            GeneratorHarness.Run(Mediator, source).GeneratorDiagnostics, d => d.Id == "EAPI009");

        Assert.Contains("/orders/{id}", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Send", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_endpoint_that_reaches_its_failures_directly_is_unaffected()
    {
        const string source = """
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            public static partial class Errors
            {
                [Error(410)]
                public static partial Error Gone { get; }
            }

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/things", () => Errors.Gone.ToProblem());
            }
            """;

        var output = GeneratorHarness.RunAndCompile(source);

        Assert.Empty(output.GeneratorDiagnostics);
        Assert.Contains("\"/things\", new global::ErrorApi.ErrorDescriptor[] { _errors[0] }", output.Source("ErrorApi.Metadata.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_plain_call_with_a_source_argument_is_not_mistaken_for_a_dispatch()
    {
        // The argument type is declared in source and the callee is external, but nothing in the
        // compilation claims to handle it, so this must stay quiet rather than guess.
        const string source = """
            using System;
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            public sealed record Payload(string Value);

            public static partial class Errors
            {
                [Error(410)]
                public static partial Error Gone { get; }
            }

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/things", () =>
                    {
                        Console.WriteLine(new Payload("x"));
                        return Errors.Gone.ToProblem();
                    });
            }
            """;

        var output = GeneratorHarness.RunAndCompile(source);

        // The endpoint found its error, so there is nothing to warn about.
        Assert.Empty(output.GeneratorDiagnostics);
    }
}
