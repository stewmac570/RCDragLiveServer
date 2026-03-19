# Requirements: RCDragLiveServer

**Defined:** 2026-03-19
**Core Value:** Anyone at the track can reliably open a fast, simple phone-friendly page and see the current live race state without friction.

## v1 Requirements

### API

- [ ] **API-01**: RC Drag Manager can `POST /api/update` with a JSON payload representing the current live race state
- [ ] **API-02**: `POST /api/update` rejects requests that do not include a valid `X-API-KEY` header
- [ ] **API-03**: The service stores only the latest live race state in memory
- [ ] **API-04**: Anyone can `GET /api/live` and receive the latest live race state as JSON
- [ ] **API-05**: `GET /health` returns a healthy response suitable for Render health checks

### Data

- [ ] **DATA-01**: The project defines a clear `LiveRaceState` model for the live event state
- [ ] **DATA-02**: The project defines a clear `LiveMatch` model for current match entries
- [ ] **DATA-03**: `LiveRaceState` includes event name, event date, current round, next-up race, and current match list

### UI

- [ ] **UI-01**: Anyone can open `/` and see a server-rendered HTML live race page
- [ ] **UI-02**: The homepage is mobile-friendly and easy to read on a phone
- [ ] **UI-03**: The homepage refreshes automatically every 10 seconds
- [ ] **UI-04**: The homepage shows event name, event date, current round, next-up race, and current match list
- [ ] **UI-05**: The homepage loads even when no live race state has been posted yet and shows a clear empty-state message

### Configuration

- [ ] **CFG-01**: The API key is configured through app settings and can be overridden by environment variables

### Hosting

- [x] **HOST-01**: The app targets .NET 8 if available, otherwise the latest stable ASP.NET Core Web API
- [ ] **HOST-02**: The app binds to the `PORT` environment variable when present so it runs on Render
- [ ] **HOST-03**: The service remains stateless except for the in-memory latest live race state
- [ ] **HOST-04**: The repository includes a README with local run steps and Render deployment notes

## v2 Requirements

### UI

- **UI-06**: Spectators can view richer race visuals such as bracket displays

### Data

- **DATA-04**: The service stores historical event state or replay data

### API

- **API-06**: The update endpoint supports stronger authentication than a shared API key

### Web

- **WEB-01**: The public live display is split into a separate frontend project

## Out of Scope

| Feature | Reason |
|---------|--------|
| WinForms sender implementation in this repo | The existing RC Drag Manager app will publish updates separately |
| Persistent database storage | v1 only needs the latest live state and should stay simple |
| Advanced spectator UI or bracket visuals | Reliability and low-friction phone viewing take priority in v1 |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| API-01 | Phase 2 | Pending |
| API-02 | Phase 2 | Pending |
| API-03 | Phase 2 | Pending |
| API-04 | Phase 3 | Pending |
| API-05 | Phase 1 | Pending |
| DATA-01 | Phase 1 | Pending |
| DATA-02 | Phase 1 | Pending |
| DATA-03 | Phase 1 | Pending |
| UI-01 | Phase 4 | Pending |
| UI-02 | Phase 4 | Pending |
| UI-03 | Phase 4 | Pending |
| UI-04 | Phase 4 | Pending |
| UI-05 | Phase 4 | Pending |
| CFG-01 | Phase 2 | Pending |
| HOST-01 | Phase 1 | Complete |
| HOST-02 | Phase 1 | Pending |
| HOST-03 | Phase 1 | Pending |
| HOST-04 | Phase 5 | Pending |

**Coverage:**
- v1 requirements: 18 total
- Mapped to phases: 18
- Unmapped: 0

---
*Requirements defined: 2026-03-19*
*Last updated: 2026-03-19 after initial definition*
