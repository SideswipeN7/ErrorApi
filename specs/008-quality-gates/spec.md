# 008 — Quality gates

**Status:** implemented · **Where:** `tests/`, `.github/workflows/`

## Why

Every other spec is only as true as whatever enforces it. This spec pins the enforcement itself.

## Functional requirements

- **FR-001** Warning-free build: `-warnaserror` everywhere in CI, trim/AOT analyzers included.
- **FR-002** Ten suites: generator behaviour + snapshots (no result library referenced), one suite
  per adapter with the library version overridable (CI runs the version matrix), the Swashbuckle
  suite, and an integration suite booting twelve samples against live documents, live problem
  responses and the served TS contract.
- **FR-003** Line coverage of hand-written source MUST stay measurable
  (`--collect:"XPlat Code Coverage"`, merge method in AGENTS) and was last measured at 89%
  (Generator 94%); attribute classes sit at 0% by nature.
- **FR-004** Releases go through one maintainer-only `workflow_dispatch`: full build + all suites as
  the gate, pack with the input `VersionPrefix`, then — unless `dry_run` — nuget.org push, git tag
  and a GitHub release. The dry run MUST rehearse everything and publish nothing.
- **FR-005** Once the repository is public, `main` MUST require a PR with the `build` check passing
  (`.github/enable-branch-protection.ps1` — GitHub gates branch protection on visibility).
- **FR-006** Generator diagnostics ignore `#pragma`; suppression is per-declaration
  (`[SuppressErrorApi]`) or project-wide (`NoWarn`), and every diagnostic is release-tracked.
- **FR-007** `dotnet add package ErrorApi` MUST be the whole install on every supported TFM. The
  package is a dependencies-only meta-package (no assembly): `ErrorApi.AspNetCore` everywhere, plus
  `ErrorApi.Swashbuckle` on net8/net9 where Swagger is the only document road; its README carries
  the quickstart.

## Acceptance evidence

`ci` workflow (build + adapters matrix + benchmarks), `release` workflow (dry run validated
end to end), the coverage numbers and merge method in AGENTS, `AnalyzerReleases.*` shipping the
EAPI catalog. For FR-007: the ci Pack job packs the whole solution, and the `ErrorApi.nuspec`
inside the produced package shows the per-TFM dependency groups and no `lib/` folder.
