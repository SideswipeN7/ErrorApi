extern alias BasicApi;
extern alias ControllersApi;
extern alias ToolboxApi;

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ErrorApi.Integration.Tests;

/// <summary>
/// The samples, actually running: every claim the unit tests make about generated source is checked
/// here against a live OpenAPI document, a live problem response and a live TypeScript contract —
/// the same checks that used to be manual curls.
/// </summary>
public sealed class BasicSampleTests(WebApplicationFactory<BasicApi::Program> factory)
    : IClassFixture<WebApplicationFactory<BasicApi::Program>>
{
    [Fact]
    public async Task The_document_lists_every_reachable_failure()
    {
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths");

        var get = paths.GetProperty("/orders/{id}").GetProperty("get").GetProperty("responses");
        Assert.True(get.TryGetProperty("404", out var notFound));
        Assert.True(notFound.GetProperty("content").TryGetProperty("application/problem+json", out _));

        var pay = paths.GetProperty("/orders/{id}/pay").GetProperty("post").GetProperty("responses");
        Assert.True(pay.TryGetProperty("404", out _));
        Assert.True(pay.TryGetProperty("409", out _));
        Assert.True(pay.TryGetProperty("422", out _));
    }

    [Fact]
    public async Task A_live_failure_answers_problem_json_carrying_the_code()
    {
        using var ambient = ErrorApiRuntime.Use(factory.Services.GetRequiredService<IErrorApiMetadata>());
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Orders.NotFound", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task The_TypeScript_contract_is_served()
    {
        using var client = factory.CreateClient();
        var contract = await client.GetStringAsync("/openapi/errors.ts");

        Assert.Contains("Orders.NotFound", contract, StringComparison.Ordinal);
        Assert.Contains("\"GET /orders/{id}\"", contract, StringComparison.Ordinal);
    }
}

/// <summary>The controller surface produces the same contract as the Minimal API surface.</summary>
public sealed class ControllersSampleTests(WebApplicationFactory<ControllersApi::Program> factory)
    : IClassFixture<WebApplicationFactory<ControllersApi::Program>>
{
    [Fact]
    public async Task An_attribute_routed_action_documents_its_failures()
    {
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths");

        var get = paths.GetProperty("/orders/{id}").GetProperty("get").GetProperty("responses");
        Assert.True(get.TryGetProperty("404", out _));

        var pay = paths.GetProperty("/orders/{id}/pay").GetProperty("post").GetProperty("responses");
        Assert.True(pay.TryGetProperty("404", out _));
        Assert.True(pay.TryGetProperty("409", out _));
    }

    [Fact]
    public async Task A_live_action_failure_answers_problem_json()
    {
        using var ambient = ErrorApiRuntime.Use(factory.Services.GetRequiredService<IErrorApiMetadata>());
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Orders.NotFound", body.RootElement.GetProperty("code").GetString());
    }
}

/// <summary>
/// The cross-assembly story end to end: failures declared in Sample.Shared.Errors — including the
/// body-inferred "Very.Old.Retired" — documented and answered by an API that never sees that source.
/// </summary>
public sealed class ToolboxSampleTests(WebApplicationFactory<ToolboxApi::Program> factory)
    : IClassFixture<WebApplicationFactory<ToolboxApi::Program>>
{
    [Fact]
    public async Task Failures_from_the_referenced_assembly_are_documented()
    {
        using var client = factory.CreateClient();
        var document = await client.GetStringAsync("/openapi/v1.json");

        using var parsed = JsonDocument.Parse(document);
        var customer = parsed.RootElement.GetProperty("paths").GetProperty("/customers/{id}").GetProperty("get").GetProperty("responses");
        Assert.True(customer.TryGetProperty("404", out _));
        Assert.True(customer.TryGetProperty("410", out _));

        var ping = parsed.RootElement.GetProperty("paths").GetProperty("/gateway/ping").GetProperty("get").GetProperty("responses");
        Assert.True(ping.TryGetProperty("504", out _));

        // The body-inferred wire code crossed the boundary — not a name-derived guess.
        Assert.Contains("Very.Old.Retired", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failure_whose_type_lives_in_the_library_resolves_through_the_composed_model()
    {
        using var ambient = ErrorApiRuntime.Use(factory.Services.GetRequiredService<IErrorApiMetadata>());
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/customers/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Customers.NotFound", body.RootElement.GetProperty("code").GetString());
    }
}
