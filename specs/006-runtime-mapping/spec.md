# 006 — Runtime mapping

**Status:** implemented · **Packages:** `ErrorApi.Abstractions`, `ErrorApi.AspNetCore`, adapters

## Why

Whatever the document promises, the wire must deliver — the same codes, the same statuses, the same
problem shape — regardless of which result library (or none) the handler was written in.

## Functional requirements

- **FR-001** Every failure MUST answer `application/problem+json` carrying the stable `code`
  extension; the mapping families (`ToHttpResult`, `ToTypedResult`, `ToCreated*`,
  `ToActionResult*`) MUST cover both arms, with `Task`/`ValueTask` twins.
- **FR-002** Each adapter MUST resolve back to the same catalog entry the document was built from —
  by instance type, by known code, or honestly as a 500 when the catalog never saw the value.
- **FR-003** `AddErrorApiResults()` MUST let a handler return `Result`/`Result<T>` directly: the
  endpoint filter maps it exactly as `ToHttpResult()` would, and the endpoint''s 200 metadata is
  rewritten to describe `T`, never the wrapper.
- **FR-004** Annotated exceptions MUST answer through the same `Error.ToProblem()` path via the
  exception handler, indistinguishable on the wire from the result styles.
- **FR-005** Flow helpers MUST exist: `Switch` runs exactly one branch; `OnSuccess`/`OnFailure` run a
  side effect and hand the same result back, awaitable end to end.
- **FR-006** Registration MUST be one call with one lambda: `AddErrorApi()` alone is the minimal
  setup; every knob — composition, exception handler, document shaping, problem-type URI, adapter
  options — extends `AddErrorApi(x => ...)`. The first registration MUST win in DI and on the
  ambient static alike; `ErrorApiRuntime.Use` MUST scope the static restorably.

## Acceptance evidence

`ResultMappingSurfaceTests`, `TypedResultSurfaceTests`, `ActionResultSurfaceTests`,
`ResultFilterTests` + the Mediator sample''s live direct-return assertions, `ResultFlowTests`,
adapter suites × the CI version matrix, `ExceptionHandlerOptionTests`, `OptionsLambdaTests`,
`CompositionTests`, `RuntimeScopeTests`.

## Out of scope

Mapping foreign result types without their adapter package; per-request model swapping.
