# 003 — Cross-assembly discovery

**Status:** implemented · **Packages:** `ErrorApi.Generator`, `ErrorApi.Abstractions`, `ErrorApi.AspNetCore`

## Why

Layered solutions put the failures two projects away from the endpoints. The dependency direction is
sacred — Domain must know nothing about the API — so the knowledge travels *along* the references:
each library bakes what its surface can reach, and the consumer reads it back.

## Functional requirements

- **FR-001** A compilation that runs the generator and maps no endpoints MUST export reachability for
  every public method, public property getter, and handled message type
  (`[assembly: ReachabilityExport]`), and its source-resolved catalog facts (`CatalogExport`).
- **FR-002** A consuming compilation MUST continue the walk through those exports — direct calls by
  member id, dispatches by message type — transitively along the reference direction.
- **FR-003** An export left incomplete by an unseen dispatch MUST report `EAPI012` in the library''s
  own build.
- **FR-004** Producer and consumer MUST each have an explicit project-file knob:
  `<ErrorApiExportReachability>` and `<ErrorApiIncludeAssemblies>` (exact names or trailing-star
  prefixes).
- **FR-005** Every generated assembly MUST expose its model as `<Namespace>.ErrorApiModel.Metadata`,
  and `AddErrorApi(x => x.Include(...))` MUST compose models first-answer-wins, so instance-type
  switches declared in referenced assemblies resolve in the host process.

## Acceptance evidence

`CrossAssemblyTests` (exports, includes, property getters, EAPI012, status export round-trip),
`CompositionTests`, and the Toolbox sample booted in CI: the body-inferred `Very.Old.Retired`
documented and answered across the boundary.

## Out of scope

Assemblies that never ran the generator (they stay opaque; `[ProducesError]` covers them).
