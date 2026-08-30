# 002 — Endpoint discovery

**Status:** implemented · **Package:** `ErrorApi.Generator`

## Why

The contract question is *per endpoint*: which failures can this route answer? Nobody should have to
say so by hand — the handler''s code already knows. The generator walks it.

## User scenarios

- Minimal API endpoints and attribute-routed controllers in one app produce one consistent document.
- An endpoint behind MediatR/Wolverine documents what its handler raises, with no attribute anywhere.
- Two versions of one route keep separate contracts, split by API description group.
- When the walk cannot see past something, the build says so instead of documenting a partial
  contract as complete.

## Functional requirements

- **FR-001** Every `Map*` call site with a compile-time route template MUST become an endpoint entry
  (method, normalized route, group); a non-literal template MUST report `EAPI002`.
- **FR-002** Attribute-routed `ControllerBase`/`[ApiController]` actions MUST be a second endpoint
  surface, honouring MVC''s token, rooted-template and inheritance rules (base actions, base
  `[Route]`, attributes through overrides).
- **FR-003** The walk MUST follow calls into source bodies, local functions, property getters, and
  through interface/virtual dispatch to implementations in the compilation, bounded (default 12,
  `errorapi_walk_depth`) and cycle-safe.
- **FR-004** A dispatch-shaped call (interface/abstract, no implementation present) MUST be bridged
  through its message type: handlers by generic interface or `*Handler`/`*Consumer` convention, plus
  pipeline behaviours generic over the request. An unbridgeable dispatch MUST report `EAPI009` —
  also on partial contracts.
- **FR-005** Endpoint identity MUST be route + method + group. Groups come from `WithGroupName`
  literals, `[ApiExplorerSettings]`, or Asp.Versioning literals synthesized to the `''v''VVV` shape;
  matching MUST run through `EndpointGroup.Normalize` so `"v1"`, `"V1"` and `"1.0"` are one group.
  The same route mapped twice with nothing telling the mappings apart MUST report `EAPI011`.
- **FR-006** A `Result`-returning handler that reaches no entry MUST report `EAPI006`; an unresolvable
  handler MUST report `EAPI007`.
- **FR-007** Discovery MUST be an incremental pipeline: a no-op edit leaves the emit step cached.

## Acceptance evidence

`ControllerDiscoveryTests`, `EndpointGroupTests` (incl. Asp.Versioning + normalization twins),
`TypedResultDiscoveryTests`, dispatch/pipeline suites, `IncrementalityTests`, and the integration
suite booting twelve samples against live documents.

## Out of scope

Conventional (non-attribute) MVC routing; handlers resolved from runtime registries; API versions
computed at runtime (the ambiguous merge warns instead).
