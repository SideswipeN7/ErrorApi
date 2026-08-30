using ErrorApi.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// <see cref="Error"/> as a value: the With* copies, the extension bag, and the problem projection —
/// including the optional <c>type</c> URI template, which is process-wide state and so runs in the
/// serialized collection.
/// </summary>
[Collection("ambient-metadata")]
public sealed class ErrorPrimitiveTests
{
    [Fact]
    public void The_with_copies_change_one_thing_and_keep_the_rest()
    {
        var error = new Error("Orders.NotFound", 404, "Order not found", "detail");

        var retitled = error.WithTitle("Missing order");
        Assert.Equal("Missing order", retitled.Title);
        Assert.Equal("detail", retitled.Detail);

        var detailed = error.WithDetail("Order 7 is gone");
        Assert.Equal("Order 7 is gone", detailed.Detail);
        Assert.Equal("Order not found", detailed.Title);

        Assert.False(error.IsNone);
    }

    [Fact]
    public void Extensions_accumulate_across_WithExtension_copies_and_reach_the_problem()
    {
        var error = new Error("Orders.NotFound", 404)
            .WithExtension("traceId", "abc")
            .WithExtension("attempt", 2);

        Assert.Equal("abc", error.Extensions!["traceId"]);

        var problem = error.ToProblem();
        Assert.Equal("abc", problem.ProblemDetails.Extensions["traceId"]);
        Assert.Equal(2, problem.ProblemDetails.Extensions["attempt"]);
        Assert.Equal("Orders.NotFound", problem.ProblemDetails.Extensions[ResultHttpExtensions.CodeExtensionName]);
    }

    [Fact]
    public void The_type_uri_template_fills_problem_type_when_configured()
    {
        try
        {
            ResultHttpExtensions.ProblemTypeUriFormat = "https://errors.example.com/{0}";

            Assert.Equal(
                "https://errors.example.com/Orders.NotFound",
                new Error("Orders.NotFound", 404).ToProblem().ProblemDetails.Type);

            Assert.Equal(
                "https://errors.example.com/Orders.NotFound",
                ((ProblemDetails)new Error("Orders.NotFound", 404).ToProblemActionResult().Value!).Type);
        }
        finally
        {
            ResultHttpExtensions.ProblemTypeUriFormat = null;
        }
    }
}

/// <summary>The <see cref="Result"/> combinators, both arms each.</summary>
public sealed class ResultPrimitiveTests
{
    private static readonly Error NotFound = new("Orders.NotFound", 404, "Order not found");

    [Fact]
    public void Match_folds_both_arms()
    {
        Assert.Equal("ok", Result.Success().Match(() => "ok", e => e.Code));
        Assert.Equal("Orders.NotFound", Result.Failure(NotFound).Match(() => "ok", e => e.Code));
        Assert.Equal(8, Result<int>.Success(7).Match(v => v + 1, _ => -1));
        Assert.Equal(-1, Result<int>.Failure(NotFound).Match(v => v + 1, _ => -1));
    }

    [Fact]
    public void Map_and_Bind_propagate_the_failure_untouched()
    {
        Assert.Equal("7", Result<int>.Success(7).Map(v => v.ToString()).Value);
        Assert.Equal(NotFound, Result<int>.Failure(NotFound).Map(v => v.ToString()).Error);

        Assert.Equal(14, Result<int>.Success(7).Bind(v => Result<int>.Success(v * 2)).Value);
        Assert.Equal(NotFound, Result<int>.Failure(NotFound).Bind(v => Result<int>.Success(v * 2)).Error);
    }

    [Fact]
    public void WithoutValue_keeps_only_the_outcome()
    {
        Assert.True(Result<int>.Success(7).WithoutValue().IsSuccess);
        Assert.Equal(NotFound, Result<int>.Failure(NotFound).WithoutValue().Error);
    }

    [Fact]
    public void Reading_the_value_of_a_failure_names_the_code()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => Result<int>.Failure(NotFound).Value);

        Assert.Contains("Orders.NotFound", thrown.Message, StringComparison.Ordinal);
    }
}

/// <summary>The rest of the <c>ToHttpResult</c>/<c>ToCreated</c> family — every overload, both arms.</summary>
public sealed class ResultMappingSurfaceTests
{
    private static readonly Error NotFound = new("Orders.NotFound", 404);
    private static readonly Result<int> Ok7 = Result<int>.Success(7);
    private static readonly Result<int> Failed = Result<int>.Failure(NotFound);

    [Fact]
    public void The_shaped_success_and_the_fixed_location_answer()
    {
        Assert.IsType<NoContent>(Ok7.ToHttpResult(_ => Microsoft.AspNetCore.Http.TypedResults.NoContent()));
        Assert.Equal("/fixed", Assert.IsType<Created<int>>(Ok7.ToCreated("/fixed")).Location);
        Assert.IsType<ProblemHttpResult>(Failed.ToCreated("/fixed"));
        Assert.IsType<ProblemHttpResult>(Failed.ToCreatedAtUri(_ => throw new InvalidOperationException("never")));
    }

    [Fact]
    public void The_named_route_answers_with_and_without_route_values()
    {
        var withValues = Assert.IsType<CreatedAtRoute<int>>(
            Ok7.ToCreatedAtRoute("GetThing", v => new RouteValueDictionary { ["id"] = v }));
        Assert.Equal("GetThing", withValues.RouteName);

        var bare = Assert.IsType<CreatedAtRoute<int>>(Ok7.ToCreatedAtRoute("GetThing"));
        Assert.Equal("GetThing", bare.RouteName);

        Assert.IsType<ProblemHttpResult>(Failed.ToCreatedAtRoute("GetThing"));
        Assert.IsType<ProblemHttpResult>(Failed.ToCreatedAtRoute("GetThing", _ => throw new InvalidOperationException("never")));
    }

    [Fact]
    public async Task The_awaited_forms_mirror_their_synchronous_twins()
    {
        Assert.IsType<Ok<int>>(await Task.FromResult(Ok7).ToHttpResult());
        Assert.IsType<NoContent>(await Task.FromResult(Ok7).ToHttpResult(_ => Microsoft.AspNetCore.Http.TypedResults.NoContent()));
        Assert.IsType<NoContent>(await Task.FromResult(Result.Success()).ToHttpResult());
        Assert.Equal("/fixed", Assert.IsType<Created<int>>(await Task.FromResult(Ok7).ToCreated("/fixed")).Location);
        Assert.Equal("/things/7", Assert.IsType<Created<int>>(await Task.FromResult(Ok7).ToCreated(v => $"/things/{v}")).Location);
        Assert.IsType<CreatedAtRoute<int>>(
            await Task.FromResult(Ok7).ToCreatedAtRoute("GetThing", v => new RouteValueDictionary { ["id"] = v }));
        Assert.IsType<Ok<int>>(await new ValueTask<Result<int>>(Ok7).ToHttpResult());
        Assert.IsType<NoContent>(await new ValueTask<Result>(Result.Success()).ToHttpResult());
    }
}

/// <summary>The <c>TypedResults</c> twins' remaining overloads — both arms each.</summary>
public sealed class TypedResultSurfaceTests
{
    private static readonly Error NotFound = new("Orders.NotFound", 404);
    private static readonly Result<int> Ok7 = Result<int>.Success(7);
    private static readonly Result<int> Failed = Result<int>.Failure(NotFound);

    [Fact]
    public void The_created_family_answers_on_both_arms()
    {
        Assert.Equal("/fixed", Assert.IsType<Created<int>>(Ok7.ToTypedCreated("/fixed").Result).Location);
        Assert.Equal("/things/7", Assert.IsType<Created<int>>(
            Ok7.ToTypedCreatedAtUri(v => new Uri($"/things/{v}", UriKind.Relative)).Result).Location);
        Assert.Equal("GetThing", Assert.IsType<CreatedAtRoute<int>>(Ok7.ToTypedCreatedAtRoute("GetThing").Result).RouteName);

        Assert.IsType<ProblemHttpResult>(Failed.ToTypedCreated("/fixed").Result);
        Assert.IsType<ProblemHttpResult>(Failed.ToTypedCreatedAtUri(_ => throw new InvalidOperationException("never")).Result);
        Assert.IsType<ProblemHttpResult>(Failed.ToTypedCreatedAtRoute("GetThing").Result);
        Assert.IsType<ProblemHttpResult>(Result.Failure(NotFound).ToTypedResult().Result);
    }

    [Fact]
    public async Task The_awaited_forms_mirror_their_synchronous_twins()
    {
        Assert.IsType<ProblemHttpResult>((await Task.FromResult(Failed).ToTypedResult()).Result);
        Assert.IsType<ProblemHttpResult>((await Task.FromResult(Result.Failure(NotFound)).ToTypedResult()).Result);
        Assert.Equal("/fixed", Assert.IsType<Created<int>>((await Task.FromResult(Ok7).ToTypedCreated("/fixed")).Result).Location);
        Assert.IsType<CreatedAtRoute<int>>(
            (await Task.FromResult(Ok7).ToTypedCreatedAtRoute("GetThing", v => new RouteValueDictionary { ["id"] = v })).Result);
        Assert.IsType<Ok<int>>((await new ValueTask<Result<int>>(Ok7).ToTypedResult()).Result);
    }
}

/// <summary>The MVC vocabulary: identical mapping, spoken in <c>ActionResult</c>.</summary>
public sealed class ActionResultSurfaceTests
{
    private static readonly Error NotFound = new("Orders.NotFound", 404, "Order not found");

    [Fact]
    public void Both_arms_answer_in_MVCs_own_types()
    {
        var ok = Result<int>.Success(7).ToActionResult();
        Assert.Equal(7, ok.Value);

        var failed = Result<int>.Failure(NotFound).ToActionResult();
        var problem = Assert.IsType<ObjectResult>(failed.Result);
        Assert.Equal(404, problem.StatusCode);
        Assert.Equal("Orders.NotFound", ((ProblemDetails)problem.Value!).Extensions[ResultHttpExtensions.CodeExtensionName]);

        Assert.IsType<NoContentResult>(Result.Success().ToActionResult());
        Assert.IsType<ObjectResult>(Result.Failure(NotFound).ToActionResult());
    }

    [Fact]
    public void Created_builds_the_location_and_a_failure_never_reaches_the_lambda()
    {
        var created = Assert.IsType<CreatedResult>(
            Result<int>.Success(7).ToCreatedActionResult(v => $"/things/{v}").Result);
        Assert.Equal("/things/7", created.Location);

        Assert.IsType<ObjectResult>(
            Result<int>.Failure(NotFound).ToCreatedActionResult(_ => throw new InvalidOperationException("never")).Result);
    }

    [Fact]
    public async Task The_awaited_form_mirrors_the_synchronous_one()
    {
        var ok = await Task.FromResult(Result<int>.Success(7)).ToActionResult();
        Assert.Equal(7, ok.Value);
    }
}

/// <summary>The composite model's edges: construction contracts and lookup fallthrough.</summary>
public sealed class CompositeMetadataEdgeTests
{
    [Fact]
    public void Construction_rejects_nothing_to_compose()
    {
        Assert.Throws<ArgumentNullException>(() => new CompositeErrorApiMetadata(null!));
        Assert.Throws<ArgumentException>(() => new CompositeErrorApiMetadata([]));
    }

    [Fact]
    public void Lookups_fall_through_to_later_models_and_miss_honestly()
    {
        var composite = new CompositeErrorApiMetadata([new EmptyMetadata(), new FakeMetadata()]);

        Assert.Equal("Orders.NotFound", composite.FindError("Orders.NotFound")!.Code);
        Assert.Null(composite.FindError("Nope.Nothing"));
        Assert.Null(composite.FindErrorForInstance(new object()));
        Assert.True(composite.TryGetEndpointErrors("GET", "/orders/{id}", "anything", out var errors));
        Assert.Single(errors);
        Assert.False(composite.TryGetEndpointErrors("GET", "/nope", out _));
        Assert.Equal(3, composite.Endpoints.Count);
    }

    private sealed class EmptyMetadata : IErrorApiMetadata
    {
        public IReadOnlyList<ErrorDescriptor> AllErrors => [];

        public IReadOnlyList<EndpointErrors> Endpoints => [];

        public ErrorDescriptor? FindError(string code) => null;

        public ErrorDescriptor? FindErrorForInstance(object? instance) => null;

        public bool TryGetEndpointErrors(string httpMethod, string routePattern, out IReadOnlyList<ErrorDescriptor> errors)
        {
            errors = [];
            return false;
        }

        public bool TryGetEndpointErrors(string httpMethod, string routePattern, string? group, out IReadOnlyList<ErrorDescriptor> errors)
        {
            errors = [];
            return false;
        }
    }
}

/// <summary><see cref="ErrorApiRuntime.Resolve"/> and <see cref="ErrorApiRuntime.Current"/>.</summary>
[Collection("ambient-metadata")]
public sealed class RuntimeResolveTests
{
    private sealed record TypedFailure;

    [Fact]
    public void Resolve_goes_instance_then_code_then_fallback()
    {
        var metadata = new FakeMetadata();
        metadata.ByType[typeof(TypedFailure)] = FakeMetadata.AlreadyPaid;

        using (ErrorApiRuntime.Use(metadata))
        {
            Assert.Equal(409, ErrorApiRuntime.Resolve(new TypedFailure()).StatusCode);
            Assert.Equal(404, ErrorApiRuntime.Resolve(null, code: "Orders.NotFound").StatusCode);

            var fallback = ErrorApiRuntime.Resolve(new object(), fallbackTitle: "Unmapped", fallbackStatus: 502);
            Assert.Equal(502, fallback.StatusCode);
            Assert.Equal("Unmapped", fallback.Title);
        }
    }

    [Fact]
    public void Current_throws_without_a_model_and_answers_with_one()
    {
        var previous = ErrorApiRuntime.Metadata;
        ErrorApiRuntime.Metadata = null;
        try
        {
            Assert.Throws<InvalidOperationException>(() => ErrorApiRuntime.Current);
        }
        finally
        {
            ErrorApiRuntime.Metadata = previous;
        }

        using (ErrorApiRuntime.Use(new FakeMetadata()))
        {
            Assert.NotNull(ErrorApiRuntime.Current);
        }
    }
}

/// <summary>The build-step contract emitter and the options' argument contracts.</summary>
[Collection("ambient-metadata")]
public sealed class ContractEmissionTests
{
    [Fact]
    public void TryEmitErrorContract_writes_the_file_only_when_asked()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IErrorApiMetadata>(new FakeMetadata());
        using var app = builder.Build();

        var path = Path.Combine(Path.GetTempPath(), $"errorapi-{Guid.NewGuid():N}.ts");
        try
        {
            Assert.False(((IApplicationBuilder)app).TryEmitErrorContract(["--something-else"], path));
            Assert.False(File.Exists(path));

            Assert.True(((IApplicationBuilder)app).TryEmitErrorContract(["--emit-error-contract", path]));
            Assert.Contains("Orders.NotFound", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_options_reject_impossible_arguments()
    {
        var options = new ErrorApiOptions();

        Assert.Throws<ArgumentNullException>(() => options.Include(null!));
        Assert.Throws<ArgumentException>(() => options.Include([null!]));
        Assert.Throws<ArgumentNullException>(() => options.FilterErrorCodes(null!));

        // An assembly that never ran the generator has no model to find.
        Assert.Throws<InvalidOperationException>(() => options.IncludeFromAssemblies(typeof(string).Assembly));
    }
}

/// <summary>
/// The flow helpers: <c>Switch</c> runs exactly one branch and ends the flow;
/// <c>OnSuccess</c>/<c>OnFailure</c> run a side effect and hand the same result back, so a chain
/// keeps flowing. Every awaited twin mirrors its synchronous one.
/// </summary>
public sealed class ResultFlowTests
{
    private static readonly Error NotFound = new("Orders.NotFound", 404);

    [Fact]
    public void Switch_runs_exactly_one_branch()
    {
        var seen = new List<string>();

        Result<int>.Success(7).Switch(v => seen.Add($"ok:{v}"), e => seen.Add($"err:{e.Code}"));
        Result<int>.Failure(NotFound).Switch(v => seen.Add($"ok:{v}"), e => seen.Add($"err:{e.Code}"));
        Result.Success().Switch(() => seen.Add("ok"), e => seen.Add($"err:{e.Code}"));
        Result.Failure(NotFound).Switch(() => seen.Add("ok"), e => seen.Add($"err:{e.Code}"));

        Assert.Equal(["ok:7", "err:Orders.NotFound", "ok", "err:Orders.NotFound"], seen);
    }

    [Fact]
    public void OnSuccess_and_OnFailure_fire_on_their_own_arm_and_hand_the_result_back()
    {
        var seen = new List<string>();

        var ok = Result<int>.Success(7)
            .OnSuccess(v => seen.Add($"ok:{v}"))
            .OnFailure(e => seen.Add($"err:{e.Code}"));
        Assert.Equal(7, ok.Value);

        var failed = Result<int>.Failure(NotFound)
            .OnSuccess(v => seen.Add($"ok:{v}"))
            .OnFailure(e => seen.Add($"err:{e.Code}"));
        Assert.Equal(NotFound, failed.Error);

        Result.Success().OnSuccess(() => seen.Add("unit-ok")).OnFailure(e => seen.Add("unit-err"));
        Result.Failure(NotFound).OnSuccess(() => seen.Add("unit-ok2")).OnFailure(e => seen.Add("unit-err"));

        Assert.Equal(["ok:7", "err:Orders.NotFound", "unit-ok", "unit-err"], seen);
    }

    [Fact]
    public async Task The_awaited_forms_flow_through_a_whole_chain()
    {
        var seen = new List<string>();

        var result = await Task.FromResult(Result<int>.Success(7))
            .OnSuccess(v => seen.Add($"ok:{v}"))
            .OnFailure(e => seen.Add("never"));
        Assert.Equal(7, result.Value);

        await Task.FromResult(Result<int>.Failure(NotFound)).Switch(v => seen.Add("never"), e => seen.Add($"err:{e.Code}"));
        await Task.FromResult(Result.Failure(NotFound)).OnFailure(e => seen.Add($"unit:{e.Code}"));
        await Task.FromResult(Result.Success()).OnSuccess(() => seen.Add("unit-ok"));
        await Task.FromResult(Result.Success()).Switch(() => seen.Add("switch-ok"), e => seen.Add("never"));

        await new ValueTask<Result<int>>(Result<int>.Success(7)).Switch(v => seen.Add($"vt:{v}"), e => seen.Add("never"));
        Assert.Equal(7, (await new ValueTask<Result<int>>(Result<int>.Success(7)).OnSuccess(v => seen.Add($"vt-on:{v}"))).Value);
        await new ValueTask<Result<int>>(Result<int>.Failure(NotFound)).OnFailure(e => seen.Add($"vt-err:{e.Code}"));
        await new ValueTask<Result>(Result.Success()).Switch(() => seen.Add("vt-unit"), e => seen.Add("never"));

        Assert.Equal(
            ["ok:7", "err:Orders.NotFound", "unit:Orders.NotFound", "unit-ok", "switch-ok", "vt:7", "vt-on:7", "vt-err:Orders.NotFound", "vt-unit"],
            seen);
    }
}
