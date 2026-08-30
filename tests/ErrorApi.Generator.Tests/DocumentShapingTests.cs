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

/// <summary>
/// <c>AddErrorApiResults()</c>: a handler returns <c>Result</c>/<c>Result&lt;T&gt;</c> directly and
/// the endpoint filter maps it exactly as <c>ToHttpResult()</c> would. The mapping core is pinned
/// here; the live behaviour rides in the integration suite through the Mediator sample.
/// </summary>
public sealed class ResultFilterTests
{
    [Fact]
    public void A_valued_success_maps_to_200_with_the_value()
    {
        var mapped = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IResult>(
            ErrorApi.AspNetCore.ErrorApiResultFilter.Map(Result<int>.Success(7)));

        Assert.Equal(200, ((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)mapped).StatusCode);
        Assert.Equal(7, ((Microsoft.AspNetCore.Http.IValueHttpResult)mapped).Value);
    }

    [Fact]
    public void A_valueless_success_maps_to_204()
    {
        var mapped = ErrorApi.AspNetCore.ErrorApiResultFilter.Map(Result.Success());

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NoContent>(mapped);
    }

    [Fact]
    public void A_failure_maps_to_the_problem_shape_with_the_code()
    {
        var mapped = ErrorApi.AspNetCore.ErrorApiResultFilter.Map(
            Result<int>.Failure(new Error("Orders.NotFound", 404, "Order not found")));

        var problem = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>(mapped);
        Assert.Equal(404, problem.StatusCode);
        Assert.Equal("Orders.NotFound", Assert.Contains(ResultHttpExtensions.CodeExtensionName, problem.ProblemDetails.Extensions));
    }

    [Fact]
    public void Anything_that_is_not_a_result_passes_through_untouched()
    {
        var value = new object();

        Assert.Same(value, ErrorApi.AspNetCore.ErrorApiResultFilter.Map(value));
        Assert.Null(ErrorApi.AspNetCore.ErrorApiResultFilter.Map(null));
    }
}

/// <summary>
/// <c>AddErrorApi(x =&gt; x.AddExceptionHandler(...))</c>: the lambda form of
/// <c>AddErrorApiExceptionHandler()</c>, so one call configures everything — still explicit,
/// never a side effect.
/// </summary>
[Collection("ambient-metadata")]
public sealed class ExceptionHandlerOptionTests
{
    [Fact]
    public void The_option_registers_the_handler_and_applies_the_tuning()
    {
        var services = new ServiceCollection();

        using (ErrorApiRuntime.Use(new FakeMetadata()))
        {
            ErrorApiRegistration.Register(
                services,
                new FakeMetadata(),
                x => x.AddExceptionHandler(o => o.UseExceptionMessageAsDetail = true));
        }

        var provider = services.BuildServiceProvider();

        Assert.Contains(
            provider.GetServices<Microsoft.AspNetCore.Diagnostics.IExceptionHandler>(),
            handler => handler is ErrorApiExceptionHandler);
        Assert.True(provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<ErrorApiExceptionOptions>>()
            .Value.UseExceptionMessageAsDetail);
    }

    [Fact]
    public void Without_the_option_no_handler_is_registered()
    {
        var services = new ServiceCollection();

        using (ErrorApiRuntime.Use(new FakeMetadata()))
        {
            ErrorApiRegistration.Register(services, new FakeMetadata(), x => { });
        }

        Assert.DoesNotContain(
            services.BuildServiceProvider().GetServices<Microsoft.AspNetCore.Diagnostics.IExceptionHandler>(),
            handler => handler is ErrorApiExceptionHandler);
    }
}
