using ErrorApi.Interop;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

using ArdalisResult = Ardalis.Result.Result;
using ResultStatus = Ardalis.Result.ResultStatus;
using ValidationError = Ardalis.Result.ValidationError;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// Runtime mapping of the Ardalis.Result adapter. Ardalis has no typed error and no code slot of its
/// own, so the code rides where there is room — <c>ValidationError.ErrorCode</c> or an error message —
/// and everything else falls back to the <c>ResultStatus</c>.
/// </summary>
public sealed class ArdalisResultAdapterTests
{
    private static readonly FakeMetadata Metadata = new();

    [Fact]
    public void A_validation_error_code_resolves_against_the_catalog()
    {
        var result = ArdalisResult.Invalid(new ValidationError
        {
            ErrorCode = "Orders.NotFound",
            ErrorMessage = "No such order.",
        });

        var error = ((Ardalis.Result.IResult)result).ToErrorApiError(Metadata);

        Assert.Equal("Orders.NotFound", error.Code);
        Assert.Equal(404, error.StatusCode);
        Assert.Equal("Order not found", error.Title);
        Assert.Equal("No such order.", error.Detail);
    }

    [Fact]
    public void An_error_message_that_is_a_known_code_is_the_second_way_in()
    {
        var result = ArdalisResult.Conflict("Orders.AlreadyPaid");

        var error = ((Ardalis.Result.IResult)result).ToErrorApiError(Metadata);

        Assert.Equal("Orders.AlreadyPaid", error.Code);
        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public void An_unrecognized_failure_answers_with_the_status_and_its_name()
    {
        var result = ArdalisResult.NotFound("nothing here");

        var error = ((Ardalis.Result.IResult)result).ToErrorApiError(Metadata);

        Assert.Equal("NotFound", error.Code);
        Assert.Equal(404, error.StatusCode);
        Assert.Equal("nothing here", error.Detail);
    }

    [Fact]
    public void The_status_map_matches_the_ardalis_aspnetcore_convention()
    {
        Assert.Equal(400, ArdalisResultHttpExtensions.StatusFor(ResultStatus.Invalid));
        Assert.Equal(401, ArdalisResultHttpExtensions.StatusFor(ResultStatus.Unauthorized));
        Assert.Equal(403, ArdalisResultHttpExtensions.StatusFor(ResultStatus.Forbidden));
        Assert.Equal(404, ArdalisResultHttpExtensions.StatusFor(ResultStatus.NotFound));
        Assert.Equal(409, ArdalisResultHttpExtensions.StatusFor(ResultStatus.Conflict));
        Assert.Equal(422, ArdalisResultHttpExtensions.StatusFor(ResultStatus.Error));
        Assert.Equal(503, ArdalisResultHttpExtensions.StatusFor(ResultStatus.Unavailable));
        Assert.Equal(500, ArdalisResultHttpExtensions.StatusFor(ResultStatus.CriticalError));
    }

    [Fact]
    public void A_success_maps_to_the_value_and_created_keeps_its_location()
    {
        global::Ardalis.Result.Result<int> ok = 7;
        Assert.Equal(7, Assert.IsType<Ok<int>>(ok.ToHttpResult()).Value);

        var created = global::Ardalis.Result.Result<int>.Created(7, "/things/7");
        Assert.Equal("/things/7", Assert.IsType<Created<int>>(created.ToHttpResult()).Location);
    }

    [Fact]
    public void A_failure_carries_its_code_as_a_problem_extension()
    {
        ErrorApiRuntime.Metadata = Metadata;
        try
        {
            global::Ardalis.Result.Result<int> result = ArdalisResult.NotFound("Orders.NotFound");

            var problem = Assert.IsType<ProblemHttpResult>(result.ToHttpResult());

            Assert.Equal(404, problem.StatusCode);
            Assert.Equal("Orders.NotFound", Assert.Contains(ResultHttpExtensions.CodeExtensionName, problem.ProblemDetails.Extensions));
        }
        finally
        {
            ErrorApiRuntime.Metadata = null;
        }
    }

    [Fact]
    public void A_non_generic_success_maps_to_no_content()
    {
        Assert.IsType<NoContent>(ArdalisResult.Success().ToHttpResult());
        Assert.IsType<NoContent>(ArdalisResult.NoContent().ToHttpResult());
    }
}

/// <summary>
/// The generator's view: a factory catalog in Ardalis style — Declared members returning Ardalis
/// results, codes explicit on the attribute — reaches the endpoint contract like any other.
/// </summary>
public sealed class ArdalisResultDiscoveryTests
{
    private const string Source = """
        using System;
        using ErrorApi;
        using ErrorApi.Interop;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;

        namespace Shop;

        public static class OrderErrors
        {
            [ErrorApi.Error("Orders.NotFound", 404, Title = "Order not found")]
            public static Ardalis.Result.Result NotFound() => Ardalis.Result.Result.NotFound("Orders.NotFound");

            [ErrorApi.Error("Orders.AlreadyPaid", 409)]
            public static Ardalis.Result.Result AlreadyPaid() => Ardalis.Result.Result.Conflict("Orders.AlreadyPaid");
        }

        public interface IOrderService { Ardalis.Result.Result<int> Pay(Guid id); }

        public sealed class OrderService : IOrderService
        {
            public Ardalis.Result.Result<int> Pay(Guid id) =>
                id == Guid.Empty ? OrderErrors.NotFound() : OrderErrors.AlreadyPaid();
        }

        public static class Endpoints
        {
            public static void Map(IEndpointRouteBuilder app) =>
                app.MapPost("/orders/{id:guid}/pay", (Guid id, IOrderService s) => s.Pay(id).ToHttpResult());
        }
        """;

    [Fact]
    public void Factory_members_reach_the_endpoint_contract()
    {
        var output = GeneratorHarness.RunAndCompile(Source);

        Assert.Empty(output.GeneratorDiagnostics);
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"POST\", \"/orders/{id}/pay\", new global::ErrorApi.ErrorDescriptor[] { _errors[0], _errors[1] })",
            output.Source("ErrorApi.Metadata.g.cs"),
            StringComparison.Ordinal);
    }
}
