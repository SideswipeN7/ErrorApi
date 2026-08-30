extern alias ErrorOrApi;
extern alias OneOfApi;
extern alias LanguageExtApi;
extern alias ExceptionsApi;
extern alias FluentResultsApi;
extern alias ArdalisApi;
extern alias CfeApi;
extern alias MediatorApi;
extern alias ValidationApi;

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// Many sample hosts share this test process, and the ambient ErrorApiRuntime.Metadata is one per
// process (first registration wins) — exactly the documented parallel-hosts limitation. The suite
// therefore runs serially and each assertion scopes the ambient model to its own host's.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ErrorApi.Integration.Tests;

/// <summary>
/// The samples share one contract on purpose — the same orders API written once per style — so one
/// assertion covers them all: the document lists the failures, and a live miss answers
/// <c>application/problem+json</c> carrying <c>Orders.NotFound</c>. Every sample boots here except
/// Wolverine, whose startup codegen is too heavy for a per-commit gate.
/// </summary>
internal static class SharedOrdersContract
{
    public static async Task AssertAsync<TProgram>(WebApplicationFactory<TProgram> factory)
        where TProgram : class
    {
        using var ambient = ErrorApiRuntime.Use(factory.Services.GetRequiredService<IErrorApiMetadata>());
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths");

        Assert.True(paths.GetProperty("/orders/{id}").GetProperty("get").GetProperty("responses").TryGetProperty("404", out _));

        var pay = paths.GetProperty("/orders/{id}/pay").GetProperty("post").GetProperty("responses");
        Assert.True(pay.TryGetProperty("404", out _));
        Assert.True(pay.TryGetProperty("409", out _));

        using var missing = await client.GetAsync($"/orders/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
        Assert.Equal("Orders.NotFound", body.RootElement.GetProperty("code").GetString());
    }
}

public sealed class ErrorOrSampleTests(WebApplicationFactory<ErrorOrApi::Program> factory)
    : IClassFixture<WebApplicationFactory<ErrorOrApi::Program>>
{
    [Fact]
    public Task The_shared_contract_holds() => SharedOrdersContract.AssertAsync(factory);
}

public sealed class OneOfSampleTests(WebApplicationFactory<OneOfApi::Program> factory)
    : IClassFixture<WebApplicationFactory<OneOfApi::Program>>
{
    [Fact]
    public Task The_shared_contract_holds() => SharedOrdersContract.AssertAsync(factory);
}

public sealed class LanguageExtSampleTests(WebApplicationFactory<LanguageExtApi::Program> factory)
    : IClassFixture<WebApplicationFactory<LanguageExtApi::Program>>
{
    [Fact]
    public Task The_shared_contract_holds() => SharedOrdersContract.AssertAsync(factory);
}

public sealed class ExceptionsSampleTests(WebApplicationFactory<ExceptionsApi::Program> factory)
    : IClassFixture<WebApplicationFactory<ExceptionsApi::Program>>
{
    [Fact]
    public Task The_shared_contract_holds() => SharedOrdersContract.AssertAsync(factory);
}

public sealed class FluentResultsSampleTests(WebApplicationFactory<FluentResultsApi::Program> factory)
    : IClassFixture<WebApplicationFactory<FluentResultsApi::Program>>
{
    [Fact]
    public Task The_shared_contract_holds() => SharedOrdersContract.AssertAsync(factory);
}

public sealed class ArdalisSampleTests(WebApplicationFactory<ArdalisApi::Program> factory)
    : IClassFixture<WebApplicationFactory<ArdalisApi::Program>>
{
    [Fact]
    public Task The_shared_contract_holds() => SharedOrdersContract.AssertAsync(factory);
}

public sealed class CfeSampleTests(WebApplicationFactory<CfeApi::Program> factory)
    : IClassFixture<WebApplicationFactory<CfeApi::Program>>
{
    [Fact]
    public Task The_shared_contract_holds() => SharedOrdersContract.AssertAsync(factory);
}

/// <summary>
/// The Mediator sample also carries the direct-return demo: its GET handler returns
/// <c>Result&lt;Order&gt;</c> with no mapping call, and <c>AddErrorApiResults()</c> answers.
/// </summary>
public sealed class MediatorSampleTests(WebApplicationFactory<MediatorApi::Program> factory)
    : IClassFixture<WebApplicationFactory<MediatorApi::Program>>
{
    [Fact]
    public Task The_shared_contract_holds() => SharedOrdersContract.AssertAsync(factory);

    [Fact]
    public async Task The_direct_return_documents_the_value_not_the_wrapper()
    {
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

        // The 200 of the direct-return endpoint describes Order — the convention rewrote the metadata
        // so the Result<T> wrapper never leaks into the document.
        var ok = document.RootElement.GetProperty("paths").GetProperty("/orders/{id}")
            .GetProperty("get").GetProperty("responses").GetProperty("200");
        var schema = ok.GetProperty("content").GetProperty("application/json").GetProperty("schema");

        Assert.DoesNotContain("isSuccess", schema.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The validation sample has its own shape: every endpoint behind the dispatcher documents the
/// pipeline behaviour's 400 alongside its own failures.
/// </summary>
public sealed class ValidationSampleTests(WebApplicationFactory<ValidationApi::Program> factory)
    : IClassFixture<WebApplicationFactory<ValidationApi::Program>>
{
    [Fact]
    public async Task Every_dispatching_endpoint_documents_the_behaviours_400()
    {
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths");

        Assert.True(paths.GetProperty("/orders").GetProperty("post").GetProperty("responses").TryGetProperty("400", out _));
        Assert.True(paths.GetProperty("/orders/{id}/cancel").GetProperty("post").GetProperty("responses").TryGetProperty("400", out _));
    }
}
