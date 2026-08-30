# Performance and native AOT

The measured request-path cost, the benchmark methodology, and the no-reflection guarantee.

## Performance

`benchmarks/ErrorApi.Benchmarks` measures the request-path cost — the generated lookups and the
`→ IResult` mapping, base library and every adapter — against raw `TypedResults` as the floor:

```bash
dotnet run -c Release --project benchmarks/ErrorApi.Benchmarks
```

Measured on .NET 10.0.9, x64 (i9-9900K), BenchmarkDotNet 0.15.4, in-process short-run job:

| Benchmark | Mean | Allocated |
| --- | ---: | ---: |
| `TypedResults.Ok(7)` *(the floor)* | 4.5 ns | 24 B |
| `TypedResults.Problem(404, ...)` *(the floor)* | 24.3 ns | 168 B |
| `FindError` (generated code switch) | 5.2 ns | 0 B |
| `FindErrorForInstance` (generated type switch) | 2.5 ns | 0 B |
| `TryGetEndpointErrors` (generated route switch) | 3.4 ns | 0 B |
| `Result<T>` success → `ToHttpResult` | 4.4 ns | 24 B |
| `Result<T>` failure → `ToHttpResult` | 60.1 ns | 304 B |
| Adapter success paths (ErrorOr, OneOf, language-ext, FluentResults, Ardalis, CFE) | 4.2–5.9 ns | 24 B |
| Adapter failure paths (catalog resolution + problem construction) | 64–81 ns | 304–328 B |

What the numbers say: the **success path costs the same as writing `TypedResults.Ok` by hand** — the
adapters add nothing measurable — the generated lookups are single-digit-nanosecond and
allocation-free, and a failure costs ~60–80 ns end to end, of which 24 ns is ASP.NET's own
`ProblemDetails` machinery. The first benchmark run also paid for itself twice: it caught
FluentResults' `IsFailed`/`Errors` running an allocating LINQ `OfType` per call (the adapter now scans
`Reasons` directly — its success path went from 52 ns / 120 B to 5.5 ns / 24 B), and it flagged
`ToProblem` building a temporary extensions dictionary that ASP.NET then copied (it now writes into
the `ProblemDetails` it constructs — the failure path halved, 130 → 60 ns and 576 → 304 B).

---

---

## Native AOT

Nothing in the runtime path uses reflection: the catalog is `const` data, the endpoint lookup is a `switch` over string literals, the type lookup is a pattern switch, and `Result → IResult` is a branch. Every package is marked `IsAotCompatible`; the sample builds with `PublishAot` enabled so the trim and AOT analyzers run over it in CI, not just in the README.


---

[← back to the README](../README.md)

## Per-framework results (CI)

The same suite runs on net8.0, net9.0 and net10.0 on every main push (Ubuntu, shared runner — read
these as relative numbers; the absolute table above is one dedicated machine). Run `faa7098`:

| Benchmark | net8.0 | net9.0 | net10.0 |
| --- | ---: | ---: | ---: |
| `TypedResults.Ok` *(floor)* | 7.9 ns / 48 B | 7.9 ns / 24 B | 8.4 ns / 24 B |
| `TypedResults.Problem` *(floor)* | 30.8 ns | 49.3 ns | 25.7 ns |
| `FindError` / type switch / route switch | 1.8–4.7 ns / 0 B | 1.6–4.1 ns / 0 B | 2.6–4.4 ns / 0 B |
| ErrorApi `Result<T>` success / failure | 11.4 / 65.4 ns | 10.9 / 91.8 ns | 5.9 / 60.8 ns |
| ErrorOr success / failure | 9.5 / 87.1 ns | 9.6 / 114.0 ns | 5.5 / 63.7 ns |
| OneOf success / failure | 12.9 / 202.1 ns | 11.3 / 121.5 ns | 6.4 / 67.9 ns |
| language-ext success / failure | 10.2 / 85.5 ns | 8.8 / 130.4 ns | 6.4 / 71.4 ns |
| FluentResults success / failure | 10.0 / 89.2 ns | 10.3 / 123.3 ns | 7.3 / 76.0 ns |
| Ardalis success / failure | 9.1 / 99.3 ns | 8.6 / 126.4 ns | 6.1 / 65.1 ns |
| CSharpFunctionalExtensions success / failure | 10.1 / 78.9 ns | 9.8 / 104.3 ns | 5.9 / 63.5 ns |

Success allocations equal the floor on every framework (net8''s 48 B is the framework''s own
`Ok<int>` box); failures allocate 304–336 B. The net8 OneOf failure (202 ns) is the one real
outlier — the union''s `Match` costs more on the older JIT.

To run one framework locally: `dotnet run -c Release -f net8.0` (from `benchmarks/ErrorApi.Benchmarks`).
