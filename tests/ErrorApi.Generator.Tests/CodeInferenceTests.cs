using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// <c>[Error(404)]</c> asks the generator to work the code out. These tests pin the three places it
/// looks, and the order it looks in.
/// </summary>
public sealed class CodeInferenceTests
{
    /// <summary>A stand-in for ErrorOr's factory: what matters is a string parameter called <c>code</c>.</summary>
    private const string Factory = """
        namespace Foreign;

        public readonly struct Failure
        {
            private Failure(string code, string description)
            {
                Code = code;
                Description = description;
            }

            public string Code { get; }

            public string Description { get; }

            public static Failure NotFound(string code, string description) => new(code, description);
        }
        """;

    [Fact]
    public void The_code_is_read_out_of_the_body_when_the_body_names_one()
    {
        const string source = """
            using ErrorApi;
            using Foreign;

            namespace Shop;

            public static class OrderErrors
            {
                [Error(404)]
                public static Failure NotFound => Failure.NotFound("Orders.NotFound", "No such order.");
            }
            """;

        var metadata = GeneratorHarness.RunAndCompile(Factory, source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("\"Orders.NotFound\", 404, \"Not found\"", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_argument_is_matched_by_name_not_position()
    {
        const string source = """
            using ErrorApi;
            using Foreign;

            namespace Shop;

            public static class OrderErrors
            {
                [Error(409)]
                public static Failure AlreadyPaid =>
                    Failure.NotFound(description: "Already paid.", code: "Orders.AlreadyPaid");
            }
            """;

        var metadata = GeneratorHarness.RunAndCompile(Factory, source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("\"Orders.AlreadyPaid\", 409, \"Already paid\"", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_body_the_code_comes_from_the_catalog_name_and_the_member_name()
    {
        const string source = """
            using ErrorApi;

            namespace Shop;

            public static partial class OrderErrors
            {
                [Error(404)]
                public static partial Error NotFound { get; }
            }
            """;

        var output = GeneratorHarness.RunAndCompile(source);

        // "OrderErrors" carries no information a code needs, so the suffix is dropped.
        Assert.Contains("\"Order.NotFound\", 404, \"Not found\"", output.Source("ErrorApi.Metadata.g.cs"), StringComparison.Ordinal);
        Assert.Contains("public const string NotFound = \"Order.NotFound\";", output.Source("Shop.OrderErrors.Catalog.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorCatalog_sets_the_prefix_for_the_whole_catalog()
    {
        const string source = """
            using ErrorApi;

            namespace Shop;

            [ErrorCatalog("Orders")]
            public static partial class OrderErrors
            {
                [Error(404)]
                public static partial Error NotFound { get; }

                [Error(409)]
                public static partial Error AlreadyPaid { get; }
            }
            """;

        var metadata = GeneratorHarness.RunAndCompile(source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("\"Orders.AlreadyPaid\", 409", metadata, StringComparison.Ordinal);
        Assert.Contains("\"Orders.NotFound\", 404", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void An_annotated_type_uses_its_own_name_when_no_prefix_is_declared()
    {
        const string source = """
            using ErrorApi;

            namespace Shop;

            [Error(404)]
            public sealed record OrderNotFound(System.Guid Id);
            """;

        var metadata = GeneratorHarness.RunAndCompile(source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("\"OrderNotFound\", 404, \"Order not found\"", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void An_assembly_level_prefix_reaches_annotated_types()
    {
        const string source = """
            using ErrorApi;

            [assembly: ErrorCatalog("Shop")]

            namespace Shop;

            [Error(404)]
            public sealed record OrderNotFound(System.Guid Id);
            """;

        var metadata = GeneratorHarness.RunAndCompile(source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("\"Shop.OrderNotFound\", 404", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void An_explicit_code_still_wins_over_everything()
    {
        const string source = """
            using ErrorApi;
            using Foreign;

            namespace Shop;

            [ErrorCatalog("Orders")]
            public static class OrderErrors
            {
                [Error("Legacy.Code", 404, Title = "Kept for compatibility")]
                public static Failure NotFound => Failure.NotFound("Legacy.Code", "No such order.");
            }
            """;

        var output = GeneratorHarness.RunAndCompile(Factory, source);

        Assert.Empty(output.GeneratorDiagnostics);
        Assert.Contains("\"Legacy.Code\", 404, \"Kept for compatibility\"", output.Source("ErrorApi.Metadata.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void EAPI008_reports_a_declared_code_that_the_body_contradicts()
    {
        const string source = """
            using ErrorApi;
            using Foreign;

            namespace Shop;

            public static class OrderErrors
            {
                [Error("Orders.NotFound", 404)]
                public static Failure NotFound => Failure.NotFound("Orders.Missing", "No such order.");
            }
            """;

        var diagnostic = Assert.Single(GeneratorHarness.Run(Factory, source).GeneratorDiagnostics, d => d.Id == "EAPI008");

        Assert.Contains("Orders.NotFound", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Orders.Missing", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NotFound", "Not found")]
    [InlineData("AlreadyPaid", "Already paid")]
    [InlineData("AmountMismatch", "Amount mismatch")]
    [InlineData("OrderNotFound", "Order not found")]
    [InlineData("RateLimited", "Rate limited")]
    [InlineData("Gone", "Gone")]
    public void The_title_is_the_name_read_as_a_sentence(string name, string expected)
    {
        var source = $$"""
            using ErrorApi;

            public static partial class Catalog
            {
                [Error(400)]
                public static partial Error {{name}} { get; }
            }
            """;

        var metadata = GeneratorHarness.RunAndCompile(source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains($"400, \"{expected}\"", metadata, StringComparison.Ordinal);
    }
}
