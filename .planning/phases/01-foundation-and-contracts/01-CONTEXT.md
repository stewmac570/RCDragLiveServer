# Phase 1: Foundation and Contracts - Context

**Gathered:** 2026-03-20
**Status:** Ready for planning

<domain>
## Phase Boundary

Establish the ASP.NET Core Web API baseline, Render-friendly hosting setup, and the core live race payload models (`LiveRaceState`, `LiveMatch`) for v1.

</domain>

<decisions>
## Implementation Decisions

### Live Payload Contract Shape
- Keep v1 simple and practical with string properties for public display fields.
- `LiveRaceState` required fields for v1: `EventName`, `EventDate`, `CurrentRound`, `NextUp`, `Matches`.
- `LiveMatch` includes: `Driver1`, `Driver2`.
- Avoid strict canonical over-enforcement in v1.

### Event Date Representation
- `EventDate` is a simple display string in v1.
- Use the easiest sender-provided value from the WinForms publisher.
- No timezone logic in v1.
- No match timing fields in v1.

### Empty/Default State Behavior
- Before any update is posted, `GET /api/live` returns a valid empty/default state (not an error).
- Before any update is posted, `GET /` still loads and shows a clear "No live race data available yet" message.
- `Matches` defaults to an empty list.

### Model Evolution Policy
- Prefer flexible additive changes after v1.
- Keep v1 contract stable for current consumers while allowing additional fields later.
- Do not add explicit versioning or over-engineer extensibility yet.

### Claude's Discretion
- Exact property naming conventions and casing details, as long as they remain consistent and align with ASP.NET Core JSON defaults/project conventions.
- Internal validation boundary details that preserve the required v1 behavior without over-constraining future additive fields.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Planning and Scope Sources
- `.planning/ROADMAP.md` - Phase 1 goal, dependencies, requirements mapping, and success criteria.
- `.planning/REQUIREMENTS.md` - DATA-01, DATA-02, DATA-03, HOST-01, HOST-02, HOST-03 constraints and acceptance expectations.
- `.planning/PROJECT.md` - Product constraints and v1 simplicity priorities.
- `.planning/STATE.md` - Current project progress and active phase context.

No additional external specs or ADRs were referenced during this discussion.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- No application source files exist yet in this repository; there are no current reusable runtime components/services/models.

### Established Patterns
- No established app-level coding patterns yet; Phase 1 will define the baseline patterns.

### Integration Points
- Phase 1 implementation will create the initial ASP.NET Core entrypoint, model contracts, and state abstraction points that later phases will extend (`POST /api/update`, `GET /api/live`, `GET /health`, and homepage wiring in subsequent phases).

</code_context>

<specifics>
## Specific Ideas

- Keep sender integration friction low by accepting simple display-oriented strings in v1.
- Prioritize practical delivery over strict schema enforcement and over-designed versioning in early phases.

</specifics>

<deferred>
## Deferred Ideas

- Strong timezone normalization and match-level timing fields.
- Formal API versioning strategy.
- Stricter canonical contract enforcement and richer model semantics.

</deferred>

---

*Phase: 01-foundation-and-contracts*
*Context gathered: 2026-03-20*