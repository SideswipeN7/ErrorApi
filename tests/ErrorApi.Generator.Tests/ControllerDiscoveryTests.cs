using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// The second endpoint surface: attribute-routed controllers. An action is a handler like any other —
/// the walk starts at the method — and the route comes from the class prefix, the verb attribute's
/// template, and MVC's token and rooting rules.
/// </summary>
public sealed class ControllerDiscoveryTests
{
    private const string Domain = """
        using System;
        using ErrorApi;

        namespace Shop;

        [ErrorCatalog("Orders")]
        public static partial class OrderErrors
        {
            [Error(404)] public static partial Error NotFound { get; }
            [Error(409)] public static partial Error AlreadyPaid { get; }
        }

        public interface IOrderService
        {
            Result<int> GetById(Guid id);
            Result Pay(Guid id);
        }

        public sealed class OrderService : IOrderService
        {
            public Result<int> GetById(Guid id) => id == Guid.Empty ? OrderErrors.NotFound : 1;
            public Result Pay(Guid id) => GetById(id).IsFailure ? OrderErrors.NotFound : OrderErrors.AlreadyPaid;
        }
        """;

    [Fact]
    public void An_attribute_routed_action_is_documented_like_any_endpoint()
    {
        const string controller = """
            using System;
            using ErrorApi;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;

            namespace Shop;

            [ApiController]
            [Route("api/[controller]")]
            public sealed class OrdersController(IOrderService service) : ControllerBase
            {
                [HttpGet("{id:guid}")]
                public IResult GetById(Guid id) => service.GetById(id).ToHttpResult();

                [HttpPost("{id:guid}/pay")]
                public IResult Pay(Guid id) => service.Pay(id).ToHttpResult();
            }
            """;

        var output = GeneratorHarness.RunAndCompile(Domain, controller);
        var metadata = output.Source("ErrorApi.Metadata.g.cs");

        Assert.Empty(output.GeneratorDiagnostics);

        // [controller] token replaced, constraint stripped, literals lower-cased — the same key the
        // OpenAPI transformer computes from ApiDescription.RelativePath at runtime.
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/api/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[1] })",
            metadata,
            StringComparison.Ordinal);
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"POST\", \"/api/orders/{id}/pay\", new global::ErrorApi.ErrorDescriptor[] { _errors[0], _errors[1] })",
            metadata,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_rooted_template_replaces_the_class_prefix()
    {
        const string controller = """
            using ErrorApi;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;

            namespace Shop;

            [ApiController]
            [Route("api/[controller]")]
            public sealed class HealthController : ControllerBase
            {
                [HttpGet("/health"), ProducesError("Orders.NotFound")]
                public IResult Check() => Results.Ok();
            }
            """;

        var metadata = GeneratorHarness.RunAndCompile(Domain, controller).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("case \"/health\":", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/health", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void The_action_token_and_multiple_verbs_both_resolve()
    {
        const string controller = """
            using System;
            using ErrorApi;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;

            namespace Shop;

            [Route("api/[controller]/[action]")]
            public sealed class LegacyOrdersController(IOrderService service) : ControllerBase
            {
                [HttpPut]
                [HttpPatch]
                public IResult Amend(Guid id) => service.GetById(id).ToHttpResult();

                [NonAction]
                public IResult NotAnEndpoint() => service.GetById(Guid.Empty).ToHttpResult();
            }
            """;

        var metadata = GeneratorHarness.RunAndCompile(Domain, controller).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"PATCH\", \"/api/legacyorders/amend\"",
            metadata,
            StringComparison.Ordinal);
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"PUT\", \"/api/legacyorders/amend\"",
            metadata,
            StringComparison.Ordinal);
        Assert.DoesNotContain("notanendpoint", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void Controllers_and_minimal_apis_share_one_document()
    {
        const string mixed = """
            using System;
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.AspNetCore.Routing;

            namespace Shop;

            [ApiController]
            [Route("api/orders")]
            public sealed class OrdersController(IOrderService service) : ControllerBase
            {
                [HttpGet("{id:guid}")]
                public IResult GetById(Guid id) => service.GetById(id).ToHttpResult();
            }

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapPost("/api/orders/{id:guid}/pay", (Guid id, IOrderService s) => s.Pay(id).ToHttpResult());
            }
            """;

        var metadata = GeneratorHarness.RunAndCompile(Domain, mixed).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("new global::ErrorApi.EndpointErrors(\"GET\", \"/api/orders/{id}\"", metadata, StringComparison.Ordinal);
        Assert.Contains("new global::ErrorApi.EndpointErrors(\"POST\", \"/api/orders/{id}/pay\"", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void A_controller_project_counts_as_an_api_for_the_unreachable_rule()
    {
        // Controllers are endpoints, so a catalog entry no action reaches must still fire EAPI010.
        const string controller = """
            using System;
            using ErrorApi;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;

            namespace Shop;

            [ApiController]
            [Route("api/orders")]
            public sealed class OrdersController(IOrderService service) : ControllerBase
            {
                [HttpGet("{id:guid}")]
                public IResult GetById(Guid id) => service.GetById(id).ToHttpResult();

                [HttpPost("{id:guid}/pay")]
                public IResult Pay(Guid id) => service.Pay(id).ToHttpResult();
            }
            """;

        const string extra = """
            using ErrorApi;

            namespace Shop;

            [ErrorCatalog("Common")]
            public static partial class CommonErrors
            {
                [Error(429)] public static partial Error RateLimited { get; }
            }
            """;

        var output = GeneratorHarness.RunAndCompile(Domain, controller, extra);

        var reported = Assert.Single(output.GeneratorDiagnostics, d => d.Id == "EAPI010");
        Assert.Contains("Common.RateLimited", reported.GetMessage(), StringComparison.Ordinal);
    }
}
