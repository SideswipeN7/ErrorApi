using Xunit;

namespace ErrorApi.Generator.Tests;

public sealed class CatalogGenerationTests
{
    [Fact]
    public void Catalog_is_implemented_from_the_attributes()
    {
        var output = GeneratorHarness.RunAndCompile(TestSources.Catalog);

        Snapshot.Match(output.ToSnapshot(), nameof(Catalog_is_implemented_from_the_attributes));
    }

    [Fact]
    public void Nested_and_global_namespace_catalogs_keep_their_declaration_shape()
    {
        const string source = """
            using ErrorApi;

            public static partial class Outer
            {
                internal static partial class Inner
                {
                    [Error("Root.Broken", 500, Title = "Something broke")]
                    internal static partial Error Broken { get; }
                }
            }
            """;

        var output = GeneratorHarness.RunAndCompile(source);

        Snapshot.Match(output.ToSnapshot(), nameof(Nested_and_global_namespace_catalogs_keep_their_declaration_shape));
    }

    [Fact]
    public void Detail_template_binds_to_the_method_parameters()
    {
        var output = GeneratorHarness.RunAndCompile(TestSources.Catalog);
        var catalog = output.Source("Shop.Orders.OrderErrors.Catalog.g.cs");

        Assert.Contains(
            "string.Format(global::System.Globalization.CultureInfo.InvariantCulture, \"Order {0} was already paid.\", orderId)",
            catalog,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Codes_are_emitted_as_constants()
    {
        var output = GeneratorHarness.RunAndCompile(TestSources.Catalog);
        var catalog = output.Source("Shop.Orders.OrderErrors.Catalog.g.cs");

        Assert.Contains("public const string NotFound = \"Orders.NotFound\";", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public void Documented_detail_uses_parameter_names_instead_of_positions()
    {
        var output = GeneratorHarness.RunAndCompile(TestSources.Catalog);
        var metadata = output.Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("\"Order {orderId} was already paid.\"", metadata, StringComparison.Ordinal);
    }
}
