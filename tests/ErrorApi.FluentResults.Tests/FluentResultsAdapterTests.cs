using ErrorApi.Interop;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

using FluentError = FluentResults.Error;
using FluentResult = FluentResults.Result;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// FluentResults is the case where the library carries nothing to read: a failure is a message. The
/// identity has to come from modelling the failure as its own <c>Error</c> subclass, which is what the
/// library recommends anyway — and a type is a shape the catalog already understands.
/// </summary>
public sealed class FluentResultsDiscoveryTests
{
    private const string Source = """
        using System;
        using FluentResults;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;

        // No 'using ErrorApi;' here on purpose: both libraries export Result, so importing both makes
        // every mention of it ambiguous. FluentResults wins the plain name; ours is spelled out.
        using FluentError = FluentResults.Error;

        namespace Shop;

        [ErrorApi.ErrorCatalog("Orders")]
        public static class OrderErrors
        {
            [ErrorApi.Error(404, Description = "No order exists for that id.")]
            public sealed class NotFound(Guid id) : FluentError($"No order {id}.");

            [ErrorApi.Error(409)]
            public sealed class AlreadyPaid(Guid id) : FluentError($"Order {id} was already paid.");
        }

        public sealed record Order(Guid Id);

        public interface IOrderService
        {
            Result<Order> GetById(Guid id);
            Result Pay(Guid id);
        }

        public sealed class OrderService : IOrderService
        {
            public Result<Order> GetById(Guid id) =>
                id == Guid.Empty ? Result.Fail(new OrderErrors.NotFound(id)) : Result.Ok(new Order(id));

            public Result Pay(Guid id)
            {
                // The 404 reaches the pay endpoint through this call.
                var lookup = GetById(id);
                return lookup.IsFailed ? lookup.ToResult() : Result.Fail(new OrderErrors.AlreadyPaid(id));
            }
        }

        public static class Endpoints
        {
            public static void Map(IEndpointRouteBuilder app)
            {
                app.MapGet("/orders/{id:guid}", (Guid id, IOrderService s) =>
                    ErrorApi.Interop.FluentResultsHttpExtensions.ToHttpResult(s.GetById(id)));

                app.MapPost("/orders/{id:guid}/pay", (Guid id, IOrderService s) =>
                    ErrorApi.Interop.FluentResultsHttpExtensions.ToHttpResult(s.Pay(id)));
            }
        }
        """;

    [Fact]
    public void An_annotated_error_subclass_reaches_the_endpoint_contract()
    {
        var metadata = GeneratorHarness.RunAndCompile(Source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[1] })",
            metadata,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_failure_two_frames_below_the_handler_still_reaches_the_contract()
    {
        var metadata = GeneratorHarness.RunAndCompile(Source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"POST\", \"/orders/{id}/pay\", new global::ErrorApi.ErrorDescriptor[] { _errors[0], _errors[1] })",
            metadata,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Error_subclasses_are_resolved_by_type_without_reflection()
    {
        var metadata = GeneratorHarness.RunAndCompile(Source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("global::Shop.OrderErrors.NotFound => _errors[1],", metadata, StringComparison.Ordinal);
        Assert.Contains("\"Orders.NotFound\", 404, \"Not found\"", metadata, StringComparison.Ordinal);
    }
}

public sealed class FluentResultsMappingTests
{
    private sealed class OrderNotFound() : FluentError("No such order.");

    private static FakeMetadata Metadata()
    {
        var metadata = new FakeMetadata();
        metadata.ByType[typeof(OrderNotFound)] = FakeMetadata.NotFound;
        return metadata;
    }

    [Fact]
    public void An_annotated_error_resolves_to_the_documented_entry()
    {
        var mapped = new OrderNotFound().ToErrorApiError(Metadata());

        Assert.Equal("Orders.NotFound", mapped.Code);
        Assert.Equal(404, mapped.StatusCode);
        Assert.Equal("No such order.", mapped.Detail);
    }

    [Fact]
    public void A_code_in_the_metadata_is_the_second_way_in()
    {
        // FluentResults has no code of its own, so its metadata bag is the idiomatic place to put one.
        var error = new FluentError("Already paid.").WithMetadata(FluentResultsHttpExtensions.CodeMetadataKey, "Orders.AlreadyPaid");

        var mapped = error.ToErrorApiError(Metadata());

        Assert.Equal("Orders.AlreadyPaid", mapped.Code);
        Assert.Equal(409, mapped.StatusCode);
    }

    [Fact]
    public void A_bare_message_is_a_500_because_a_message_is_not_a_contract()
    {
        var mapped = FluentResult.Fail("something went wrong").ToErrorApiError(Metadata());

        Assert.Equal(500, mapped.StatusCode);
        Assert.Equal("something went wrong", mapped.Detail);
    }

    [Fact]
    public void The_first_error_decides_the_status_and_the_code()
    {
        var result = FluentResult.Fail(new OrderNotFound()).WithError(new FluentError("and another"));

        var mapped = result.ToErrorApiError(Metadata());

        Assert.Equal("Orders.NotFound", mapped.Code);
        Assert.Equal(404, mapped.StatusCode);
        Assert.Null(mapped.Extensions);
    }

    [Fact]
    public void The_rest_can_be_carried_as_an_extension_member_when_asked_for()
    {
        FluentResultsHttpExtensions.IncludeAllErrors = true;
        try
        {
            var result = FluentResult.Fail(new OrderNotFound()).WithError(new FluentError("and another"));

            var mapped = result.ToErrorApiError(Metadata());

            Assert.Equal(404, mapped.StatusCode);
            Assert.NotNull(mapped.Extensions);
            Assert.True(mapped.Extensions!.ContainsKey("errors"));
        }
        finally
        {
            FluentResultsHttpExtensions.IncludeAllErrors = false;
        }
    }

    [Fact]
    public void A_success_maps_to_the_value()
    {
        var mapped = Assert.IsType<Ok<int>>(FluentResult.Ok(7).ToHttpResult());

        Assert.Equal(7, mapped.Value);
    }

    [Fact]
    public void A_failure_maps_to_a_problem()
    {
        ErrorApiRuntime.Metadata = Metadata();
        try
        {
            var problem = Assert.IsType<ProblemHttpResult>(FluentResult.Fail(new OrderNotFound()).ToHttpResult());

            Assert.Equal(404, problem.StatusCode);
        }
        finally
        {
            ErrorApiRuntime.Metadata = null;
        }
    }

    [Fact]
    public void The_location_is_built_from_the_created_value()
    {
        ErrorApiRuntime.Metadata = Metadata();
        try
        {
            var created = Assert.IsType<Created<int>>(FluentResult.Ok(7).ToCreated(v => $"/things/{v}"));

            Assert.Equal("/things/7", created.Location);
        }
        finally
        {
            ErrorApiRuntime.Metadata = null;
        }
    }
}

