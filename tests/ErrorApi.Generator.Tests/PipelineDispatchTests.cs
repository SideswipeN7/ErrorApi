using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// The parts of a dispatch the message alone cannot reach: pipeline types generic over the request,
/// failures declared on the message itself, and handlers matched by naming convention instead of an
/// interface. Nothing here names a library — the fixtures share their shapes with MediatR, Wolverine
/// and Brighter.
/// </summary>
public sealed class PipelineDispatchTests
{
    /// <summary>A mediator with a pipeline: sender, handler interface, and a behaviour interface.</summary>
    private const string Mediator = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Mediator;

        public interface IRequest<TResponse>;

        public interface IRequestHandler<TRequest, TResponse>
            where TRequest : IRequest<TResponse>
        {
            Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
        }

        public interface IPipelineBehavior<TRequest, TResponse>
        {
            Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next);
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

        [ErrorCatalog("Common")]
        public static class CommonErrors
        {
            [Error(400, Title = "Request failed validation")]
            public sealed class Validation(string message) : Exception(message);
        }

        [ErrorCatalog("Orders")]
        public static partial class OrderErrors
        {
            [Error(404)] public static partial Error NotFound { get; }
        }

        public sealed record Order(Guid Id);
        public sealed record GetOrder(Guid Id) : IRequest<Result<Order>>;

        public sealed class GetOrderHandler : IRequestHandler<GetOrder, Result<Order>>
        {
            public Task<Result<Order>> Handle(GetOrder request, CancellationToken cancellationToken) =>
                Task.FromResult<Result<Order>>(OrderErrors.NotFound);
        }

        public sealed class ValidationBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        {
            public Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next)
            {
                if (request is null)
                {
                    throw new CommonErrors.Validation("empty request");
                }

                return next();
            }
        }

        public static class Endpoints
        {
            public static void Map(IEndpointRouteBuilder app) =>
                app.MapGet("/orders/{id:guid}", async (Guid id, ISender sender) =>
                    (await sender.Send(new GetOrder(id))).ToHttpResult());
        }
        """;

    [Fact]
    public void A_behaviour_generic_over_the_request_reaches_every_dispatched_endpoint()
    {
        var output = GeneratorHarness.RunAndCompile(Mediator, Application);

        // Common.Validation sorts before Orders.NotFound, so the endpoint documents both.
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[0], _errors[1] })",
            output.Source("ErrorApi.Metadata.g.cs"),
            StringComparison.Ordinal);

        Assert.Empty(output.GeneratorDiagnostics);
    }

    [Fact]
    public void A_handler_closed_over_a_concrete_message_is_not_mistaken_for_a_behaviour()
    {
        // Two endpoints, two messages, one behaviour. Each endpoint keeps its own handler's failure
        // plus the behaviour's — never the other handler's. If handlers leaked through the pipeline
        // rule, GET would document Orders.AlreadyPaid too.
        const string second = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using ErrorApi;
            using Mediator;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            namespace Shop;

            [ErrorCatalog("Payments")]
            public static partial class PaymentErrors
            {
                [Error(409)] public static partial Error AlreadyPaid { get; }
            }

            public sealed record PayOrder(Guid Id) : IRequest<Result>;

            public sealed class PayOrderHandler : IRequestHandler<PayOrder, Result>
            {
                public Task<Result> Handle(PayOrder request, CancellationToken cancellationToken) =>
                    Task.FromResult<Result>(PaymentErrors.AlreadyPaid);
            }

            public static class PayEndpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapPost("/orders/{id:guid}/pay", async (Guid id, ISender sender) =>
                        (await sender.Send(new PayOrder(id))).ToHttpResult());
            }
            """;

        var metadata = GeneratorHarness.RunAndCompile(Mediator, Application, second).Source("ErrorApi.Metadata.g.cs");

        // GET: Common.Validation + Orders.NotFound — no Payments.AlreadyPaid.
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[0], _errors[1] })",
            metadata,
            StringComparison.Ordinal);

        // POST: Common.Validation + Payments.AlreadyPaid — no Orders.NotFound.
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"POST\", \"/orders/{id}/pay\", new global::ErrorApi.ErrorDescriptor[] { _errors[0], _errors[2] })",
            metadata,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProducesError_on_the_message_type_reaches_the_endpoint()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using ErrorApi;
            using Mediator;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            namespace Shop;

            [ErrorCatalog("Common")]
            public static partial class CommonErrors
            {
                [Error(429)] public static partial Error RateLimited { get; }
            }

            public sealed record Order(Guid Id);

            [ProducesError("Common.RateLimited")]
            public sealed record GetOrder(Guid Id) : IRequest<Order>;

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/orders/{id:guid}", async (Guid id, ISender sender) =>
                        Results.Ok(await sender.Send(new GetOrder(id))));
            }
            """;

        var output = GeneratorHarness.RunAndCompile(Mediator, source);

        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[0] })",
            output.Source("ErrorApi.Metadata.g.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_partial_contract_behind_an_unresolved_dispatch_is_still_reported()
    {
        // The endpoint reaches one failure directly, then dispatches a message nothing handles. Before,
        // EAPI009 only fired on an empty contract — a partial one read as complete, which is the worst
        // way for it to be wrong.
        const string source = """
            using System;
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
                [Error(404)] public static partial Error NotFound { get; }
            }

            public sealed record Order(Guid Id);
            public sealed record GetOrder(Guid Id) : IRequest<Order>;

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/orders/{id:guid}", async (Guid id, ISender sender) =>
                    {
                        if (id == Guid.Empty)
                        {
                            return OrderErrors.NotFound.ToProblem();
                        }

                        return Results.Ok(await sender.Send(new GetOrder(id)));
                    });
            }
            """;

        var output = GeneratorHarness.RunAndCompile(Mediator, source);

        var reported = Assert.Single(output.GeneratorDiagnostics, d => d.Id == "EAPI009");
        Assert.Contains("Send", reported.GetMessage(), StringComparison.Ordinal);

        // The failure it could reach stays in the contract.
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[0] })",
            output.Source("ErrorApi.Metadata.g.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_convention_handler_with_no_interface_is_found_through_the_message()
    {
        // The Wolverine shape: a bus whose implementation is elsewhere, and a handler matched purely by
        // the `*Handler` suffix and a `Handle` method taking the message.
        const string source = """
            using System;
            using System.Threading.Tasks;
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            namespace Shop;

            public interface IMessageBus
            {
                Task<T> InvokeAsync<T>(object message);
            }

            [ErrorCatalog("Orders")]
            public static partial class OrderErrors
            {
                [Error(404)] public static partial Error NotFound { get; }
            }

            public sealed record Order(Guid Id);
            public sealed record GetOrder(Guid Id);

            public static class GetOrderHandler
            {
                public static Task<Result<Order>> Handle(GetOrder command) =>
                    Task.FromResult<Result<Order>>(OrderErrors.NotFound);
            }

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapGet("/orders/{id:guid}", async (Guid id, IMessageBus bus) =>
                        (await bus.InvokeAsync<Result<Order>>(new GetOrder(id))).ToHttpResult());
            }
            """;

        var output = GeneratorHarness.RunAndCompile(source);

        Assert.Empty(output.GeneratorDiagnostics);
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[0] })",
            output.Source("ErrorApi.Metadata.g.cs"),
            StringComparison.Ordinal);
    }
}
