using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// <c>[Error]</c> has to sit on the declaration, which rules out anything from a package. A mapping says
/// the same thing from the outside, and everything downstream has to treat the result as an ordinary
/// type-identified entry.
/// </summary>
public sealed class ErrorMappingTests
{
    /// <summary>Stands in for a referenced package: types you cannot annotate.</summary>
    private const string Package = """
        namespace Payments;

        public sealed class CardDeclined
        {
            public string Reason { get; init; } = "";
        }

        public sealed class GatewayTimeoutException : System.Exception;

        public abstract class PaymentFailure;
        """;

    [Fact]
    public void A_mapped_type_becomes_a_catalog_entry()
    {
        const string source = """
            using ErrorApi;

            [assembly: ErrorMapping(typeof(Payments.CardDeclined), "Payments.CardDeclined", 402, Title = "Card declined")]
            """;

        var metadata = GeneratorHarness.RunAndCompile(Package, source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("\"Payments.CardDeclined\", 402, \"Card declined\"", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mapped_type_is_resolvable_by_instance_like_any_other()
    {
        const string source = """
            using ErrorApi;

            [assembly: ErrorMapping(typeof(Payments.CardDeclined), "Payments.CardDeclined", 402)]
            """;

        var metadata = GeneratorHarness.RunAndCompile(Package, source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("global::Payments.CardDeclined => _errors[0],", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void The_code_and_title_can_be_left_to_the_generator()
    {
        const string source = """
            using ErrorApi;

            [assembly: ErrorMapping(typeof(Payments.GatewayTimeoutException), 504)]
            """;

        var metadata = GeneratorHarness.RunAndCompile(Package, source).Source("ErrorApi.Metadata.g.cs");

        // The Exception suffix is noise on the wire here too.
        Assert.Contains("\"GatewayTimeout\", 504, \"Gateway timeout\"", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mapped_type_your_own_code_constructs_reaches_the_endpoint()
    {
        const string source = """
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            [assembly: ErrorMapping(typeof(Payments.CardDeclined), "Payments.CardDeclined", 402)]

            namespace Shop;

            public interface IPaymentService { object Charge(); }

            public sealed class PaymentService : IPaymentService
            {
                public object Charge() => new Payments.CardDeclined();
            }

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapPost("/payments", (IPaymentService service) => Results.Ok(service.Charge()));
            }
            """;

        var metadata = GeneratorHarness.RunAndCompile(Package, source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"POST\", \"/payments\", new global::ErrorApi.ErrorDescriptor[] { _errors[0] })",
            metadata,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProducesError_can_name_a_mapped_code_without_EAPI005()
    {
        // The point of the feature: a failure the library raises on its own is declared once as a
        // mapping and referenced by code wherever it surfaces.
        const string source = """
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            [assembly: ErrorMapping(typeof(Payments.GatewayTimeoutException), "Payments.GatewayTimeout", 504)]

            namespace Shop;

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app) =>
                    app.MapPost("/payments", [ProducesError("Payments.GatewayTimeout")] () => Results.Ok());
            }
            """;

        var output = GeneratorHarness.RunAndCompile(Package, source);

        Assert.Empty(output.GeneratorDiagnostics);
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"POST\", \"/payments\", new global::ErrorApi.ErrorDescriptor[] { _errors[0] })",
            output.Source("ErrorApi.Metadata.g.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_mapping_that_clashes_with_a_declared_code_is_reported()
    {
        const string source = """
            using ErrorApi;

            [assembly: ErrorMapping(typeof(Payments.CardDeclined), "Payments.CardDeclined", 402)]

            namespace Shop;

            public static partial class PaymentErrors
            {
                [Error("Payments.CardDeclined", 402)]
                public static partial Error CardDeclined { get; }
            }
            """;

        var diagnostic = Assert.Single(
            GeneratorHarness.Run(Package, source).GeneratorDiagnostics, d => d.Id == "EAPI001");

        Assert.Contains("Payments.CardDeclined", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_abstract_type_cannot_be_mapped()
    {
        const string source = """
            using ErrorApi;

            [assembly: ErrorMapping(typeof(Payments.PaymentFailure), 500)]
            """;

        // An abstract type cannot identify one failure, the same rule [Error] applies to a base record.
        Assert.Contains(GeneratorHarness.Run(Package, source).GeneratorDiagnostics, d => d.Id == "EAPI003");
    }

    [Fact]
    public void A_status_outside_the_http_range_is_reported()
    {
        const string source = """
            using ErrorApi;

            [assembly: ErrorMapping(typeof(Payments.CardDeclined), 42)]
            """;

        Assert.Contains(GeneratorHarness.Run(Package, source).GeneratorDiagnostics, d => d.Id == "EAPI004");
    }
}
