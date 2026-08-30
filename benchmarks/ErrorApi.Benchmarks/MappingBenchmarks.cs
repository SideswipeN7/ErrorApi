using BenchmarkDotNet.Attributes;
using ErrorApi.Interop;
using Microsoft.AspNetCore.Http;

namespace ErrorApi.Benchmarks;

/// <summary>
/// The request-path cost of ErrorApi: mapping a result to an <see cref="IResult"/>, in the base
/// library and through every adapter. Failure paths run the catalog resolution plus the problem
/// construction; success paths show the adapter overhead over raw <c>TypedResults</c>.
/// </summary>
[MemoryDiagnoser]
public class MappingBenchmarks
{
    private static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private Result<int> _coreOk;
    private Result<int> _coreFail;
    private ErrorOr.ErrorOr<int> _errorOrOk;
    private ErrorOr.ErrorOr<int> _errorOrFail;
    private OneOf.OneOf<int, TypedFailure> _oneOfOk;
    private OneOf.OneOf<int, TypedFailure> _oneOfFail;
    private LanguageExt.Fin<int> _finOk;
    private LanguageExt.Fin<int> _finFail;
    private FluentResults.Result<int> _fluentOk = null!;
    private FluentResults.Result<int> _fluentFail = null!;
    private Ardalis.Result.Result<int> _ardalisOk = null!;
    private Ardalis.Result.Result<int> _ardalisFail = null!;
    private CSharpFunctionalExtensions.Result<int, TypedFailure> _cfeOk;
    private CSharpFunctionalExtensions.Result<int, TypedFailure> _cfeFail;
    private TypedFailure _typed = null!;

    [GlobalSetup]
    public void Setup()
    {
        ErrorApiRuntime.Metadata = global::ErrorApi.Generated.ErrorApiGenerated.Metadata;

        _typed = new TypedFailure(Id);
        _coreOk = 7;
        _coreFail = BenchErrors.NotFound;
        _errorOrOk = 7;
        _errorOrFail = ErrorOr.Error.NotFound(code: "Bench.NotFound", description: "No such thing.");
        _oneOfOk = 7;
        _oneOfFail = _typed;
        _finOk = 7;
        _finFail = LanguageExt.Fin<int>.Fail(new BenchGone(Id));
        _fluentOk = FluentResults.Result.Ok(7);
        _fluentFail = FluentResults.Result.Fail<int>(new BenchFluent());
        _ardalisOk = Ardalis.Result.Result<int>.Success(7);
        _ardalisFail = Ardalis.Result.Result<int>.NotFound("Bench.NotFound");
        _cfeOk = CSharpFunctionalExtensions.Result.Success<int, TypedFailure>(7);
        _cfeFail = CSharpFunctionalExtensions.Result.Failure<int, TypedFailure>(_typed);
    }

    // ---- the floor: what ASP.NET itself charges for the same responses --------------------------

    [Benchmark(Baseline = true)]
    public IResult Raw_TypedResults_Ok() => TypedResults.Ok(7);

    [Benchmark]
    public IResult Raw_TypedResults_Problem() => TypedResults.Problem(statusCode: 404, title: "Not found");

    // ---- the generated model's lookups ----------------------------------------------------------

    [Benchmark]
    public ErrorDescriptor? Lookup_FindError() => ErrorApiRuntime.Metadata!.FindError("Bench.NotFound");

    [Benchmark]
    public ErrorDescriptor? Lookup_FindError_miss() => ErrorApiRuntime.Metadata!.FindError("Nope.Nothing");

    [Benchmark]
    public ErrorDescriptor? Lookup_FindErrorForInstance() => ErrorApiRuntime.Metadata!.FindErrorForInstance(_typed);

    [Benchmark]
    public bool Lookup_TryGetEndpointErrors() =>
        ErrorApiRuntime.Metadata!.TryGetEndpointErrors("GET", "/bench/{id}", null, out _);

    // ---- base library ---------------------------------------------------------------------------

    [Benchmark]
    public IResult Core_Success() => _coreOk.ToHttpResult();

    [Benchmark]
    public IResult Core_Failure() => _coreFail.ToHttpResult();

    // ---- adapters, failure arm = catalog resolution + problem construction ----------------------

    [Benchmark]
    public IResult ErrorOr_Success() => _errorOrOk.ToHttpResult();

    [Benchmark]
    public IResult ErrorOr_Failure() => _errorOrFail.ToHttpResult();

    [Benchmark]
    public IResult OneOf_Success() => _oneOfOk.ToHttpResult();

    [Benchmark]
    public IResult OneOf_Failure() => _oneOfFail.ToHttpResult();

    [Benchmark]
    public IResult LanguageExt_Success() => _finOk.ToHttpResult();

    [Benchmark]
    public IResult LanguageExt_Failure() => _finFail.ToHttpResult();

    [Benchmark]
    public IResult FluentResults_Success() => _fluentOk.ToHttpResult();

    [Benchmark]
    public IResult FluentResults_Failure() => _fluentFail.ToHttpResult();

    [Benchmark]
    public IResult Ardalis_Success() => _ardalisOk.ToHttpResult();

    [Benchmark]
    public IResult Ardalis_Failure() => _ardalisFail.ToHttpResult();

    [Benchmark]
    public IResult Cfe_Success() => _cfeOk.ToHttpResult();

    [Benchmark]
    public IResult Cfe_Failure() => _cfeFail.ToHttpResult();
}
