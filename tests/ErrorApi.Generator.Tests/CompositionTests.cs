using ErrorApi.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// <c>AddErrorApi(x =&gt; x.Include(...))</c>: several assemblies' models answering as one, the host's
/// own first. The point is the instance-type switch — a failure whose type is declared in Domain can
/// only be resolved by Domain's generated switch, and composition is what puts it in reach.
/// </summary>
[Collection("ambient-metadata")]
public sealed class CompositionTests
{
    private sealed record DomainFailure;

    private static FakeMetadata DomainModel()
    {
        var domain = new FakeMetadata();
        domain.ByType[typeof(DomainFailure)] = FakeMetadata.AlreadyPaid;
        return domain;
    }

    [Fact]
    public void The_host_model_wins_and_the_included_ones_fill_the_gaps()
    {
        var host = new FakeMetadata();
        var composite = new CompositeErrorApiMetadata([host, DomainModel()]);

        // The host answers what it knows…
        Assert.True(composite.TryGetEndpointErrors("GET", "/orders/{id}", out var errors));
        Assert.Equal("Orders.NotFound", Assert.Single(errors).Code);

        // …and the included model resolves the instance type the host has never heard of.
        Assert.Same(FakeMetadata.AlreadyPaid, composite.FindErrorForInstance(new DomainFailure()));
    }

    [Fact]
    public void AllErrors_unions_by_code_with_the_host_first()
    {
        var composite = new CompositeErrorApiMetadata([new FakeMetadata(), DomainModel()]);

        // Both models carry the same two codes; the union stays two, host's instances first.
        Assert.Equal(2, composite.AllErrors.Count);
        Assert.Equal("Orders.NotFound", composite.FindError("Orders.NotFound")!.Code);
    }

    [Fact]
    public void Registration_with_include_lands_a_composite_in_DI_and_on_the_static()
    {
        var services = new ServiceCollection();
        var host = new FakeMetadata();

        using (ErrorApiRuntime.Use(host))
        {
            ErrorApiRegistration.Register(services, host, x => x.Include(DomainModel()));

            var registered = services.BuildServiceProvider().GetRequiredService<IErrorApiMetadata>();

            Assert.IsType<CompositeErrorApiMetadata>(registered);
            Assert.Same(registered, ErrorApiRuntime.Metadata);
            Assert.NotNull(registered.FindErrorForInstance(new DomainFailure()));
        }
    }

    [Fact]
    public void Registration_without_includes_keeps_the_plain_model()
    {
        var services = new ServiceCollection();
        var host = new FakeMetadata();

        using (ErrorApiRuntime.Use(host))
        {
            ErrorApiRegistration.Register(services, host, x => { });

            Assert.Same(host, services.BuildServiceProvider().GetRequiredService<IErrorApiMetadata>());
        }
    }

    [Fact]
    public void The_first_registration_wins_in_DI_and_on_the_static_alike()
    {
        var services = new ServiceCollection();
        var first = new FakeMetadata();
        var second = new FakeMetadata();

        using (ErrorApiRuntime.Use(first))
        {
            ErrorApiRegistration.Register(services, first);
            ErrorApiRegistration.Register(services, second);

            // Two modules each calling AddErrorApi(): whichever ran first is the model — through DI
            // and through the ambient static the adapters read. They must never disagree.
            Assert.Same(first, services.BuildServiceProvider().GetRequiredService<IErrorApiMetadata>());
            Assert.Same(first, ErrorApiRuntime.Metadata);
        }
    }

    [Fact]
    public void Every_assembly_gets_a_public_model_accessor_under_its_own_name()
    {
        const string source = """
            using ErrorApi;

            [ErrorCatalog("Common")]
            public static partial class CommonErrors
            {
                [Error(429)] public static partial Error RateLimited { get; }
            }
            """;

        var metadata = GeneratorHarness.RunAndCompile(source).Source("ErrorApi.Metadata.g.cs");

        // The harness compiles as "ErrorApi.GeneratorTests.Subject" — the accessor namespace is the
        // assembly name, so two referenced assemblies can never collide.
        Assert.Contains("namespace ErrorApi.GeneratorTests.Subject", metadata, StringComparison.Ordinal);
        Assert.Contains("public static class ErrorApiModel", metadata, StringComparison.Ordinal);
        Assert.Contains(
            "public static global::ErrorApi.IErrorApiMetadata Metadata => global::ErrorApi.Generated.ErrorApiGenerated.Metadata;",
            metadata,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_namespace_sanitizer_twins_agree()
    {
        // The emitter derives the namespace at build time; IncludeFromAssemblies re-derives it at
        // startup. Same inputs, same answers — or the reflection convenience misses.
        Assert.Equal("My.Assembly_Name", ErrorApiOptions.SanitizeNamespace("My.Assembly-Name"));
        Assert.Equal("My._1Weird.Name", ErrorApiOptions.SanitizeNamespace("My.1Weird.Name"));
        Assert.Equal("ErrorApiAssembly", ErrorApiOptions.SanitizeNamespace(""));
    }
}
