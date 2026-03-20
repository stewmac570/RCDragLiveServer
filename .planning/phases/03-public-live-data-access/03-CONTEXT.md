# Phase 3: Public Live Data Access - Context

**Gathered:** 2026-03-20
**Status:** Ready for planning

<domain>
## Phase Boundary

Expose the current in-memory live race state through public read endpoints (`GET /api/live` and `GET /health`) without authentication.

</domain>

<decisions>
## Implementation Decisions

### No-Data Contract for `GET /api/live`
- Before any update is posted, return the default `LiveRaceState` object from the in-memory store.
- Keep `matches` as an empty array (`[]`) in no-data responses.
- Do not add `hasData` or server status flags in v1.
- Return `200 OK` for no-data responses.

### Public Response Shape
- `GET /api/live` returns raw `LiveRaceState` JSON (no wrapper envelope).
- No response metadata fields (such as `updatedAt`) in v1.
- Keep default ASP.NET Core JSON serializer behavior.
- No query parameters for shaping/detail selection in v1.

### Health Endpoint Contract
- `GET /health` returns minimal JSON: `{ "status": "healthy" }`.
- Health is liveness-only and does not depend on whether live race data exists yet.
- `GET /health` is publicly accessible (no API key).
- No version/build/uptime metadata in health response for v1.

### Caching and Freshness
- `GET /api/live` sends explicit no-cache/no-store response headers.
- `GET /health` also sends explicit no-cache/no-store response headers.
- No ETag/conditional GET behavior in v1.
- No explicit compression configuration in this phase.

### Claude's Discretion
- Exact header set values used to enforce no-cache/no-store behavior, as long as intent is explicit and deterministic.
- Endpoint implementation style (controller vs mapped endpoint) as long as external contracts above are preserved.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Scope and Requirements
- `.planning/ROADMAP.md` - Phase 3 goal, success criteria, and plan targets.
- `.planning/REQUIREMENTS.md` - API-04 and API-05 requirements.
- `.planning/PROJECT.md` - v1 simplicity and public spectator access priorities.
- `.planning/STATE.md` - current project position and execution continuity.

### Prior Context Dependencies
- `.planning/phases/01-foundation-and-contracts/01-CONTEXT.md` - baseline contract and in-memory state assumptions.
- `.planning/phases/02-secure-live-update-ingestion/02-CONTEXT.md` - update endpoint behavior and payload/state replacement decisions.

No external ADR/spec docs were referenced during this discussion.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `src/RCDragLiveServer/Services/ILiveRaceStateStore.cs` and `InMemoryLiveRaceStateStore.cs` already provide latest-state read/write abstraction.
- `src/RCDragLiveServer/Models/LiveRaceState.cs` and `LiveMatch.cs` define the raw response contract for `GET /api/live`.
- `src/RCDragLiveServer/Controllers/LiveUpdateController.cs` already populates in-memory state via secure updates.

### Established Patterns
- `Program.cs` uses minimal-hosting with controller mapping and DI wiring.
- Security gate currently applies only where `[RequireApiKey]` is added; public endpoints can remain unauthenticated by default.

### Integration Points
- Add public read endpoint for latest state (`GET /api/live`) consuming `ILiveRaceStateStore.GetLatest()`.
- Add public `GET /health` endpoint with minimal healthy response contract.
- Apply explicit response headers for cache suppression on both public endpoints.

</code_context>

<specifics>
## Specific Ideas

- Keep public read endpoints simple and predictable for spectator clients.
- Avoid extra response wrappers/metadata until later phases require them.

</specifics>

<deferred>
## Deferred Ideas

- Rich health diagnostics (version, environment, component checks).
- ETag/conditional GET and advanced caching strategies.
- Response shaping/query parameter support for live data output.

</deferred>

---

*Phase: 03-public-live-data-access*
*Context gathered: 2026-03-20*