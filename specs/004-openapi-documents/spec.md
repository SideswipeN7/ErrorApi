# 004 — OpenAPI documents

**Status:** implemented · **Packages:** `ErrorApi.AspNetCore` (net10 transformer), `ErrorApi.Swashbuckle`

## Why

The document is the contract clients read. It must list, per operation, exactly the failures the walk
found — statuses, codes, examples — and it must say the same thing no matter which document pipeline
renders it.

## Functional requirements

- **FR-001** Every matched operation MUST gain one response per distinct status, content-typed
  `application/problem+json`, whose schema requires `status` and `code`, enumerates exactly the
  reachable codes, documents the optional `errors` array, and carries one example per code.
- **FR-002** The built-in pipeline (`.NET 10`, `IOpenApiOperationTransformer`) and the Swashbuckle
  filter (`IOperationFilter`, all TFMs) MUST produce identical responses; both compile the single
  shared `ErrorResponseBuilder`.
- **FR-003** Lookup MUST be by normalized route + method + group, resolved exact-group first, then
  the ungrouped entry, with a null group matching a single-group route.
- **FR-004** `AddErrorApi()` MUST hook the transformer into every document on net10;
  `AddSwaggerGen(c => c.AddErrorApiResponses())` MUST be the whole Swashbuckle hookup.
- **FR-005** Documentation MUST be shapeable without touching behaviour:
  `ErrorCodeDescriptionEnabled(false)` strips prose, `FilterErrorCodes`/`HideErrorCodes` hide
  entries — runtime lookups (`FindError`, `FindErrorForInstance`) pass through untouched.

## Acceptance evidence

`SwashbuckleFilterTests`, `DocumentShapingTests`, transformer coverage via twelve samples booted in
CI asserting live `/openapi/v1.json` content, group resolution in `EndpointGroupTests`.

## Out of scope

Success-response schemas (ASP.NET''s own job); documents for endpoints the generator could not match.
