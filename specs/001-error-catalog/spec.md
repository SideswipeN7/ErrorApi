# 001 — The error catalog

**Status:** implemented · **Packages:** `ErrorApi.Abstractions` (attributes), `ErrorApi.Generator` (parsing)

## Why

An API''s failures are a contract, and contracts drift when the same fact is written twice. The catalog
exists so each error is declared **once** — its code, status, title and prose — and everything else
(the wire response, the OpenAPI document, the TypeScript union) is derived from that single
declaration at compile time.

## User scenarios

- A developer declares a family of validation failures as one line per entry and gets stable dotted
  codes, statuses and titles without repeating any of them.
- A team already using language-ext annotates its existing `Expected` subclasses with a bare `[Error]`
  and the catalog reads the status and title the types already carry.
- A reviewer sees at build time when two declarations collide or drift, instead of at runtime.

## Functional requirements

- **FR-001** `[Error]` on a `static partial` member returning `Error` MUST cause the generator to
  implement the member; on a type, field or bodied member the entry is recorded, never implemented.
- **FR-002** The wire code MUST resolve as: explicit argument → `code:` literal in the member''s own
  body → declaration name prefixed by its `[ErrorCatalog]`. Writing the code twice and letting the
  copies disagree MUST report `EAPI008`.
- **FR-003** The status MUST resolve as: `[ErrorStatusCode]` → `[Error(status)]` argument →
  `[ErrorCatalog(prefix, defaultStatus)]` → (types only) an int argument of the base constructor
  call whose **parameter name** is status-like (`code`, `status`, `statusCode`, …). No resolvable
  status MUST fail the declaration (`EAPI003`); one outside 100–599 MUST report `EAPI004`.
- **FR-004** Inside an `[ErrorCatalog]` type, an unannotated `static partial Error` member MUST be an
  entry by membership alone. A member with its own implementation part, or a non-partial helper,
  MUST NOT be claimed implicitly.
- **FR-005** `[ErrorDescription]` MUST override `Description =`; `[ErrorStatusCode]` MUST override
  every less specific status, and a disagreement with an explicit `[Error(status)]` MUST report
  `EAPI013`.
- **FR-006** Duplicate codes MUST report `EAPI001`; a declared entry no endpoint can return MUST
  report `EAPI010`; `[SuppressErrorApi("id")]` MUST silence exactly that rule on that declaration.
- **FR-007** Codes, statuses and titles resolved from source (bodies, base constructors) MUST be
  baked into the assembly (`CatalogExport`) so consumers re-derive the identical resolution.

## Acceptance evidence

`CatalogDefaultsTests` (defaults, implicit membership, base-ctor inference incl. the
mis-inference guard, cross-assembly export round-trip, EAPI003/EAPI013), `ContractAndMappingTests`,
snapshot suites over generated catalogs, `EAPI*` assertions across `ErrorApi.Generator.Tests`.

## Out of scope

Entries resolved from runtime state; per-request catalogs; localization of titles.
