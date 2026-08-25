using Microsoft.CodeAnalysis;
using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// The walk has to re-run on every edit — it reads arbitrary method bodies — but the pipeline funnels
/// into one value-equatable model, and everything downstream of that model must cache. These tests are
/// the regression gate on that boundary: if the model stops comparing equal across a no-op edit, every
/// keystroke in the IDE starts re-emitting sources and re-parsing generated files.
/// </summary>
public sealed class IncrementalityTests
{
    private static readonly string[] Fixture = [TestSources.Catalog, TestSources.Service, TestSources.Endpoints];

    [Fact]
    public void An_edit_that_changes_nothing_leaves_the_outputs_cached()
    {
        var result = GeneratorHarness.RunTwice(
            Fixture,
            compilation => compilation.AddSyntaxTrees(
                GeneratorHarness.ParseTree("internal static class Unrelated { }", "Unrelated.cs")));

        // The model stage re-ran (the compilation changed) but produced an equal model…
        var model = Assert.Single(result.TrackedSteps[ErrorApiGenerator.ModelStepName]);
        Assert.All(model.Outputs, output => Assert.Equal(IncrementalStepRunReason.Unchanged, output.Reason));

        // …so nothing downstream of it ran again.
        var outputs = result.TrackedOutputSteps
            .SelectMany(step => step.Value)
            .SelectMany(step => step.Outputs)
            .ToList();

        Assert.NotEmpty(outputs);
        Assert.All(outputs, output => Assert.Equal(IncrementalStepRunReason.Cached, output.Reason));
    }

    [Fact]
    public void An_edit_that_changes_the_contract_reaches_the_outputs()
    {
        var result = GeneratorHarness.RunTwice(
            Fixture,
            compilation => compilation.AddSyntaxTrees(GeneratorHarness.ParseTree(
                """
                using ErrorApi;
                using Microsoft.AspNetCore.Builder;
                using Microsoft.AspNetCore.Http;
                using Microsoft.AspNetCore.Routing;

                namespace Shop.Orders;

                public static class ExtraEndpoints
                {
                    public static void Map(IEndpointRouteBuilder app) =>
                        app.MapGet("/extra", (IOrderService s) => s.GetById(System.Guid.Empty).ToHttpResult());
                }
                """,
                "ExtraEndpoints.cs")));

        var model = Assert.Single(result.TrackedSteps[ErrorApiGenerator.ModelStepName]);
        Assert.All(model.Outputs, output => Assert.Equal(IncrementalStepRunReason.Modified, output.Reason));
    }
}
