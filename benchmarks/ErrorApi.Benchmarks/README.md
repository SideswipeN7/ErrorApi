# ErrorApi.Benchmarks

The request-path cost of ErrorApi: the generated lookups and the `→ IResult` mapping, in the base
library and through every adapter, with raw `TypedResults` as the floor. The catalog and endpoint
table are generated for this assembly itself, so the switches under test are the real emitted ones.

```bash
dotnet run -c Release --project benchmarks/ErrorApi.Benchmarks
```

The in-process toolchain is deliberate: BenchmarkDotNet''s default compiles and launches a fresh
executable per benchmark, which per-hash application control policies (Smart App Control) block.

## Results

.NET 10.0.9, x64 (i9-9900K), BenchmarkDotNet 0.15.4, ShortRun in-process. `main @ cd8e3a7`.

| Benchmark | Mean | Allocated |
| --- | ---: | ---: |
| `TypedResults.Ok(7)` *(floor)* | 4.5 ns | 24 B |
| `TypedResults.Problem(404, …)` *(floor)* | 24.3 ns | 168 B |
| `FindError` — hit / miss | 5.2 / 5.8 ns | 0 B |
| `FindErrorForInstance` | 2.5 ns | 0 B |
| `TryGetEndpointErrors` | 3.4 ns | 0 B |
| `Result<T>` success | 4.4 ns | 24 B |
| `Result<T>` failure | 60.1 ns | 304 B |
| ErrorOr success / failure | 4.6 / 70.2 ns | 24 / 304 B |
| OneOf success / failure | 5.0 / 66.6 ns | 24 / 304 B |
| language-ext success / failure | 5.9 / 76.1 ns | 24 / 304 B |
| FluentResults success / failure | 5.5 / 80.5 ns | 24 / 328 B |
| Ardalis success / failure | 4.2 / 73.9 ns | 24 / 304 B |
| CSharpFunctionalExtensions success / failure | 5.0 / 64.2 ns | 24 / 304 B |

**Reading:** success paths cost the same as writing `TypedResults.Ok` by hand; the generated lookups
are single-digit nanoseconds and allocation-free; a failure costs ~60–80 ns end to end, of which
24 ns is ASP.NET''s own `ProblemDetails` machinery.

## What the first run bought

The initial numbers exposed two real inefficiencies, both fixed and re-measured:

1. **FluentResults allocated on success** — its `IsFailed`/`Errors` run a LINQ `OfType` over
   `Reasons` per call. The adapter now scans `Reasons` directly: 52.5 ns / 120 B → **5.5 ns / 24 B**.
2. **`ToProblem` built a dictionary twice** — a temporary extensions dictionary that ASP.NET then
   copied. It now writes into the `ProblemDetails` it constructs, and `ErrorDescriptor.ToError()`
   caches its immutable `Error`: every failure path halved, 130 → **60 ns**, 576 → **304 B**.

Full run history and charts: the benchmark results page linked from the repository status notes.
After touching the mapping path, re-run and refresh this table plus README "Performance".
