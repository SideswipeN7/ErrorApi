using ErrorApi.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// <c>AddErrorApi(x =&gt; x.ErrorCodeDescriptionEnabled(false))</c> and the visibility filters shape
/// what the model <em>documents</em> — OpenAPI, the catalog listing, the TS contract — while the
/// runtime lookups the adapters answer through stay untouched. Visibility is a documentation decision,
/// never a behaviour change.
/// </summary>
[Collection("ambient-metadata")]
public sealed class DocumentShapingTests
{
    private static IErrorApiMetadata Register(Action<ErrorApiOptions> configure)
    {
        var services = new ServiceCollection();
        using (ErrorApiRuntime.Use(new FakeMetadata()))
        {
            ErrorApiRegistration.Register(services, new FakeMetadata(), configure);
        }

        return services.BuildServiceProvider().GetRequiredService<IErrorApiMetadata>();
    }

    [Fact]
    public void Disabling_descriptions_strips_the_prose_from_documentation_surfaces()
    {
        var metadata = Register(x => x.ErrorCodeDescriptionEnabled(false));

        // The documentation surfaces lose the prose…
        Assert.All(metadata.AllErrors, e => Assert.Null(e.Description));
        Assert.True(metadata.TryGetEndpointErrors("GET", "/orders/{id}", out var errors));
        Assert.Null(Assert.Single(errors).Description);

        // …everything else about the entry survives…
        Assert.Equal("Order not found", errors[0].Title);
        Assert.Equal(404, errors[0].StatusCode);

        // …and the runtime lookup still carries the full entry — the wire never exposed descriptions,
        // so there is nothing to hide there.
        Assert.Equal("No order exists for that id.", metadata.FindError("Orders.NotFound")!.Description);
    }

    [Fact]
    public void The_default_keeps_descriptions_and_wraps_nothing()
    {
        var metadata = Register(x => x.ErrorCodeDescriptionEnabled());

        Assert.IsType<FakeMetadata>(metadata);
        Assert.Equal("No order exists for that id.", metadata.AllErrors[0].Description);
    }

    [Fact]
    public void A_hidden_code_disappears_from_documentation_but_still_answers()
    {
        var metadata = Register(x => x.HideErrorCodes("Orders.AlreadyPaid"));

        // Gone from the catalog listing and from every endpoint's documented contract…
        Assert.DoesNotContain(metadata.AllErrors, e => e.Code == "Orders.AlreadyPaid");
        Assert.True(metadata.TryGetEndpointErrors("POST", "/orders/{id}/pay", out var errors));
        Assert.Equal("Orders.NotFound", Assert.Single(errors).Code);
        Assert.DoesNotContain(
            metadata.Endpoints,
            e => e.Errors.Any(error => error.Code == "Orders.AlreadyPaid"));

        // …but the endpoint still resolves it at runtime: hiding documents nothing away on the wire.
        Assert.NotNull(metadata.FindError("Orders.AlreadyPaid"));
    }

    [Fact]
    public void Filters_compose_and_each_must_pass()
    {
        var metadata = Register(x => x
            .FilterErrorCodes(e => e.StatusCode < 500)
            .HideErrorCodes("Orders.AlreadyPaid"));

        Assert.Equal("Orders.NotFound", Assert.Single(metadata.AllErrors).Code);
    }

    [Fact]
    public void The_TypeScript_contract_respects_the_shaping()
    {
        var shaped = Register(x => x.ErrorCodeDescriptionEnabled(false).HideErrorCodes("Orders.AlreadyPaid"));

        var contract = TypeScriptContractWriter.Write(shaped);

        Assert.DoesNotContain("Orders.AlreadyPaid", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("No order exists for that id.", contract, StringComparison.Ordinal);
        Assert.Contains("Orders.NotFound", contract, StringComparison.Ordinal);
    }
}
