# Feature specifications

Retroactive specs in the [spec-kit](https://github.com/github/spec-kit) shape: one folder per
capability, each `spec.md` stating the requirements the code must satisfy and the acceptance evidence
that proves it does. Requirements are numbered (`FR-xxx`) and every one maps to an executable gate —
a test, a CI job, or a benchmark — because a requirement nothing enforces is a wish.

| Spec | Capability |
| --- | --- |
| [001-error-catalog](001-error-catalog/spec.md) | Declaring errors once — inference, implicit membership, overrides |
| [002-endpoint-discovery](002-endpoint-discovery/spec.md) | Finding every endpoint and every reachable failure at compile time |
| [003-cross-assembly](003-cross-assembly/spec.md) | Layered solutions — exports, boundary knobs, model composition |
| [004-openapi-documents](004-openapi-documents/spec.md) | The document half — transformer, Swashbuckle filter, shaping options |
| [005-typescript-contract](005-typescript-contract/spec.md) | The client half — one exhaustive union per endpoint |
| [006-runtime-mapping](006-runtime-mapping/spec.md) | Result → HTTP at runtime — mappings, flow, filters, exceptions |
| [007-performance-and-aot](007-performance-and-aot/spec.md) | The request-path budget and the no-reflection guarantee |
| [008-quality-gates](008-quality-gates/spec.md) | How every other spec stays true — suites, CI, coverage, release |
