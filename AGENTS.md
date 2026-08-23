# AGENTS.md

Orientation for a coding agent (or a new contributor) working in this repository. `README.md` explains
what ErrorApi does for its users; this file explains how the code is arranged and what must stay true.

Claude Code, Cursor and Copilot all read this file. Keep it accurate — a stale map is worse than none.

## What this repository is

A Roslyn incremental source generator plus its runtime. It reads an `[Error]`-annotated catalog and the
Minimal API `Map*` calls of a compilation, works out **which errors each endpoint can return**, and emits
a reflection-free model that fills in the OpenAPI document and a TypeScript error contract.

The whole point is that work happens at **compile time**. Any change that moves a decision to runtime, or
that introduces reflection on the request path, is going the wrong way.

## Commands

```bash
dotnet build ErrorApi.slnx                       # must be warning-free
dotnet test ErrorApi.slnx                        # 79 tests across four suites
ERRORAPI_ACCEPT_SNAPSHOTS=1 dotnet test ErrorApi.slnx   # re-approve snapshots, then read the diff
dotnet run --project samples/Sample.Api          # /swagger, /scalar, /openapi/v1.json, /openapi/errors.ts
dotnet run --project samples/Sample.Api -- --emit-error-contract out.ts
```

CI builds `-c Release -warnaserror`. The sample has `PublishAot=true`, so the trim and AOT analyzers run
over it during a normal build; an IL2026/IL3050 warning there is a real regression, not noise.

## Project map

| Project | TFM | Holds |
| --- | --- | --- |
| `src/ErrorApi.Abstractions` | `netstandard2.0;net10.0` | `Error`, `Result`/`Result<T>`, `[Error]`, `[ProducesError]`, `ErrorDescriptor`, `IErrorApiMetadata`, `RoutePattern`, `ErrorApiRuntime` |
| `src/ErrorApi.Generator` | `netstandard2.0` | the generator: parsing, the call-graph walk, the emitters |
| `src/ErrorApi.AspNetCore` | `net10.0` | `ToHttpResult()`, the OpenAPI operation transformer, the TypeScript writer, `AddErrorApi()`'s target |
| `src/ErrorApi.{ErrorOr,OneOf,LanguageExt}` | `net10.0` | one adapter each, pinning that library's version |
| `tests/ErrorApi.TestKit` | `net10.0` | the generator harness, the snapshot assertion, `FakeMetadata` |
| `tests/ErrorApi.Generator.Tests` | `net10.0` | core snapshot and behaviour tests; references no result library |
| `tests/ErrorApi.{ErrorOr,OneOf,LanguageExt}.Tests` | `net10.0` | one suite per adapter, version overridable |
| `samples/Sample.Api` | `net10.0` | the end-to-end proof |

The generator does **not** reference `ErrorApi.Abstractions`. It matches attributes by metadata name
(`CatalogParser.ErrorAttributeName`), which is also why it can read a catalog out of a referenced assembly.

## The pipeline, file by file

1. **`CatalogParser`** turns each `[Error]` declaration into a `CatalogEntry`.
   Two kinds, and the distinction matters everywhere downstream:
   - `Generated` — a `static partial` member returning `ErrorApi.Error`. The generator writes its body.
     Held to strict rules; violations are `EAPI003`.
   - `Declared` — a type, a field, or a member with its own body. Nothing is emitted; the entry is only
     recorded. This is what lets a catalog live in ErrorOr's, OneOf's or language-ext's error types.
     **A member is only held to the generated rules if it is marked `partial`.**
   The wire code and the title may be inferred — see `Helpers/NameInference`. The priority is
   explicit argument, then a `code:` literal in the member's body, then the name plus the
   `[ErrorCatalog]` prefix. `ErrorReachabilityWalker` resolves codes through the same helper, because
   the walk has to agree with the catalog it is walking towards; a change to one order without the
   other silently empties endpoint contracts.
2. **`EndpointScanner`** resolves each `Map*` call site: route template (including `MapGroup` prefixes
   followed back through locals), HTTP method, and handler expression.
3. **`ErrorReachabilityWalker`** walks from the handler through the call graph — into source bodies, local
   functions, and **through interface and virtual dispatch** to implementations in the compilation —
   collecting `[Error]` member reads, `[Error]` type constructions, and `[ProducesError]` declarations.
   Bounded at depth 12, cycle-safe, semantic models cached per tree.
4. **`Emit/CatalogEmitter`** writes the implementing partials (generated entries only).
   **`Emit/MetadataEmitter`** writes the descriptor table, the endpoint map, the code switch, the
   instance-type switch, and the zero-argument `AddErrorApi()` overload.
5. At runtime `ErrorApiOperationTransformer` looks the endpoint up by normalized route + method and fills
   in the responses; `TypeScriptContractWriter` renders the same model as a TS module.

## Invariants

- **No reflection on the request path.** Lookups are `switch` statements over string literals or type
  patterns. If you find yourself reaching for `Type.GetType`, `Activator`, or an attribute read at
  runtime, the answer belongs in the generator instead.
- **`RoutePattern.Normalize` exists twice** — once in `Abstractions` for runtime, once as
  `Helpers/RouteNormalizer` for the generator, because the generator cannot reference the runtime
  assembly. `RouteNormalizationTests` pins the copies together. Change one, change both.
- **Error codes are unique; statuses are not.** Two entries may share `422`; `EAPI001` fires only on a
  duplicated code. The OpenAPI transformer groups by status and lists every code in `code.enum`.
- **Generated code is emitted for the user's assembly**, so it may reference `ErrorApi.AspNetCore` types
  only when the compilation actually references them — see the `RegistrationTypeName` guard in
  `ErrorApiGenerator.Execute`.
- **Incremental-pipeline models must have value equality.** Use `EquatableArray<T>`, `LocationInfo` and
  `DiagnosticInfo` rather than holding `ISymbol`, `SyntaxNode`, `Location` or `Diagnostic`.
- **Generator diagnostics ignore `#pragma warning disable`.** Suppress them with `<NoWarn>` or
  `.editorconfig`, and say so when you document a rule.
- **Code inference has exactly one implementation.** `NameInference.CodeFromBody` and
  `CodeFromName` are shared by `CatalogParser` and `ErrorReachabilityWalker`. A symbol from a
  referenced assembly has no body to read, so cross-assembly catalogs resolve by name only.
- **Adapters do not depend on each other.** Each pins exactly one result library. Shared behaviour goes in
  `ErrorApi.AspNetCore` or in the generated model, never in a second adapter.

## Testing

Four suites, and the split is deliberate. `ErrorApi.Generator.Tests` references **no** result library, so
it proves the core stands on its own. Each adapter gets its own project referencing exactly one library,
which keeps a version bump from leaking anywhere else.

`GeneratorHarness` (in `ErrorApi.TestKit`) compiles source snippets against the assemblies that test
process already runs on, so each suite exercises the real surface of its own library rather than stubs.
Prefer `RunAndCompile` over `Run`: it fails when the generated code does not compile, which catches most
emitter mistakes on its own.

The library version is a build property, so one suite covers a range:

```bash
dotnet test tests/ErrorApi.ErrorOr.Tests -p:ErrorOrTestVersion=2.0.1
```

The same knob exists as `OneOfTestVersion` and `LanguageExtTestVersion`. CI runs them as a matrix — add
a row there whenever you widen the supported range, and only claim a version the matrix actually runs.

Snapshots live in `tests/ErrorApi.Generator.Tests/Snapshots/*.verified.txt` and are compared by a
dependency-free helper in `Snapshot.cs`. A mismatch writes `.received.txt` beside the approved file. When
a change to the emitters is intentional, re-approve and **read the diff** — that diff is the review.

When you add a feature, add both halves: a generator test that asserts on the emitted text, and — where
there is runtime behaviour — a behaviour test. Adapter tests do exactly this, one class per library.

## Adding a new adapter

1. `src/ErrorApi.<Library>/` targeting `net10.0`, `IsAotCompatible`, one `PackageReference` for the
   library plus a `ProjectReference` to `ErrorApi.AspNetCore`. Pin the version in
   `Directory.Packages.props` under the adapter group.
2. Namespace `ErrorApi.Interop` for every adapter, so one `using` covers them all.
3. Resolve errors through `IErrorApiMetadata`: `FindError(code)` when the library carries a string code,
   `FindErrorForInstance(instance)` when the failure is a type. Fall back to something defensible and
   document the fallback. Take an optional `IErrorApiMetadata? metadata = null` parameter that defaults to
   `ErrorApiRuntime.Metadata`, so the behaviour is testable without a host.
4. Add `tests/ErrorApi.<Library>.Tests/` alongside the others: a `<Library>TestVersion` property with the
   pinned default, `VersionOverride="$(<Library>TestVersion)"` on the package reference, a reference to
   `ErrorApi.TestKit`, and tests proving both halves — discovery through the generator, and mapping at
   runtime. Register the project in `ErrorApi.slnx` and add matrix rows to `.github/workflows/ci.yml`.
5. Add a section to the README's *Bring your own Result type*.

If the library carries neither a code nor a status — FluentResults' `Result.Fail("message")` is the
example — say so plainly instead of inventing a convention.

## Style

- File-scoped namespaces, `var` where the type is apparent, 4-space indent, `.editorconfig` is authoritative.
- Public API carries XML docs. Comments explain *why*, never restate the code; the existing files are the
  reference for density and tone.
- Generated code is written through `SourceWriter`. Keep the output readable — it is snapshot-reviewed by
  humans, and `global::`-qualified so it cannot be captured by a user namespace.
- Prose in docs and comments is English; conversation with the maintainer may be Polish.

## Before publishing to NuGet

README images use **repository-relative** paths, because that is what renders on GitHub while the
repository is private. nuget.org cannot resolve a relative path, so a package page would show broken
images. As part of a release — on the release branch, not on main — rewrite them to absolute raw URLs,
which requires the repository to be public:

```bash
sed -i 's|docs/images/|https://raw.githubusercontent.com/SideswipeN7/EApi/main/docs/images/|g' README.md
```

The adapter READMEs use `../../docs/images/` and need the same treatment. Doing it on main instead
breaks the images for whoever is reading the repository.

## Things that will bite you

- Adding a member to `IErrorApiMetadata` breaks every hand-written implementation, including
  `FakeMetadata` in the tests. There are no default interface members because `Abstractions` targets
  `netstandard2.0`.
- `ErrorOr` and `LanguageExt` both export a type named `Error`. Inside `namespace ErrorApi.Interop` the
  bare name binds to *ours*; alias theirs (`using LangError = LanguageExt.Common.Error;`).
- The generator runs on this repository's own projects. A change that emits invalid code shows up as a
  build failure in `ErrorApi.AspNetCore` before any test runs.
- `EAPI002` fires on `MapErrorContract`'s parameterised route by design; it is suppressed via `<NoWarn>`
  in that project only.
