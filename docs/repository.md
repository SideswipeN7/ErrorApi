# Repository guide

The project layout, how to build and test, and how ErrorApi relates to the alternatives.

## Repository layout

```
src/ErrorApi.Abstractions   Error, Result<T>, [Error], [ProducesError], the metadata contracts
src/ErrorApi.Generator      the incremental generator (netstandard2.0, Roslyn 4.14)
src/ErrorApi.AspNetCore     Result→IResult mapping, the OpenAPI transformer, the TypeScript writer
src/ErrorApi.ErrorOr        adapter, pinned to ErrorOr 2.1.x
src/ErrorApi.OneOf          adapter, pinned to OneOf 3.0.x — and the entry point for hand-rolled unions
src/ErrorApi.LanguageExt    adapter, pinned to LanguageExt.Core 4.4.x
src/ErrorApi.FluentResults  adapter, pinned to FluentResults 3.16.x
tests/ErrorApi.TestKit      the generator harness, the snapshot assertion, a hand-built model
tests/…Generator.Tests      core tests — no result library referenced, so they pass on the core alone
tests/ErrorApi.*.Tests      one suite per adapter, each pinning its own library version
tests/…Integration.Tests    three samples running under WebApplicationFactory: live documents, live problems
benchmarks/ErrorApi.Benchmarks  request-path cost, base library and every adapter, vs raw TypedResults
samples/Sample.Api          the reference API: route groups, interface dispatch, [ProducesError], AOT
samples/Sample.ErrorOr.Api  the same API in ErrorOr, with codes read out of the factory calls
samples/Sample.OneOf.Api    the same API as a union, with the failure cases carrying [Error]
samples/Sample.LanguageExt.Api  the same API in Fin<T>, with annotated Expected subclasses
samples/Sample.Exceptions.Api   the same API with no result type at all, only annotated exceptions
samples/Sample.Mediator.Api     the same API with every endpoint behind MediatR
samples/Sample.FluentResults.Api  the same API in FluentResults, with annotated Error subclasses
samples/Sample.Wolverine.Api    the same API behind Wolverine, handlers matched by convention
samples/Sample.Controllers.Api  the same API on attribute-routed controllers
samples/Sample.Shared.Errors    a class library: shared catalog + services, exports baked in at build
samples/Sample.Toolbox.Api      consumes the library across the assembly boundary; the toolbox features
samples/Sample.Ardalis.Api      the same API in Ardalis.Result, with a factory catalog
samples/Sample.Cfe.Api          the same API in CSharpFunctionalExtensions Result<T, E>
samples/Sample.Mediator.Validation.Api  MediatR + FluentValidation: the behaviour's 400 discovered on every endpoint
samples/client              how the generated union is consumed
```

Each adapter is its own package so you take only the dependency you already have. They share one generator: the compile-time half needs no per-library knowledge.

### Build and test

```bash
dotnet test ErrorApi.slnx
```

The generator tests compile real snippets against the live ASP.NET Core assemblies and compare the emitted files to approved snapshots under `tests/ErrorApi.Generator.Tests/Snapshots/`. To re-approve after an intentional change:

```bash
ERRORAPI_ACCEPT_SNAPSHOTS=1 dotnet test ErrorApi.slnx
```

Then read the diff. That diff is the review.

The integration suite boots `Sample.Api`, `Sample.Controllers.Api` and `Sample.Toolbox.Api` in-process
and asserts against the live `/openapi/v1.json`, a live `application/problem+json` response carrying
the catalog code, and the served TypeScript contract — the end-to-end claims are CI gates, not manual
checks.

Each adapter has its own suite, referencing only its own library, so a version bump cannot leak into
anything else. The version under test is a build property, which is how one suite covers a range:

```bash
dotnet test tests/ErrorApi.ErrorOr.Tests -p:ErrorOrTestVersion=2.0.1
```

```bash
dotnet test tests/ErrorApi.OneOf.Tests -p:OneOfTestVersion=3.0.263
```

```bash
dotnet test tests/ErrorApi.LanguageExt.Tests -p:LanguageExtTestVersion=4.4.0
```

CI runs that as a matrix. The adapters are verified against ErrorOr 1.10.0 / 2.0.1 / 2.1.1,
OneOf 3.0.263 / 3.0.271, LanguageExt.Core 4.4.0 / 4.4.9, FluentResults 3.15.0 / 3.16.0,
Ardalis.Result 9.1.0 / 10.1.0, CSharpFunctionalExtensions 3.4.3 / 3.7.0 and LanguageExt.Core
5.0.0-beta-77. language-ext **v5** has no stable release yet, so `ErrorApi.LanguageExt.V5` ships as a
**prerelease** tracking the beta — it goes stable the moment 5.0.0 does, and the v4 package stays as it is.

Working on this repository with a coding agent? [`AGENTS.md`](../AGENTS.md) is the map: invariants, where each concern lives, and the checks that have to pass.

---

---

## Where this sits next to the alternatives

| | Failure typing | Maps to `ProblemDetails` | Documents errors in OpenAPI | Typed client errors |
| --- | --- | --- | --- | --- |
| **ErrorOr / FluentResults / OneOf** | yes | yes, at runtime | no | no |
| **Exceptions and a handler** | no | yes, at runtime | no | no |
| **NSwag / Kiota** | — | — | only what you hand-wrote with `.Produces(...)` | success shapes only |
| **`.Produces<ProblemDetails>(404)` by hand** | — | — | yes, until someone forgets | no |
| **ErrorApi** | yes, or bring your own | yes, generated | yes, derived from the code | yes, generated union |

The honest framing: ErrorOr solves the return type, NSwag solves the client shape, and neither answers *"which errors does this endpoint return?"* ErrorApi answers exactly that question and leans on the other two for everything else.


---

[← back to the README](../README.md)
