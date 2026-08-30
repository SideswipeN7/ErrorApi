# 005 — The TypeScript contract

**Status:** implemented · **Package:** `ErrorApi.AspNetCore` (`TypeScriptContractWriter`)

## Why

The frontend is the other party to the contract. A string-typed `catch` is folklore; a generated
union is a compiler check — add a failure server-side and the client build breaks instead of
production.

## Functional requirements

- **FR-001** The writer MUST render, from the same compile-time model the server runs on: an
  `ApiProblem<TCode>` shape, one union type per endpoint
  (`GetOrdersByIdError = ApiProblem<"Orders.NotFound" | …>`), and a catalog of all codes.
- **FR-002** When two groups share a route, aliases and keys MUST tell them apart
  (`GetOrdersByIdV1Error`, `"GET /orders/{id} @v1"`); a cosmetic group MUST NOT rename anything.
- **FR-003** The contract MUST be reachable both ways: served live (`MapErrorContract()`, default
  `/openapi/errors.ts`) and emitted as a build step
  (`app.TryEmitErrorContract(args)` with `--emit-error-contract <path>`).
- **FR-004** Document shaping (hidden codes, stripped descriptions) MUST apply to the contract
  exactly as it applies to OpenAPI.

## Acceptance evidence

`TypeScriptContractTests`, `EndpointGroupTests` (load-bearing group suffixes),
`DocumentShapingTests`, `ContractEmissionTests` (the build-step emitter writes a real file), and the
integration suite fetching `/openapi/errors.ts` live.

## Out of scope

Generating request/response success types (NSwag et al. do this well); non-TypeScript targets.
