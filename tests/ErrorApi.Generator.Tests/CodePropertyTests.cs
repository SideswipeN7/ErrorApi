using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// <c>[Error(409, Code = "VersionFail")]</c>: the explicit code as a named property, beside the
/// positional <c>[Error("VersionFail", 409)]</c>. Read by the catalog parser and by the walker alike,
/// and taken verbatim — explicit means explicit, no catalog prefix.
/// </summary>
public sealed class CodePropertyTests
{
    [Fact]
    public void The_code_property_is_read_by_the_parser_and_the_walker()
    {
        const string source = """
            using System;
            using ErrorApi;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            namespace Shop;

            [ErrorCatalog("Orders")]
            public static partial class OrderErrors
            {
                // An annotated type: the walker resolves the code when it meets the throw.
                [Error(409, Code = "VersionFail")]
                public sealed class StaleVersionException : Exception;

                // A generated member: the parser resolves the code when it writes the body.
                [Error(422, Code = "Orders.Mismatch")]
                public static partial Error AmountMismatch { get; }
            }

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app)
                {
                    app.MapPut("/orders/{id:guid}", (Guid id) => Save(id));
                    app.MapPost("/orders/{id:guid}/pay", (Guid id) => OrderErrors.AmountMismatch.ToProblem());
                }

                private static IResult Save(Guid id) => throw new OrderErrors.StaleVersionException();
            }
            """;

        var output = GeneratorHarness.RunAndCompile(source);

        Assert.Empty(output.GeneratorDiagnostics);

        var metadata = output.Source("ErrorApi.Metadata.g.cs");
        // Verbatim, not "Orders.VersionFail" — and indexed by code: Orders.Mismatch = 0, VersionFail = 1.
        Assert.DoesNotContain("Orders.VersionFail", metadata, StringComparison.Ordinal);
        Assert.Contains("new global::ErrorApi.ErrorDescriptor(\"VersionFail\", 409, \"Stale version\"", metadata, StringComparison.Ordinal);
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"PUT\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[1] })",
            metadata,
            StringComparison.Ordinal);
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"POST\", \"/orders/{id}/pay\", new global::ErrorApi.ErrorDescriptor[] { _errors[0] })",
            metadata,
            StringComparison.Ordinal);
    }
}
