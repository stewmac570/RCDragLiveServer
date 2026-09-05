# RCDragLiveServer - Current Reference

Last updated: 2026-09-06

`RCDragLiveServer` is the public live scoreboard companion for the RC Drag Manager desktop app.

Desktop repository:

```text
C:\Users\Stewart McMillan\source\repos\RC-Drag-Manager
```

Desktop integration docs:

```text
C:\Users\Stewart McMillan\source\repos\RC-Drag-Manager\Docs\12_Live_Server_Integration.md
```

## Runtime Shape

- ASP.NET Core app.
- Stores active race state in memory.
- Requires `ApiKey` config at startup.
- Protected desktop endpoints use `X-API-KEY`.
- Public scoreboard and dial-in pages are generated server-side in `PublicLiveController`.
- Pages do not poll. They hold a Server-Sent Events connection and are pushed to
  when state changes; verified to stream through Render's proxy rather than being
  buffered (~0.3s end to end, 30 concurrent connections).

## Endpoints

| Method | Route | Purpose | Auth |
| --- | --- | --- | --- |
| `GET` | `/` | Public active-event landing page, including the driver dial-in form. | none |
| `GET` | `/event/{eventId}` | Public event scoreboard, class tabs, bracket, winners, standings, and dial-in form. | none |
| `GET` | `/api/live` | Public JSON snapshot of active class states. | none |
| `GET` | `/health` | Health check. | none |
| `POST` | `/api/dialin` | Public dial-in submission. | none, rate-limited |
| `POST` | `/api/dialin/login` | Public driver login; checks a PIN without writing a dial-in. | none, rate-limited |
| `GET` | `/stream` | SSE push channel for the landing page. | none |
| `GET` | `/event/{eventId}/stream` | SSE push channel for one event page. | none |
| `GET` | `/api/dialin?eventId=...` | Desktop polling for submitted dial-ins. | `X-API-KEY` |
| `POST` | `/api/update` | Desktop live state ingestion. | `X-API-KEY` |
| `POST` | `/api/reset` | Clear one event. | `X-API-KEY` |

## Current Data Contract

Main model: `LiveRaceState`

```json
{
  "eventId": "",
  "eventName": "",
  "eventDate": "",
  "classType": "",
  "raceType": "",
  "currentRound": "",
  "nextUp": "",
  "rrStandings": null,
  "drivers": [],
  "matches": [],
  "winners": [],
  "dialInLocked": false
}
```

`POST /api/update` requires the `matches` property. It can be an empty array, but it cannot be missing or null.

`drivers` is the class entry list (`driverId`, `driverName`, `dialIn`) and is what
makes the dial-in form usable before a bracket exists. Until a round is generated
`matches` is empty, so without `drivers` the site has no names to offer. The desktop
sends it on every push, including the `EventStarted` push and after a roster edit.

## State Behavior

- Events are bucketed by event name first, then event id, then `(default)`.
- Class states are stored under each event bucket by class type.
- Event aliases include event name, event id, and GUID `N`/`D` formats.
- Active event buckets expire after two hours without updates.
- New session ids for an existing event bucket clear stale class state and dial-ins.
- State is volatile; app restart clears active events and dial-ins.

## Live Updates

Both public pages open an `EventSource` and are pushed to when state changes; there
is no polling and no page reload. A push carries no payload -- the page re-reads the
board itself and swaps it in place, which keeps rendering in one place and means a
burst of desktop pushes costs one refresh rather than several.

Publishing happens on desktop state ingestion, on event clear, and on a public
dial-in submission, so a dial-in entered on a phone reaches every other viewer.
Every publish also wakes the landing stream, so an event appearing or finishing
shows up there.

Idle connections get a keep-alive comment every 20 seconds. A page whose stream
never establishes falls back to a 30-second poll.

Subscribers are held in process. Running more than one instance would mean a push
landing on one instance never reaching subscribers on another -- the same
limitation the in-memory state store already has.

## Dial-In Behavior

Drivers log in on the landing page (`/`) or their event page (`/event/{eventId}`):
pick a name, enter a 4-digit PIN, then set a time. The landing page embeds the
roster for every active event as JSON and shows an event picker when more than one
event is live; the picker sits outside the form so a lock on one event cannot
strand a driver whose own event is still open.

`POST /api/dialin/login` verifies a PIN without writing a dial-in. An unclaimed
driver id accepts any well-formed PIN -- the first save is what claims it. The PIN
is held in a page-local variable and is never persisted.

Public dial-in posts validate:

- event id present,
- driver id is positive and appears in the event's entry list or match data,
- dial-in is positive and finite,
- PIN is present and exactly four digits,
- per-event/per-driver in-memory cooldown permits the update.

A PIN is mandatory. The first submission for a driver id claims it and stores the
PIN as a BCrypt hash; every later change to that driver must present the same PIN.
Submissions with a missing or malformed PIN are rejected with `invalid_pin_format`,
and a wrong PIN with `invalid_pin`.

A locked event does not refuse the write. `dialInLocked` means a round has already
been generated, so the submission is stored and the response carries
`pending: true`; the page tells the driver their time takes effect next race.

The desktop app polls protected dial-in data and applies updates by driver id.

Roster changes are handled on the client: a driver who is signed in and still
entered is left alone, including a dial-in typed but not yet saved. One who is no
longer in the event -- the RD added a driver and regenerated -- is signed out with
an explanation and the name list rebuilt.

## Tests

Tests live under:

```text
tests\RCDragLiveServer.Tests
```

Current coverage includes:

- update payload contract,
- public landing/event page behavior,
- dial-in controller validation,
- in-memory live race state store,
- in-memory dial-in store,
- in-memory dial-in rate limiter,
- live update publish/subscribe, including event isolation, burst collapsing,
  and that a desktop push wakes both the event and landing streams.

## Not Implemented

- Persistent state storage.
- Timing/Portatree result fields.
- OBS-specific timing endpoint.
- Per-user authentication for public dial-in submissions beyond the driver PIN.
- PIN recovery or reset (a forgotten PIN needs the event to be reset or restarted).
- Multi-instance deployment (SSE subscribers and race state are both in process).
