# Phase 2: Secure Live Update Ingestion - Context

**Gathered:** 2026-03-20
**Status:** Ready for planning

<domain>
## Phase Boundary

Allow RC Drag Manager to securely call `POST /api/update` with live race payloads and replace the in-memory latest state when authorized and valid.

</domain>

<decisions>
## Implementation Decisions

### API Key Request Rules
- `POST /api/update` requires `X-API-KEY` header.
- Missing API key returns `401 Unauthorized`.
- Invalid API key returns the same `401 Unauthorized` behavior as missing key.
- Header handling follows standard HTTP behavior (case-insensitive header name).
- No alternate key sources are allowed (no query-string or request-body fallback).

### Update Endpoint Response Shape
- Successful updates return `200 OK` with a minimal confirmation payload.
- Unauthorized responses use a generic body: `{"error":"unauthorized"}`.
- Invalid payloads return `400 Bad Request`.
- No response diagnostics metadata in v1 (no timestamps/request IDs in response body).

### Payload Replace Semantics
- Each accepted update fully replaces the latest in-memory state.
- If `Matches` is null/missing, reject request with `400 Bad Request`.
- Required top-level string fields may be empty strings in v1 (accepted for sender flexibility).
- Unknown JSON fields are ignored for additive compatibility.

### API Key Configuration Behavior
- Canonical configuration key is `ApiKey`.
- Environment override key is `ApiKey`.
- If `ApiKey` is missing at startup, app fails fast.
- No built-in default API key for local development.

### Claude's Discretion
- Exact minimal success response payload field names.
- Internal implementation details for key comparison and validation flow, as long as external behavior matches decisions above.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase Scope and Acceptance
- `.planning/ROADMAP.md` - Phase 2 goal, required outcomes, and success criteria.
- `.planning/REQUIREMENTS.md` - API-01, API-02, API-03, CFG-01 requirements.
- `.planning/PROJECT.md` - v1 simplicity constraints and security/deployment context.
- `.planning/STATE.md` - current project position and prior phase outcomes.

### Prior Decision Continuity
- `.planning/phases/01-foundation-and-contracts/01-CONTEXT.md` - locked payload shape and v1 flexibility constraints carried into update ingestion.

No external ADR/spec documents were referenced in this discussion.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `src/RCDragLiveServer/Models/LiveRaceState.cs` and `src/RCDragLiveServer/Models/LiveMatch.cs`: existing v1 payload contracts to be used directly for update input.
- `src/RCDragLiveServer/Services/ILiveRaceStateStore.cs`: existing abstraction for latest-state read/write.
- `src/RCDragLiveServer/Services/InMemoryLiveRaceStateStore.cs`: existing singleton in-memory implementation for latest-state replacement.

### Established Patterns
- `src/RCDragLiveServer/Program.cs` already uses minimal-hosting style with DI registration and controller routing.
- `ILiveRaceStateStore` is registered as singleton in startup and is the intended state boundary.

### Integration Points
- Add/update API controller endpoint at `POST /api/update` to validate key + payload then call `SetLatest`.
- Add API key configuration binding/validation at startup path in `Program.cs` and config files.

</code_context>

<specifics>
## Specific Ideas

- Keep response and validation behavior explicit but minimal.
- Preserve additive payload compatibility while still rejecting structurally invalid required fields (`Matches` null/missing).

</specifics>

<deferred>
## Deferred Ideas

- Stronger authentication mechanisms beyond shared API key.
- Richer diagnostics metadata in update responses.
- Strict schema enforcement/versioning policy for payload evolution.

</deferred>

---

*Phase: 02-secure-live-update-ingestion*
*Context gathered: 2026-03-20*