# Implementation Plan: [FEATURE]

**Branch**: `[###-feature-name]` | **Date**: [DATE] | **Spec**: [link]
**Input**: Feature specification from `/specs/[###-feature-name]/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

[Extract from feature spec: primary requirement + technical approach from research]

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: C# 13 / ASP.NET Core (.NET 10)
**Primary Dependencies**: EF Core, Dapper, Serilog, Microsoft.AspNetCore.OpenApi, Scalar.AspNetCore, xUnit, Moq
**Storage**: PostgreSQL (via EF Core; Dapper for performance-critical queries)
**Testing**: xUnit + Moq + WebApplicationFactory (integration)
**Target Platform**: Linux container (Docker / docker-compose for local dev)
**Project Type**: Web API service
**Performance Goals**: [NEEDS CLARIFICATION — specify per feature if relevant]
**Constraints**: <200ms p95 target; all secrets via env vars; zero build warnings
**Scale/Scope**: [NEEDS CLARIFICATION — specify per feature if relevant]

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Verify this plan complies with the EdiTrack API Constitution (`.specify/memory/constitution.md`):

- [ ] **Clean Code (I)**: All new methods ≤30 lines; names reveal intent; no `.Result`/`.Wait()`
- [ ] **API Contract (II)**: Request/response shapes use DTO classes in `Contracts/` or `Dtos/`;
      controllers use `[ProducesResponseType]`; error envelope is consistent
- [ ] **Testing (III — NON-NEGOTIABLE)**: Unit tests for all non-trivial services/utilities;
      integration tests via `WebApplicationFactory`; tests in `/tests/` mirroring `/src/`
- [ ] **Observability (IV)**: Serilog used via `ILogger`; request logging included;
      no PII in logs; `/health` endpoint present if new project
- [ ] **Security (V)**: No hardcoded secrets; input validation at controller boundary;
      HTTPS enforced in non-dev environments
- [ ] **Dependencies (VI)**: No new NuGet packages added without spec documentation and
      BCL/ASP.NET Core alternatives considered
- [ ] **CI (VII)**: Feature does not break `ci.yml` pipeline steps; no workflow files
      outside `.github/workflows/`
- [ ] **Solution Structure**: All production code under `/src/EdiTrack.*`; all test code
      under `/tests/EdiTrack.*.Tests`; namespaces follow `EdiTrack.` PascalCase convention

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)
<!--
  ACTION REQUIRED: Replace the placeholder tree below with the concrete layout
  for this feature. Delete unused options and expand the chosen structure with
  real paths (e.g., apps/admin, packages/something). The delivered plan must
  not include Option labels.
-->

```text
# EdiTrack API — Standard Layout
# Production code under /src, tests under /tests (mirrors /src with .Tests suffix)

src/
  EdiTrack.Api/
    Controllers/
    Contracts/        ← DTOs (request/response shapes)
    Services/
    Infrastructure/   ← EF Core DbContext, repositories
  EdiTrack.Core/      ← domain logic (future project)
  EdiTrack.Infrastructure/  ← data access layer (future project)

tests/
  EdiTrack.Api.Tests/
    Unit/
    Integration/

# [REMOVE IF UNUSED] Option 2: Feature adds a new top-level project under /src
src/
  EdiTrack.[NewModule]/
tests/
  EdiTrack.[NewModule].Tests/
```

**Structure Decision**: [Document the selected structure and reference the real
directories captured above]

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
