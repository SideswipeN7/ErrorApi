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
