# RCDragLiveServer

## What This Is

RCDragLiveServer is a separate ASP.NET Core Web API service that receives live race state updates from the RC Drag Manager WinForms race control app and publishes the current event state publicly. It is intended for spectators and racers to view the latest race information quickly on mobile devices through a simple public web page and JSON endpoint.

## Core Value

Anyone at the track can reliably open a fast, simple phone-friendly page and see the current live race state without friction.

## Requirements

### Validated

- [x] ASP.NET Core Web API baseline created on .NET 8 (validated in Phase 1: Foundation and Contracts)
- [x] Core live payload contracts defined (`LiveRaceState`, `LiveMatch`) with v1-required fields (validated in Phase 1: Foundation and Contracts)
- [x] Render `PORT` host binding and in-memory latest-state store baseline implemented (validated in Phase 1: Foundation and Contracts)
- [x] Protected live update ingestion implemented with `POST /api/update` + `X-API-KEY` validation and latest-state replacement (validated in Phase 2: Secure Live Update Ingestion)

### Active

- [ ] Expose the latest live race state publicly as JSON
- [ ] Show a simple public live race page optimized for reliable mobile viewing
- [ ] Run cleanly as a Render-hosted public web service

### Out of Scope

- WinForms sender implementation in this repo - the existing RC Drag Manager app will send updates separately
- Advanced bracket visuals - not part of the low-friction v1 viewing goal
- Separate frontend project - server-rendered HTML inside the API project is sufficient for v1
- Persistent storage or history - v1 stores only the latest event state in memory to keep the service simple

## Context

- The publisher is the user's Windows Forms race control app, RC Drag Manager, running on a PC at the track.
- RCDragLiveServer is a separate project from the WinForms app and acts as the public-facing live display service.
- The service will be deployed to Render as a public web service.
- The initial page should focus only on event name, event date, current round, next-up race, and the current match list.
- Reliability, phone usability, fast loading, and minimal moving parts are higher priority than richer display detail.
- The service should remain stateless except for the in-memory latest event state.

## Constraints

- **Platform**: Target .NET 8 if available, otherwise latest stable ASP.NET Core Web API - aligns with the requested deployment stack
- **Deployment**: Must run cleanly on Render and bind to the `PORT` environment variable if present - required for hosted deployment
- **Architecture**: Keep the service stateless except for in-memory latest event state - simplifies hosting and maintenance
- **Security**: `POST /api/update` must require an API key header - prevents unauthenticated public updates
- **UX**: Public homepage must be mobile-friendly, fast to load, and easy to read - this is the top v1 priority
- **Scope**: Keep implementation minimal and production-clean - avoid overbuilding v1

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Keep homepage server-rendered inside the API project | Minimizes moving parts and speeds up delivery for v1 | Confirmed (Phase 1 baseline aligns with this direction) |
| Store only the latest event state in memory | Current need is live display, not historical replay, and this keeps the service simple | Confirmed (`ILiveRaceStateStore` + `InMemoryLiveRaceStateStore`) |
| Use API key protected update ingestion | Prevents unauthenticated public writes to live race state | Confirmed (`POST /api/update` guarded by `X-API-KEY`) |
| Prioritize reliable low-friction viewing over richer detail | The main v1 success case is quick mobile visibility at the track | - Pending |
| Use RC Drag Manager as the update publisher | The user's existing race control app already owns the live race data | - Pending |

---
*Last updated: 2026-03-20 after Phase 2 execution*
