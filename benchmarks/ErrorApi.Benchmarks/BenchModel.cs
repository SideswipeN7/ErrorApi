using ErrorApi;
using Microsoft.AspNetCore.Builder;

namespace ErrorApi.Benchmarks;

/// <summary>
/// The catalog the benchmarks resolve against. Generated exactly like an application's: the generator
/// runs over this assembly, so <c>FindError</c>, <c>FindErrorForInstance</c> and
/// <c>TryGetEndpointErrors</c> below are the real emitted switches, not stand-ins.
/// </summary>
[ErrorCatalog("Bench")]
public static partial class BenchErrors
{
    [Error(404, Title = "Not found")]
    public static partial Error NotFound { get; }

    [Error(409, Title = "Conflict")]
    public static partial Error Conflict { get; }

    [Error(422, Title = "Invalid")]
    public static partial Error Invalid { get; }
}

/// <summary>A failure identified by its type — the shape OneOf and CSharpFunctionalExtensions resolve by instance.</summary>
[Error("Bench.Typed", 422, Title = "Typed failure")]
public sealed record TypedFailure(Guid Id);

/// <summary>The language-ext shape: an annotated <c>Expected</c> subclass.</summary>
[Error("Bench.Gone", 410, Title = "Gone")]
public sealed record BenchGone(Guid Id) : LanguageExt.Common.Expected("gone", 410);

/// <summary>The FluentResults shape: an annotated <c>Error</c> subclass.</summary>
[Error("Bench.Fluent", 409, Title = "Fluent conflict")]
public sealed class BenchFluent() : FluentResults.Error("conflict");

/// <summary>
/// Never invoked — the generator reads the <c>Map*</c> call sites at compile time, which is all the
/// endpoint-lookup benchmarks need to get a real generated endpoint table.
/// </summary>
internal static class BenchEndpoints
{
    public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
    {
        app.MapGet("/bench/{id:guid}", (Guid id) => Get(id).ToHttpResult());
        app.MapPost("/bench/{id:guid}/pay", (Guid id) => Pay(id).ToHttpResult());
    }

    private static Result<int> Get(Guid id) => id == Guid.Empty ? BenchErrors.NotFound : 1;

    private static Result Pay(Guid id) => id == Guid.Empty ? BenchErrors.Conflict : Result.Success();
}
