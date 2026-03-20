---
phase: 02-secure-live-update-ingestion
plan: 02
subsystem: update-endpoint
tags: [api, update, controller]
requires: [02-01]
provides:
  - Protected POST /api/update endpoint
  - Raw JSON matches-presence validation
  - Full in-memory latest-state replacement on success
affects: [phase-02, api-ingestion]
tech-stack:
  added: []
  patterns: [raw-json-presence-check, full-snapshot-replacement]
key-files:
  created: [src/RCDragLiveServer/Controllers/LiveUpdateController.cs]
  modified: []
key-decisions:
  - "RequireApiKey guard is applied directly to update controller."
  - "Missing or null matches is rejected as invalid_payload before deserialization/state mutation."
  - "Accepted payload replaces state via SetLatest and returns minimal success JSON."
patterns-established:
  - "Controller-level API key protection using RequireApiKey attribute."
  - "Deterministic response contract for update endpoint: 400 invalid_payload / 200 updated."
requirements-completed: [API-01, API-03]
duration: 8min
completed: 2026-03-20
---

# Phase 02 Plan 02: Implement the update endpoint and state storage workflow Summary

**Implemented protected `POST /api/update` with locked payload validation and full in-memory state replacement.**

## Performance

- **Duration:** 8 min
- **Tasks:** 2
- **Files modified:** 1

## Accomplishments

- Added `LiveUpdateController` at route `api/update` with `[RequireApiKey]` protection.
- Implemented raw-body JSON parsing and explicit `matches` field presence check to detect omitted fields.
- Returns `400` `{\"error\":\"invalid_payload\"}` for null/missing body, invalid JSON, missing `matches`, or `matches: null`.
- Deserializes accepted payload into `LiveRaceState`, replaces latest state via `ILiveRaceStateStore.SetLatest`, and returns `200` `{\"status\":\"updated\"}`.
- Unknown fields remain additive-compatible via default deserialization behavior.

## Task Commits

1. **Task 1 & 2:** `1bb5739` - `feat(02-02): add protected post api update endpoint`

## Files Created/Modified

- `src/RCDragLiveServer/Controllers/LiveUpdateController.cs`

## Deviations from Plan

None - plan executed as written.

## Issues Encountered

None.

## Next Phase Readiness

- Phase 2 verification plan can now validate auth and payload handling paths against a real ingestion endpoint.

## Self-Check: PASSED

---
*Phase: 02-secure-live-update-ingestion*
*Completed: 2026-03-20*