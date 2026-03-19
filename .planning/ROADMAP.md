# Roadmap: RCDragLiveServer

## Overview

This roadmap builds RCDragLiveServer from a minimal deployable ASP.NET Core service into a public live race display that is secure for updates, simple for spectators, and ready for Render deployment. The phases move from service foundation and data contracts into protected update ingestion, public JSON access, the mobile-friendly live page, and final deployment documentation.

## Phases

- [x] **Phase 1: Foundation and Contracts** - Create the ASP.NET Core service baseline, hosting setup, and core live race models (completed 2026-03-19)
- [ ] **Phase 2: Secure Live Update Ingestion** - Add the protected update API, API key configuration, and in-memory latest state storage
- [ ] **Phase 3: Public Live Data Access** - Expose the latest live state publicly through JSON and health endpoints
- [ ] **Phase 4: Public Mobile Live Page** - Deliver the simple server-rendered homepage for spectators
- [ ] **Phase 5: Production Readiness and Documentation** - Add README guidance and final deployment-facing cleanup

## Phase Details

### Phase 1: Foundation and Contracts
**Goal**: Establish the project skeleton, Render-friendly hosting configuration, and the core models that define the live race payload.
**Depends on**: Nothing (first phase)
**Requirements**: DATA-01, DATA-02, DATA-03, HOST-01, HOST-02, HOST-03
**Success Criteria** (what must be TRUE):
1. The project runs as an ASP.NET Core Web API targeting .NET 8 or the latest stable supported option.
2. The app can bind to the `PORT` environment variable and is structured for Render hosting.
3. `LiveRaceState` and `LiveMatch` clearly represent the required live display data.
**Plans**: 3 plans

Plans:
- [x] 01-01: Create the ASP.NET Core Web API project structure and hosting baseline
- [x] 01-02: Define and validate the core live race models
- [x] 01-03: Add Render-friendly startup configuration and in-memory state service abstractions

### Phase 2: Secure Live Update Ingestion
**Goal**: Allow RC Drag Manager to push the latest race state securely and update the in-memory state.
**Depends on**: Phase 1
**Requirements**: API-01, API-02, API-03, CFG-01
**Success Criteria** (what must be TRUE):
1. RC Drag Manager can submit the current live race state to `POST /api/update`.
2. Requests without a valid `X-API-KEY` are rejected.
3. Successful updates replace the in-memory latest live race state.
**Plans**: 3 plans

Plans:
- [ ] 02-01: Add API key configuration and request validation
- [ ] 02-02: Implement the update endpoint and state storage workflow
- [ ] 02-03: Verify update payload handling and failure responses

### Phase 3: Public Live Data Access
**Goal**: Expose the current live state publicly and provide operational health checks.
**Depends on**: Phase 2
**Requirements**: API-04, API-05
**Success Criteria** (what must be TRUE):
1. Anyone can call `GET /api/live` and receive the latest live race state as JSON.
2. `GET /health` returns a healthy response suitable for deployment monitoring.
3. Public read access does not require authentication.
**Plans**: 2 plans

Plans:
- [ ] 03-01: Add the public live data endpoint
- [ ] 03-02: Add the health endpoint and confirm public behavior

### Phase 4: Public Mobile Live Page
**Goal**: Deliver a simple, reliable, phone-friendly homepage that shows the current live race summary.
**Depends on**: Phase 3
**Requirements**: UI-01, UI-02, UI-03, UI-04, UI-05
**Success Criteria** (what must be TRUE):
1. Spectators can open `/` and see a server-rendered live race page on a phone.
2. The page auto-refreshes every 10 seconds and clearly shows the required live race fields.
3. The page handles the no-data case cleanly without errors or broken layout.
**Plans**: 3 plans

Plans:
- [ ] 04-01: Add the homepage endpoint and HTML rendering
- [ ] 04-02: Implement mobile-friendly layout and required live race sections
- [ ] 04-03: Add refresh behavior and empty-state handling

### Phase 5: Production Readiness and Documentation
**Goal**: Finish the project with concise documentation and deployment guidance for local use and Render.
**Depends on**: Phase 4
**Requirements**: HOST-04
**Success Criteria** (what must be TRUE):
1. The repository includes a README with local setup and run steps.
2. The README explains Render deployment expectations, including API key and port configuration.
3. The service structure remains minimal and maintainable for future iteration.
**Plans**: 2 plans

Plans:
- [ ] 05-01: Write local development and deployment documentation
- [ ] 05-02: Review project simplicity, configuration, and maintenance notes

## Progress

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Foundation and Contracts | 3/3 | Complete   | 2026-03-19 |
| 2. Secure Live Update Ingestion | 0/3 | Not started | - |
| 3. Public Live Data Access | 0/2 | Not started | - |
| 4. Public Mobile Live Page | 0/3 | Not started | - |
| 5. Production Readiness and Documentation | 0/2 | Not started | - |
