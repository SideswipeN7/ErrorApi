# 007 — Performance and native AOT

**Status:** implemented · **Project:** `benchmarks/ErrorApi.Benchmarks`

## Why

A library that sits on every request path must be free where it can be and honest where it cannot.
"No reflection on the request path" is an architectural invariant; the benchmarks are what keep the
numbers from becoming folklore.

## Functional requirements

- **FR-001** No reflection on the request path: every lookup is a generated `switch`; packages are
  `IsAotCompatible`, and one sample builds `PublishAot` under `-warnaserror` in CI.
- **FR-002** Budgets, measured against raw `TypedResults` as the floor: success paths within ~1 ns of
  hand-written mapping at equal allocation; generated lookups single-digit ns, zero-alloc; failures
  tens of ns end to end.
- **FR-003** The benchmark project MUST exercise the real generated switches (the generator runs over
  the benchmark assembly itself) and MUST run in-process (per-hash app-control policies block
  spawned benchmark executables).
- **FR-004** Every main push MUST run the benchmarks in CI and keep the full results as a per-commit
  artifact — a paper trail, deliberately not a flaky threshold gate.
- **FR-005** Known AOT boundary: `AddErrorApiResults()` serializes the success value from its runtime
  type and `IncludeFromAssemblies` reflects at startup — both documented, with static alternatives.

## Acceptance evidence

The `benchmarks` CI job + artifacts; README/docs performance tables; the AOT sample building in CI;
the two measured optimizations (FluentResults success 52.5→5.5 ns; `ToProblem` 130→60 ns) recorded in
`benchmarks/ErrorApi.Benchmarks/README.md`.

## Out of scope

Latency SLOs for user handlers; benchmarking the generator''s own build-time cost per commit
(measured once, within build noise; see AGENTS).
