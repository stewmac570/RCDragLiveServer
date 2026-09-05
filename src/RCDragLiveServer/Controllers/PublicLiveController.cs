using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using RCDragLiveServer.Models;
using RCDragLiveServer.Services;

namespace RCDragLiveServer.Controllers;

[ApiController]
[Route("")]
public sealed class PublicLiveController : ControllerBase
{
    private readonly ILiveRaceStateStore stateStore;
    private readonly IDialInStore dialInStore;
    private readonly ILiveUpdateBroadcaster broadcaster;

    public PublicLiveController(
        ILiveRaceStateStore stateStore,
        IDialInStore dialInStore,
        ILiveUpdateBroadcaster broadcaster)
    {
        this.stateStore = stateStore;
        this.dialInStore = dialInStore;
        this.broadcaster = broadcaster;
    }

    [HttpGet("")]
    public ContentResult Home()
    {
        ApplyNoCacheHeaders();

        var events = stateStore.GetActiveEvents();

        return Content(BuildLandingPage(events, BuildDialInEventPayloads(events)), "text/html; charset=utf-8");
    }

    [HttpGet("event/{eventId}")]
    public ContentResult GetEventPage(string eventId)
    {
        ApplyNoCacheHeaders();

        var classes = stateStore.GetEvent(eventId);

        if (classes == null || classes.Count == 0)
            return Content(BuildNoEventPage(), "text/html; charset=utf-8");

        var eventKey = stateStore.ResolveEventKey(eventId);
        var submittedDialIns = dialInStore.GetAll(eventKey);

        return Content(BuildHomePage(classes, submittedDialIns, dialInStore.IsLocked(eventKey), eventKey), "text/html; charset=utf-8");
    }

    [HttpGet("api/live")]
    public ActionResult<IEnumerable<LiveRaceState>> GetLive()
    {
        ApplyNoCacheHeaders();

        return Ok(stateStore.GetAll().Values.ToList());
    }

    /// <summary>
    /// Push channel for a page. Sends nothing but "something changed" -- the page
    /// then re-reads the board itself. Verified to stream through Render's proxy
    /// rather than being buffered (~0.3s end to end, 30 concurrent connections).
    /// </summary>
    [HttpGet("event/{eventId}/stream")]
    public Task StreamEvent(string eventId, CancellationToken cancellationToken) =>
        StreamChanges(stateStore.ResolveEventKey(eventId), cancellationToken);

    /// <summary>Landing page stream. Keyed on the whole site rather than one event,
    /// so a new event appearing wakes it too.</summary>
    [HttpGet("stream")]
    public Task StreamLanding(CancellationToken cancellationToken) =>
        StreamChanges(LandingStreamKey, cancellationToken);

    private async Task StreamChanges(string eventKey, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-store, max-age=0";
        Response.Headers.Pragma = "no-cache";

        // Tells an nginx-family reverse proxy not to buffer. Render sits behind one.
        Response.Headers["X-Accel-Buffering"] = "no";

        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        using var eventSubscription = broadcaster.Subscribe(eventKey);
        using var landingSubscription = string.Equals(eventKey, LandingStreamKey, StringComparison.Ordinal)
            ? null
            : broadcaster.Subscribe(LandingStreamKey);

        // Say hello immediately so the browser knows the channel is open and does
        // not sit on its reconnect timer wondering.
        await WriteSseAsync("ready", cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            var changed = await WaitForAnyAsync(eventSubscription, landingSubscription, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                break;

            if (changed)
            {
                await WriteSseAsync("changed", cancellationToken);
            }
            else
            {
                // Heartbeat: an idle connection through a proxy gets reaped, and a
                // race has long quiet stretches between winners.
                await WriteCommentAsync(cancellationToken);
            }
        }
    }

    private static async Task<bool> WaitForAnyAsync(
        ILiveUpdateSubscription first,
        ILiveUpdateSubscription? second,
        CancellationToken cancellationToken)
    {
        using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        heartbeat.CancelAfter(HeartbeatInterval);

        var waits = second is null
            ? new[] { first.WaitForChangeAsync(heartbeat.Token) }
            : new[] { first.WaitForChangeAsync(heartbeat.Token), second.WaitForChangeAsync(heartbeat.Token) };

        var finished = await Task.WhenAny(waits);
        return finished.Status == TaskStatus.RanToCompletion && finished.Result;
    }

    private async Task WriteSseAsync(string eventName, CancellationToken cancellationToken)
    {
        await Response.WriteAsync($"event: {eventName}\ndata: 1\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private async Task WriteCommentAsync(CancellationToken cancellationToken)
    {
        await Response.WriteAsync(": keep-alive\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        ApplyNoCacheHeaders();

        return Ok(new { status = "healthy" });
    }

    /// <summary>Every push also wakes the landing page, so a new or finished event
    /// shows up there without its own subscription per event.</summary>
    public const string LandingStreamKey = "(landing)";

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

    private void ApplyNoCacheHeaders()
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
    }

    // The landing page carries its own dial-in form, so it needs the driver roster
    // for every active event up front: drivers arrive at "/" and shouldn't have to
    // find their event page before they can set a time.
    private List<DialInEventPayload> BuildDialInEventPayloads(IReadOnlyList<EventSummary> events)
    {
        var payloads = new List<DialInEventPayload>();

        foreach (var summary in events)
        {
            var classes = stateStore.GetEvent(summary.EventId);
            if (classes is null || classes.Count == 0)
                continue;

            var eventKey = stateStore.ResolveEventKey(summary.EventId);
            var drivers = CollectDrivers(classes.Values, dialInStore.GetAll(eventKey));
            if (drivers.Count == 0)
                continue;

            payloads.Add(new DialInEventPayload
            {
                EventKey = eventKey,
                EventName = string.IsNullOrWhiteSpace(summary.EventName) ? eventKey : summary.EventName,
                Locked = dialInStore.IsLocked(eventKey),
                Drivers = drivers
                    .Select(d => new DialInDriverPayload
                    {
                        Id = d.Id,
                        Name = d.Name,
                        DialIn = FormatDialIn(d.DialIn)
                    })
                    .ToList()
            });
        }

        return payloads;
    }

    // One driver may appear in several matches and classes; keep the first entry
    // that carries a dial-in so the roster shows a time wherever one exists.
    private static List<(int Id, string Name, double? DialIn)> CollectDrivers(
        IEnumerable<LiveRaceState> classes,
        IReadOnlyDictionary<int, double?> submittedDialIns)
    {
        var classList = classes.ToList();

        // The class entry list is the only source of names before a round is
        // generated -- Matches is empty until then. Matches still contribute so a
        // driver who somehow only appears in a bracket is never dropped.
        var fromEntries = classList
            .SelectMany(s => s.Drivers)
            .Select(d => (
                Id: d.DriverId,
                Name: d.DriverName,
                DialIn: EffectiveDialIn(d.DriverId, d.DialIn, submittedDialIns)));

        var fromMatches = classList
            .SelectMany(s => s.Matches)
            .SelectMany(m => new[]
            {
                (Id: m.LeftDriverId,  Name: string.IsNullOrEmpty(m.LeftDriver)  ? m.Driver1 : m.LeftDriver, DialIn: EffectiveDialIn(m.LeftDriverId, m.LeftDriverDialIn, submittedDialIns)),
                (Id: m.RightDriverId, Name: string.IsNullOrEmpty(m.RightDriver) ? m.Driver2 : m.RightDriver, DialIn: EffectiveDialIn(m.RightDriverId, m.RightDriverDialIn, submittedDialIns))
            });

        return fromEntries
            .Concat(fromMatches)
            .Where(p => p.Id > 0 && !string.IsNullOrWhiteSpace(p.Name) && !string.Equals(p.Name, "BYE", StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => p.Id)
            .Select(g =>
            {
                var first = g.First();
                return (first.Id, first.Name, DialIn: g.Select(p => p.DialIn).FirstOrDefault(v => v.HasValue));
            })
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildHomePage(
        Dictionary<string, LiveRaceState> classes,
        IReadOnlyDictionary<int, double?> submittedDialIns,
        bool dialInLocked,
        string eventId)
    {
        if (classes.Count == 0)
        {
            return BuildNoEventPage();
        }

        var sortedKeys = classes.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        bool multiClass = true;

        var allDrivers = CollectDrivers(classes.Values, submittedDialIns);

        StringBuilder content = new StringBuilder();

        content.AppendLine("<div id=\"live-board\">");

        if (multiClass)
        {
            content.AppendLine("<div class=\"tab-bar\">");
            for (int i = 0; i < sortedKeys.Count; i++)
            {
                content.AppendLine($"  <button class=\"tab-btn\" data-tab=\"{i}\">{Html(sortedKeys[i])}</button>");
            }
            content.AppendLine("</div>");

            for (int i = 0; i < sortedKeys.Count; i++)
            {
                content.AppendLine($"<div class=\"tab-panel\" data-index=\"{i}\">");
                content.Append(BuildClassPanel(classes[sortedKeys[i]], submittedDialIns));
                content.AppendLine("</div>");
            }
        }
        else
        {
            content.Append(BuildClassPanel(classes[sortedKeys[0]], submittedDialIns));
        }

        content.AppendLine("</div>");

        content.Append(BuildDialInForm(allDrivers, dialInLocked));

        var pagePayload = new List<DialInEventPayload>
        {
            new DialInEventPayload
            {
                EventKey = eventId,
                EventName = eventId,
                Locked = dialInLocked,
                Drivers = allDrivers
                    .Select(d => new DialInDriverPayload { Id = d.Id, Name = d.Name, DialIn = FormatDialIn(d.DialIn) })
                    .ToList()
            }
        };

        var css = """
        * { box-sizing: border-box; }
        body { margin:0; padding:0; font-family:Arial,Helvetica,sans-serif; background:#0a0f1a; color:#f1f5f9; }
        .wrap { width:100%; max-width:760px; margin:0 auto; padding:16px; }
        .hero { background:#1e293b; border:1px solid #334155; border-radius:16px; padding:20px; margin-bottom:16px; }
        .title { font-size:32px; font-weight:900; line-height:1.1; margin:0 0 6px 0; color:#f8fafc; }
        .event-meta { font-size:15px; color:#94a3b8; margin:0 0 10px 0; }
        .badge { display:inline-block; background:#1d4ed8; color:#e0f2fe; font-size:12px; font-weight:700; text-transform:uppercase; letter-spacing:.07em; padding:3px 10px; border-radius:999px; margin-right:6px; }
        .badge-race { background:#7c3aed; color:#ede9fe; }
        .tab-bar { display:flex; gap:8px; flex-wrap:wrap; margin-bottom:20px; }
        .tab-btn { background:#1e293b; border:1px solid #334155; border-radius:999px; color:#94a3b8; cursor:pointer; font-size:13px; font-weight:700; padding:7px 20px; text-transform:uppercase; letter-spacing:.07em; transition:background .15s,color .15s,border-color .15s; }
        .tab-btn:hover { background:#263348; color:#e2e8f0; }
        .tab-btn.active { background:#1d4ed8; border-color:#1d4ed8; color:#fff; }
        .tab-panel { display:none; } .tab-panel.active { display:block; }
        .section-title { font-size:13px; font-weight:700; text-transform:uppercase; letter-spacing:.1em; color:#64748b; margin:20px 0 8px 0; }
        .panel { background:#1e293b; border:1px solid #334155; border-radius:16px; padding:16px; margin-bottom:12px; }
        .empty-state,.empty-box { background:#1e293b; border:1px dashed #475569; border-radius:12px; padding:18px; text-align:center; color:#94a3b8; font-size:15px; margin-bottom:12px; }
        .next-up-drivers { font-size:36px; font-weight:900; color:#fbbf24; line-height:1.15; word-break:break-word; padding:10px 0 4px; }
        .round-header { font-size:13px; font-weight:700; text-transform:uppercase; letter-spacing:.1em; color:#38bdf8; margin:14px 0 6px 0; padding-bottom:4px; border-bottom:1px solid #1e40af; }
        .match-list { display:grid; gap:10px; margin-bottom:4px; }
        .match-card { background:#1e293b; border:1px solid #334155; border-radius:12px; padding:0; display:flex; align-items:stretch; }
        .lane-slot { flex:1; display:flex; flex-direction:column; align-items:center; text-align:center; padding:12px 10px; }
        .lane-label { font-size:10px; font-weight:700; text-transform:uppercase; letter-spacing:.1em; color:#64748b; margin-bottom:6px; }
        .driver { font-size:18px; font-weight:800; line-height:1.2; word-break:break-word; }
        .driver.winner { color:#4ade80; }
        .driver.loser { color:#64748b; font-size:16px; font-weight:600; text-decoration:line-through; }
        .dial-in-badge { display:inline-block; background:#0f2d4a; color:#7dd3fc; font-size:12px; font-weight:700; letter-spacing:.04em; padding:2px 8px; border-radius:999px; vertical-align:middle; margin-left:8px; font-family:'Courier New',Courier,monospace; }
        .win-badge { display:inline-block; background:#14532d; color:#86efac; font-size:11px; font-weight:700; letter-spacing:.08em; padding:2px 7px; border-radius:999px; vertical-align:middle; margin-left:6px; }
        .vs { font-size:11px; font-weight:700; color:#64748b; text-transform:uppercase; letter-spacing:.1em; display:flex; align-items:center; padding:0 4px; flex-shrink:0; }
        .winners-table { width:100%; border-collapse:collapse; font-size:15px; }
        .winners-table th { text-align:left; font-size:11px; font-weight:700; text-transform:uppercase; letter-spacing:.08em; color:#64748b; padding:6px 10px; border-bottom:1px solid #334155; }
        .winners-table td { padding:8px 10px; border-bottom:1px solid #1e293b; vertical-align:middle; }
        .winners-table tr:last-child td { border-bottom:none; }
        .winner-cell { font-weight:700; color:#4ade80; }
        .loser-cell { color:#64748b; }
        .rr-standings { background:#0f172a; border:1px solid #334155; border-radius:12px; padding:14px 16px; font-family:'Courier New',Courier,monospace; font-size:13px; color:#e2e8f0; overflow-x:auto; white-space:pre; line-height:1.5; }
        .dialin-form { background:#1e293b; border:1px solid #334155; border-radius:16px; padding:20px; margin-bottom:16px; }
        .dialin-form h3 { margin:0 0 6px 0; font-size:16px; font-weight:700; color:#f8fafc; }
        .dialin-help { color:#94a3b8; font-size:13px; line-height:1.35; margin:0 0 14px 0; }
        .dialin-form label { display:block; font-size:12px; font-weight:700; text-transform:uppercase; letter-spacing:.08em; color:#94a3b8; margin-bottom:4px; }
        .dialin-form select,.dialin-form input[type=number],.dialin-form input[type=password] { width:100%; background:#0f172a; border:1px solid #475569; border-radius:8px; color:#f1f5f9; font-size:15px; padding:8px 12px; margin-bottom:12px; outline:none; }
        .dialin-form select:focus,.dialin-form input:focus { border-color:#3b82f6; }
        .dialin-form .form-row { display:grid; grid-template-columns:1fr 1fr; gap:12px; }
        .dialin-form button { width:100%; background:#1d4ed8; border:none; border-radius:8px; color:#fff; cursor:pointer; font-size:15px; font-weight:700; padding:10px; transition:background .15s; }
        .dialin-form button:hover:not(:disabled) { background:#2563eb; }
        .dialin-form button:disabled { background:#334155; color:#64748b; cursor:not-allowed; }
        .dialin-status { margin-top:10px; font-size:14px; font-weight:600; min-height:20px; text-align:center; }
        .dialin-status.ok { color:#4ade80; } .dialin-status.err { color:#f87171; } .dialin-status.info { color:#94a3b8; font-style:italic; }
        .dialin-notice { background:#451a03; border:1px solid #92400e; border-radius:12px; color:#fcd34d; font-size:13px; font-weight:600; padding:10px 14px; text-align:center; margin-bottom:12px; }
        .dialin-who { font-size:16px; font-weight:800; color:#f8fafc; margin-bottom:2px; }
        .dialin-current { font-size:13px; color:#94a3b8; margin-bottom:14px; }
        .dialin-current strong { color:#7dd3fc; font-family:'Courier New',Courier,monospace; font-size:15px; }
        .dialin-logout { margin-top:10px; background:transparent !important; border:1px solid #475569 !important; color:#94a3b8 !important; font-size:13px !important; font-weight:600 !important; padding:7px !important; }
        .dialin-logout:hover { border-color:#64748b !important; color:#e2e8f0 !important; }
        [hidden] { display:none !important; }
        .footer { margin-top:20px; text-align:center; color:#475569; font-size:12px; }
        @media(min-width:640px) { .match-list { grid-template-columns:1fr 1fr; } }
        @media(max-width:520px) { .dialin-form .form-row { grid-template-columns:1fr; gap:0; } }
""";

        var script = """
        (function () {
            var STORAGE_KEY = 'rcDragActiveClass';
            var CYCLE_MS = 8000;
            var buttons = [];
            var panels = [];
            var count = 0;
            var cycleTimer = null;

            // Set once a driver logs in.
            var dialInSession = null;

            function populateNames() {
                nameSelect.innerHTML = '';
                var blank = document.createElement('option');
                blank.value = '';
                blank.textContent = '\u2014 select your name \u2014';
                nameSelect.appendChild(blank);
                if (!raceEvent) return;
                raceEvent.drivers.forEach(function (d) {
                    var opt = document.createElement('option');
                    opt.value = String(d.id);
                    opt.setAttribute('data-name', d.name);
                    opt.setAttribute('data-dialin', d.dialIn || '');
                    opt.textContent = d.dialIn ? d.name + ' (' + d.dialIn + 's)' : d.name;
                    nameSelect.appendChild(opt);
                });
            }

            // The page no longer reloads. The server pushes when something changes and
            // only the board is swapped, so the bracket stays current while a driver
            // is part-way through entering a dial-in.
            var FALLBACK_MS = 30000;
            var refreshing = false;
            var fallbackTimer = null;

            function applyUpdate(html) {
                var doc = new DOMParser().parseFromString(html, 'text/html');

                var freshBoard = doc.getElementById('live-board');
                var board = document.getElementById('live-board');
                if (freshBoard && board) {
                    var activeLabel = null;
                    var activeBtn = document.querySelector('.tab-btn.active');
                    if (activeBtn) activeLabel = activeBtn.textContent.trim();

                    board.innerHTML = freshBoard.innerHTML;
                    rebindTabs(activeLabel);
                }

                var match = /var DIALIN_EVENTS = (\[.*?\]);/.exec(html);
                if (match) {
                    try { refreshRoster(JSON.parse(match[1])); } catch (e) {}
                }
            }

            function refreshRoster(fresh) {
                if (!fresh.length) return;
                DIALIN_EVENTS.length = 0;
                Array.prototype.push.apply(DIALIN_EVENTS, fresh);
                raceEvent = DIALIN_EVENTS[0];

                if (raceEvent.locked) {
                    noticeEl.hidden = false;
                    noticeEl.textContent = roundMessage();
                } else {
                    noticeEl.hidden = true;
                }

                if (!dialInSession) {
                    var chosen = nameSelect.value;
                    populateNames();
                    if (chosen) nameSelect.value = chosen;
                    return;
                }

                // Signed in: leave their typed time alone, and only intervene if the
                // RD regenerated the bracket without them.
                var stillEntered = raceEvent.drivers.some(function (d) { return d.id === dialInSession.driverId; });
                if (!stillEntered) {
                    dialInSession = null;
                    panel.hidden = true;
                    loginForm.hidden = false;
                    pinInput.value = '';
                    valueInput.value = '';
                    populateNames();
                    showStatus('The bracket changed and you are no longer in this event. Please log in again.', 'err');
                }
            }

            function refreshBoard() {
                if (refreshing) return;
                refreshing = true;
                fetch(location.href, { headers: { 'Accept': 'text/html' } })
                    .then(function (r) { return r.text(); })
                    .then(function (html) { applyUpdate(html); })
                    .catch(function () {})
                    .then(function () { refreshing = false; });
            }

            function startFallback() {
                if (fallbackTimer) return;
                fallbackTimer = setInterval(refreshBoard, FALLBACK_MS);
            }

            function connect() {
                if (typeof EventSource === 'undefined') { startFallback(); return; }
                try {
                    var stream = new EventSource('/event/' + encodeURIComponent(PAGE_EVENT_ID) + '/stream');
                    stream.addEventListener('changed', refreshBoard);
                    stream.onerror = function () { startFallback(); };
                } catch (e) {
                    startFallback();
                }
            }

            function activate(index) {
                buttons.forEach(function (b, i) { b.classList.toggle('active', i === index); });
                panels.forEach(function (p, i) { p.classList.toggle('active', i === index); });
            }

            function startCycle(fromIndex) {
                if (cycleTimer) clearInterval(cycleTimer);
                if (count < 2) return;
                var current = fromIndex;
                cycleTimer = setInterval(function () { current = (current + 1) % count; activate(current); }, CYCLE_MS);
            }

            // Re-read the tab elements after a board swap and restore the class the
            // viewer was looking at, so a push does not yank them to another class.
            function rebindTabs(preferredLabel) {
                if (cycleTimer) { clearInterval(cycleTimer); cycleTimer = null; }

                buttons = Array.from(document.querySelectorAll('.tab-btn'));
                panels = Array.from(document.querySelectorAll('.tab-panel'));
                count = buttons.length;
                if (count === 0) return;

                var stored = preferredLabel;
                if (!stored) { try { stored = localStorage.getItem(STORAGE_KEY); } catch (e) {} }

                var index = 0;
                var pinned = false;
                if (stored) {
                    buttons.forEach(function (btn, i) {
                        if (btn.textContent.trim() === stored) { index = i; pinned = true; }
                    });
                }

                activate(index);
                if (!pinned) startCycle(index);

                buttons.forEach(function (btn, i) {
                    btn.addEventListener('click', function () {
                        activate(i);
                        try { localStorage.setItem(STORAGE_KEY, btn.textContent.trim()); } catch (e) {}
                        if (cycleTimer) { clearInterval(cycleTimer); cycleTimer = null; }
                    });
                });
            }

            rebindTabs(null);

            var loginForm  = document.getElementById('dialin-login');
            var nameSelect = document.getElementById('dialin-name');
            var pinInput   = document.getElementById('dialin-pin');
            var panel      = document.getElementById('dialin-panel');
            var whoEl      = document.getElementById('dialin-who');
            var currentEl  = document.getElementById('dialin-current');
            var saveForm   = document.getElementById('dialin-form');
            var valueInput = document.getElementById('dialin-value');
            var saveBtn    = document.getElementById('dialin-submit');
            var logoutBtn  = document.getElementById('dialin-logout');
            var statusEl   = document.getElementById('dialin-status');
            var noticeEl   = document.getElementById('dialin-notice');

            var raceEvent = DIALIN_EVENTS.length ? DIALIN_EVENTS[0] : null;

            if (loginForm && raceEvent) {
                var showStatus = function (msg, cls) {
                    statusEl.textContent = msg;
                    statusEl.className = 'dialin-status ' + (cls || '');
                };

                var roundMessage = function () {
                    return 'A round has already been generated \u2014 your time will not take effect until the next race.';
                };

                if (raceEvent.locked) {
                    noticeEl.hidden = false;
                    noticeEl.textContent = roundMessage();
                }

                populateNames();

                var describeCurrent = function (dialIn) {
                    currentEl.innerHTML = dialIn
                        ? 'Your dial-in: <strong>' + dialIn + 's</strong>'
                        : 'You have not set a dial-in yet.';
                };

                var post = function (url, body) {
                    return fetch(url, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify(body)
                    }).then(function (r) {
                        return r.text().then(function (text) {
                            var parsed = {};
                            if (text) { try { parsed = JSON.parse(text); } catch (e) {} }
                            return { ok: r.ok, status: r.status, body: parsed };
                        });
                    });
                };

                var describeError = function (res, fallback) {
                    if (res.status === 429) return 'Too many attempts \u2014 wait a few seconds.';
                    if (res.body && res.body.error === 'invalid_pin') return 'Incorrect PIN.';
                    if (res.body && res.body.error === 'invalid_pin_format') return 'PIN must be exactly 4 digits.';
                    if (res.body && res.body.error === 'invalid_driver') return 'You are no longer entered in this event.';
                    if (res.body && res.body.error === 'invalid_dialin') return 'Enter a valid dial-in time (e.g. 3.250).';
                    return fallback;
                };

                var updateBadges = function (driverId, saved) {
                    document.querySelectorAll('[data-driver-id="' + driverId + '"]').forEach(function (el) {
                        var badge = el.querySelector('.dial-in-badge');
                        if (!badge) {
                            badge = document.createElement('span');
                            badge.className = 'dial-in-badge';
                            el.appendChild(badge);
                        }
                        badge.textContent = saved + 's';
                    });
                };

                loginForm.addEventListener('submit', function (e) {
                    e.preventDefault();
                    var driverId = parseInt(nameSelect.value, 10);
                    var pin = pinInput.value.trim();
                    var option = nameSelect.options[nameSelect.selectedIndex];
                    var name = option ? (option.getAttribute('data-name') || option.textContent) : '';
                    if (!driverId || driverId <= 0) { showStatus('Please select your name.', 'err'); return; }
                    if (!/^[0-9]{4}$/.test(pin)) { showStatus('Enter your 4-digit PIN.', 'err'); return; }

                    showStatus('Checking\u2026', 'info');
                    post('/api/dialin/login', { eventId: PAGE_EVENT_ID, driverId: driverId, pin: pin })
                        .then(function (res) {
                            if (!res.ok) { showStatus(describeError(res, 'Could not log in.'), 'err'); return; }
                            var dialIn = (res.body.dialIn === null || res.body.dialIn === undefined)
                                ? (option ? option.getAttribute('data-dialin') : '')
                                : Number(res.body.dialIn).toFixed(3);
                            dialInSession = { driverId: driverId, pin: pin };
                            whoEl.textContent = 'Signed in as ' + name;
                            describeCurrent(dialIn);
                            valueInput.value = dialIn || '';
                            loginForm.hidden = true;
                            panel.hidden = false;
                            pinInput.value = '';
                            showStatus('', '');
                            valueInput.focus();
                        })
                        .catch(function () { showStatus('Network error \u2014 try again.', 'err'); });
                });

                saveForm.addEventListener('submit', function (e) {
                    e.preventDefault();
                    if (!dialInSession) return;
                    var val = parseFloat(valueInput.value);
                    if (isNaN(val) || val <= 0) { showStatus('Enter a valid dial-in time (e.g. 3.250).', 'err'); return; }
                    saveBtn.disabled = true;
                    showStatus('Saving\u2026', 'info');
                    post('/api/dialin', {
                        eventId: PAGE_EVENT_ID,
                        driverId: dialInSession.driverId,
                        dialIn: val,
                        pin: dialInSession.pin
                    })
                        .then(function (res) {
                            saveBtn.disabled = false;
                            if (!res.ok) { showStatus(describeError(res, 'Error saving dial-in.'), 'err'); return; }
                            var saved = val.toFixed(3);
                            describeCurrent(saved);
                            updateBadges(dialInSession.driverId, saved);
                            showStatus(res.body.pending
                                ? 'Saved: ' + saved + 's. ' + roundMessage()
                                : 'Dial-in saved: ' + saved + 's', res.body.pending ? 'info' : 'ok');
                        })
                        .catch(function () { saveBtn.disabled = false; showStatus('Network error \u2014 try again.', 'err'); });
                });

                logoutBtn.addEventListener('click', function () {
                    dialInSession = null;
                    panel.hidden = true;
                    loginForm.hidden = false;
                    pinInput.value = '';
                    valueInput.value = '';
                    showStatus('', '');
                });
            }

            connect();
        })();
""";

        return
            "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n" +
            "    <meta charset=\"utf-8\" />\n" +
            "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n" +
            "    <title>RC Drag Live</title>\n" +
            "    <style>\n" + css + "    </style>\n" +
            "</head>\n<body>\n" +
            "    <div style=\"max-width:760px;margin:0 auto;padding:12px 16px 0;\">\n" +
            "      <a href=\"/\" style=\"color:#94a3b8;font-size:13px;text-decoration:none;letter-spacing:0.02em;\">&#8592; All events</a>\n" +
            "    </div>\n" +
            "    <div class=\"wrap\">\n\n" +
            content.ToString() +
            "        <div class=\"footer\">Updates live</div>\n" +
            "    </div>\n" +
            "    <script>\n" +
            $"var PAGE_EVENT_ID = {JavaScriptString(eventId)};\n" +
            $"var DIALIN_EVENTS = {JsonSerializer.Serialize(pagePayload, DialInPayloadJsonOptions)};\n" +
            script + "    </script>\n" +
            "</body>\n</html>\n";
    }

    private static string BuildLandingPage(
        IReadOnlyList<EventSummary> events,
        IReadOnlyList<DialInEventPayload> dialInEvents)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"landing\">");
        sb.AppendLine("  <div class=\"brand\">");
        sb.AppendLine("    <h1 class=\"brand-title\">Stew Mac RC</h1>");
        sb.AppendLine("    <p class=\"brand-subtitle\">Live Race Scoreboard</p>");
        sb.AppendLine("  </div>");

        sb.AppendLine("  <div id=\"live-board\">");

        if (events.Count == 0)
        {
            sb.AppendLine("  <div class=\"no-events\">");
            sb.AppendLine("    <p>No active events right now &mdash; check back soon</p>");
            sb.AppendLine("  </div>");
        }
        else
        {
            sb.AppendLine("  <div class=\"event-list\">");
            foreach (var ev in events)
            {
                string name = Html(ev.EventName);
                string date = Html(ev.EventDate);
                string classInfo = ev.ClassCount == 1 ? "1 class" : ev.ClassCount + " classes";
                sb.AppendLine($"    <a class=\"event-card\" href=\"/event/{UrlPathSegment(ev.EventId)}\">");
                sb.AppendLine($"      <div class=\"event-card-name\">{(string.IsNullOrWhiteSpace(name) ? "Unnamed Event" : name)}</div>");
                if (!string.IsNullOrWhiteSpace(date))
                    sb.AppendLine($"      <div class=\"event-card-meta\">{date} &middot; {classInfo}</div>");
                else
                    sb.AppendLine($"      <div class=\"event-card-meta\">{classInfo}</div>");
                sb.AppendLine("    </a>");
            }
            sb.AppendLine("  </div>");
        }

        sb.AppendLine("  </div>");

        sb.Append(BuildLandingDialInSection(dialInEvents));

        sb.AppendLine("  <div class=\"footer\">Updates live</div>");
        sb.AppendLine("</div>");

        var css = """
        * { box-sizing: border-box; }
        body { margin:0; padding:0; font-family:Arial,Helvetica,sans-serif; background:#0a0f1a; color:#f1f5f9; display:flex; justify-content:center; min-height:100vh; }
        .landing { width:100%; max-width:600px; padding:32px 16px; margin:auto; }
        .brand { text-align:center; margin-bottom:32px; }
        .brand-title { font-size:40px; font-weight:900; margin:0 0 8px 0; color:#f8fafc; letter-spacing:-.01em; }
        .brand-subtitle { color:#94a3b8; font-size:16px; margin:0; }
        .no-events { background:#1e293b; border:1px dashed #475569; border-radius:16px; padding:32px 24px; text-align:center; color:#94a3b8; font-size:16px; }
        .event-list { display:grid; gap:14px; }
        .event-card { background:#1e293b; border:1px solid #334155; border-radius:16px; padding:20px 24px; text-decoration:none; color:inherit; display:block; transition:background .15s,border-color .15s; }
        .event-card:hover { background:#263348; border-color:#3b82f6; }
        .event-card-name { font-size:22px; font-weight:800; color:#f8fafc; margin-bottom:6px; }
        .event-card-meta { font-size:14px; color:#94a3b8; }
        .section-title { font-size:13px; font-weight:700; text-transform:uppercase; letter-spacing:.1em; color:#64748b; margin:28px 0 8px 0; }
        .dialin-form { background:#1e293b; border:1px solid #334155; border-radius:16px; padding:20px; }
        .dialin-form h3 { margin:0 0 6px 0; font-size:16px; font-weight:700; color:#f8fafc; }
        .dialin-help { color:#94a3b8; font-size:13px; line-height:1.35; margin:0 0 14px 0; }
        .dialin-form label { display:block; font-size:12px; font-weight:700; text-transform:uppercase; letter-spacing:.08em; color:#94a3b8; margin-bottom:4px; }
        .dialin-form select,.dialin-form input[type=number],.dialin-form input[type=password] { width:100%; background:#0f172a; border:1px solid #475569; border-radius:8px; color:#f1f5f9; font-size:15px; padding:8px 12px; margin-bottom:12px; outline:none; }
        .dialin-form select:focus,.dialin-form input:focus { border-color:#3b82f6; }
        .dialin-form .form-row { display:grid; grid-template-columns:1fr 1fr; gap:12px; }
        .dialin-form button { width:100%; background:#1d4ed8; border:none; border-radius:8px; color:#fff; cursor:pointer; font-size:15px; font-weight:700; padding:10px; transition:background .15s; }
        .dialin-form button:hover:not(:disabled) { background:#2563eb; }
        .dialin-form button:disabled { background:#334155; color:#64748b; cursor:not-allowed; }
        .dialin-status { margin-top:10px; font-size:14px; font-weight:600; min-height:20px; text-align:center; }
        .dialin-status.ok { color:#4ade80; } .dialin-status.err { color:#f87171; } .dialin-status.info { color:#94a3b8; font-style:italic; }
        .dialin-notice { background:#451a03; border:1px solid #92400e; border-radius:12px; color:#fcd34d; font-size:13px; font-weight:600; padding:10px 14px; text-align:center; margin-bottom:12px; }
        .dialin-who { font-size:16px; font-weight:800; color:#f8fafc; margin-bottom:2px; }
        .dialin-current { font-size:13px; color:#94a3b8; margin-bottom:14px; }
        .dialin-current strong { color:#7dd3fc; font-family:'Courier New',Courier,monospace; font-size:15px; }
        .dialin-logout { margin-top:10px; background:transparent !important; border:1px solid #475569 !important; color:#94a3b8 !important; font-size:13px !important; font-weight:600 !important; padding:7px !important; }
        .dialin-logout:hover { border-color:#64748b !important; color:#e2e8f0 !important; }
        .footer { margin-top:28px; text-align:center; color:#475569; font-size:12px; }
        [hidden] { display:none !important; }
        @media(max-width:520px) { .dialin-form .form-row { grid-template-columns:1fr; gap:0; } }
""";

        var script = """
        (function () {
            var eventSelect = document.getElementById('dialin-event');
            var loginForm   = document.getElementById('dialin-login');
            var nameSelect  = document.getElementById('dialin-name');
            var pinInput    = document.getElementById('dialin-pin');
            var panel       = document.getElementById('dialin-panel');
            var whoEl       = document.getElementById('dialin-who');
            var currentEl   = document.getElementById('dialin-current');
            var saveForm    = document.getElementById('dialin-form');
            var valueInput  = document.getElementById('dialin-value');
            var saveBtn     = document.getElementById('dialin-submit');
            var logoutBtn   = document.getElementById('dialin-logout');
            var statusEl    = document.getElementById('dialin-status');
            var noticeEl    = document.getElementById('dialin-notice');

            // Held in memory only, never written to storage. While a driver is
            // logged in the page stops auto-refreshing, so the session survives
            // without a PIN ever being persisted.
            var session = null;

            // The page no longer reloads itself. The server pushes when something
            // actually changes and only the board is swapped, so the scoreboard stays
            // current without ever disturbing whoever is typing in the dial-in card.
            var FALLBACK_MS = 30000;
            var refreshing = false;
            var fallbackTimer = null;

            function applyUpdate(html) {
                var doc = new DOMParser().parseFromString(html, 'text/html');

                var freshBoard = doc.getElementById('live-board');
                var board = document.getElementById('live-board');
                if (freshBoard && board) board.innerHTML = freshBoard.innerHTML;

                var match = /var DIALIN_EVENTS = (\[.*?\]);/.exec(html);
                if (match) {
                    try { refreshRoster(JSON.parse(match[1])); } catch (e) {}
                }
            }

            function refreshRoster(fresh) {
                DIALIN_EVENTS.length = 0;
                Array.prototype.push.apply(DIALIN_EVENTS, fresh);

                if (!DIALIN_EVENTS.length) return;

                if (!session) {
                    // Safe to rebuild: nobody is part-way through anything.
                    var chosen = nameSelect.value;
                    rebuildEventOptions();
                    populateDrivers();
                    if (chosen) nameSelect.value = chosen;
                    return;
                }

                // Signed in. Never touch their typed dial-in; only step in if they
                // have actually gone from the event, which happens when the RD adds
                // a driver and regenerates the bracket.
                var ev = currentEvent();
                var stillEntered = ev && ev.drivers.some(function (d) { return d.id === session.driverId; });
                if (!stillEntered) {
                    leaveSession();
                    rebuildEventOptions();
                    populateDrivers();
                    showStatus('The bracket changed and you are no longer in this event. Please log in again.', 'err');
                } else {
                    refreshNotice();
                }
            }

            function rebuildEventOptions() {
                if (!eventSelect) return;
                var chosen = eventSelect.value;
                eventSelect.innerHTML = '';
                DIALIN_EVENTS.forEach(function (ev, i) {
                    var opt = document.createElement('option');
                    opt.value = String(i);
                    opt.textContent = ev.eventName;
                    eventSelect.appendChild(opt);
                });
                if (chosen && parseInt(chosen, 10) < DIALIN_EVENTS.length) eventSelect.value = chosen;
            }

            function refreshBoard() {
                if (refreshing) return;
                refreshing = true;
                fetch(location.href, { headers: { 'Accept': 'text/html' } })
                    .then(function (r) { return r.text(); })
                    .then(function (html) { applyUpdate(html); })
                    .catch(function () {})
                    .then(function () { refreshing = false; });
            }

            // Fallback for a browser or network where the stream never establishes.
            function startFallback() {
                if (fallbackTimer) return;
                fallbackTimer = setInterval(refreshBoard, FALLBACK_MS);
            }

            function connect() {
                if (typeof EventSource === 'undefined') { startFallback(); return; }
                try {
                    var stream = new EventSource('/stream');
                    stream.addEventListener('changed', refreshBoard);
                    stream.onerror = function () {
                        // EventSource reconnects on its own; the fallback covers the
                        // case where it never manages to.
                        startFallback();
                    };
                } catch (e) {
                    startFallback();
                }
            }

            if (!loginForm || !DIALIN_EVENTS.length) { connect(); return; }

            function currentEvent() {
                var index = eventSelect ? parseInt(eventSelect.value, 10) : 0;
                if (isNaN(index) || index < 0 || index >= DIALIN_EVENTS.length) index = 0;
                return DIALIN_EVENTS[index];
            }

            function showStatus(msg, cls) {
                statusEl.textContent = msg;
                statusEl.className = 'dialin-status ' + (cls || '');
            }

            function roundMessage() {
                return 'A round has already been generated \u2014 your time will not take effect until the next race.';
            }

            function refreshNotice() {
                var ev = currentEvent();
                noticeEl.hidden = !(ev && ev.locked);
                if (ev && ev.locked) noticeEl.textContent = roundMessage();
            }

            function populateDrivers() {
                var ev = currentEvent();
                nameSelect.innerHTML = '';
                var blank = document.createElement('option');
                blank.value = '';
                blank.textContent = '\u2014 select your name \u2014';
                nameSelect.appendChild(blank);
                ev.drivers.forEach(function (d) {
                    var opt = document.createElement('option');
                    opt.value = String(d.id);
                    opt.setAttribute('data-name', d.name);
                    opt.setAttribute('data-dialin', d.dialIn || '');
                    opt.textContent = d.dialIn ? d.name + ' (' + d.dialIn + 's)' : d.name;
                    nameSelect.appendChild(opt);
                });
                refreshNotice();
            }

            function describeCurrent(dialIn) {
                currentEl.innerHTML = dialIn
                    ? 'Your dial-in: <strong>' + dialIn + 's</strong>'
                    : 'You have not set a dial-in yet.';
            }

            function enterSession(ev, driverId, name, pin, dialIn) {
                session = { eventKey: ev.eventKey, driverId: driverId, pin: pin };
                whoEl.textContent = 'Signed in as ' + name;
                describeCurrent(dialIn);
                valueInput.value = dialIn || '';
                loginForm.hidden = true;
                if (eventSelect) eventSelect.disabled = true;
                panel.hidden = false;
                pinInput.value = '';
                valueInput.focus();
            }

            function leaveSession() {
                session = null;
                panel.hidden = true;
                loginForm.hidden = false;
                if (eventSelect) eventSelect.disabled = false;
                pinInput.value = '';
                valueInput.value = '';
                showStatus('', '');
            }

            function post(url, body) {
                return fetch(url, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(body)
                }).then(function (r) {
                    return r.text().then(function (text) {
                        var parsed = {};
                        if (text) { try { parsed = JSON.parse(text); } catch (e) {} }
                        return { ok: r.ok, status: r.status, body: parsed };
                    });
                });
            }

            function describeError(res, fallback) {
                if (res.status === 429) return 'Too many attempts \u2014 wait a few seconds.';
                if (res.body && res.body.error === 'invalid_pin') return 'Incorrect PIN.';
                if (res.body && res.body.error === 'invalid_pin_format') return 'PIN must be exactly 4 digits.';
                if (res.body && res.body.error === 'invalid_driver') return 'You are no longer entered in this event.';
                if (res.body && res.body.error === 'invalid_dialin') return 'Enter a valid dial-in time (e.g. 3.250).';
                return fallback;
            }

            populateDrivers();

            if (eventSelect) {
                eventSelect.addEventListener('change', function () {
                    populateDrivers();
                    showStatus('', '');
                });
            }

            loginForm.addEventListener('submit', function (e) {
                e.preventDefault();
                var ev = currentEvent();
                var driverId = parseInt(nameSelect.value, 10);
                var pin = pinInput.value.trim();
                var option = nameSelect.options[nameSelect.selectedIndex];
                var name = option ? (option.getAttribute('data-name') || option.textContent) : '';
                if (!driverId || driverId <= 0) { showStatus('Please select your name.', 'err'); return; }
                if (!/^[0-9]{4}$/.test(pin)) { showStatus('Enter your 4-digit PIN.', 'err'); return; }

                showStatus('Checking\u2026', 'info');
                post('/api/dialin/login', { eventId: ev.eventKey, driverId: driverId, pin: pin })
                    .then(function (res) {
                        if (!res.ok) { showStatus(describeError(res, 'Could not log in.'), 'err'); return; }
                        var dialIn = (res.body.dialIn === null || res.body.dialIn === undefined)
                            ? (option ? option.getAttribute('data-dialin') : '')
                            : Number(res.body.dialIn).toFixed(3);
                        enterSession(ev, driverId, name, pin, dialIn);
                        showStatus('', '');
                    })
                    .catch(function () { showStatus('Network error \u2014 try again.', 'err'); });
            });

            saveForm.addEventListener('submit', function (e) {
                e.preventDefault();
                if (!session) return;
                var val = parseFloat(valueInput.value);
                if (isNaN(val) || val <= 0) { showStatus('Enter a valid dial-in time (e.g. 3.250).', 'err'); return; }
                saveBtn.disabled = true;
                showStatus('Saving\u2026', 'info');
                post('/api/dialin', {
                    eventId: session.eventKey,
                    driverId: session.driverId,
                    dialIn: val,
                    pin: session.pin
                })
                    .then(function (res) {
                        saveBtn.disabled = false;
                        if (!res.ok) { showStatus(describeError(res, 'Error saving dial-in.'), 'err'); return; }
                        var saved = val.toFixed(3);
                        describeCurrent(saved);
                        currentEvent().drivers.forEach(function (d) {
                            if (d.id === session.driverId) d.dialIn = saved;
                        });
                        showStatus(res.body.pending
                            ? 'Saved: ' + saved + 's. ' + roundMessage()
                            : 'Dial-in saved: ' + saved + 's', res.body.pending ? 'info' : 'ok');
                    })
                    .catch(function () { saveBtn.disabled = false; showStatus('Network error \u2014 try again.', 'err'); });
            });

            logoutBtn.addEventListener('click', leaveSession);

            connect();
        })();
""";

        return
            "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n" +
            "    <meta charset=\"utf-8\" />\n" +
            "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n" +
            "    <title>Stew Mac RC &mdash; Live Race Scoreboard</title>\n" +
            "    <style>\n" + css + "    </style>\n" +
            "</head>\n<body>\n" +
            sb.ToString() +
            "    <script>\n" +
            $"var DIALIN_EVENTS = {JsonSerializer.Serialize(dialInEvents, DialInPayloadJsonOptions)};\n" +
            script +
            "    </script>\n" +
            "</body>\n</html>\n";
    }

    private static string BuildLandingDialInSection(IReadOnlyList<DialInEventPayload> dialInEvents)
    {
        if (dialInEvents.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("  <h2 class=\"section-title\">Your Dial-In</h2>");
        sb.AppendLine("  <div class=\"dialin-form\" id=\"dialin-card\">");
        sb.AppendLine("    <h3>Driver Login</h3>");
        sb.AppendLine("    <p class=\"dialin-help\">Pick your name and enter a 4-digit PIN. Your PIN is set the first time you log in and is needed to change your time later.</p>");

        // The picker sits outside the login form so it stays usable while the
        // driver is signed in to a different event.
        sb.AppendLine(dialInEvents.Count > 1 ? "    <div>" : "    <div hidden>");
        sb.AppendLine("      <label for=\"dialin-event\">Event</label>");
        sb.AppendLine("      <select id=\"dialin-event\">");
        for (int i = 0; i < dialInEvents.Count; i++)
        {
            sb.AppendLine($"        <option value=\"{i}\">{Html(dialInEvents[i].EventName)}</option>");
        }
        sb.AppendLine("      </select>");
        sb.AppendLine("    </div>");

        sb.AppendLine("    <div class=\"dialin-notice\" id=\"dialin-notice\" hidden></div>");

        sb.AppendLine("    <form id=\"dialin-login\">");
        sb.AppendLine("      <label for=\"dialin-name\">Your Name</label>");
        sb.AppendLine("      <select id=\"dialin-name\" required></select>");
        sb.AppendLine("      <label for=\"dialin-pin\">PIN (4 digits)</label>");
        sb.AppendLine("      <input type=\"password\" id=\"dialin-pin\" maxlength=\"4\" inputmode=\"numeric\" pattern=\"[0-9]{4}\" autocomplete=\"off\" required placeholder=\"4-digit PIN\" />");
        sb.AppendLine("      <button id=\"dialin-login-submit\" type=\"submit\">Log in</button>");
        sb.AppendLine("    </form>");

        sb.AppendLine("    <div id=\"dialin-panel\" hidden>");
        sb.AppendLine("      <div class=\"dialin-who\" id=\"dialin-who\"></div>");
        sb.AppendLine("      <div class=\"dialin-current\" id=\"dialin-current\"></div>");
        sb.AppendLine("      <form id=\"dialin-form\">");
        sb.AppendLine("        <label for=\"dialin-value\">Dial-In (seconds)</label>");
        sb.AppendLine("        <input type=\"number\" id=\"dialin-value\" step=\"0.001\" min=\"0.001\" inputmode=\"decimal\" autocomplete=\"off\" required placeholder=\"e.g. 3.250\" />");
        sb.AppendLine("        <button id=\"dialin-submit\" type=\"submit\">Save Dial-In</button>");
        sb.AppendLine("      </form>");
        sb.AppendLine("      <button class=\"dialin-logout\" id=\"dialin-logout\" type=\"button\">Log out</button>");
        sb.AppendLine("    </div>");

        sb.AppendLine("    <div class=\"dialin-status\" id=\"dialin-status\" role=\"status\" aria-live=\"polite\"></div>");
        sb.AppendLine("  </div>");

        return sb.ToString();
    }


    private static string BuildNoEventPage()
    {
        return """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>RC Drag Live</title>
    <style>
        * { box-sizing: border-box; }
        body {
            margin: 0;
            padding: 0;
            font-family: Arial, Helvetica, sans-serif;
            background: #0a0f1a;
            color: #f1f5f9;
            display: flex;
            align-items: center;
            justify-content: center;
            min-height: 100vh;
        }
        .no-event {
            background: #1e293b;
            border: 1px dashed #475569;
            border-radius: 16px;
            padding: 40px 48px;
            text-align: center;
            max-width: 400px;
        }
        .no-event h1 { font-size: 24px; font-weight: 900; margin: 0 0 10px 0; color: #f8fafc; }
        .no-event p { color: #94a3b8; margin: 0; font-size: 15px; }
        .footer { margin-top: 24px; color: #475569; font-size: 12px; }
    </style>
</head>
<body>
    <div class="no-event">
        <h1>No Active Event</h1>
        <p>Waiting for race data to arrive.</p>
        <div class="footer">Updates live</div>
    </div>
    <script>
        setTimeout(function () { location.reload(); }, 5000);
    </script>
</body>
</html>
""";
    }

    private static string BuildClassPanel(LiveRaceState state, IReadOnlyDictionary<int, double?> submittedDialIns)
    {
        string eventName = Html(state.EventName);
        string eventDate = Html(state.EventDate);
        string classType = Html(state.ClassType);
        string raceType  = Html(state.RaceType);
        string nextUp    = Html(state.NextUp);

        string nextUpHtml = string.IsNullOrWhiteSpace(nextUp)
            ? "<div class=\"empty-box\">Waiting for next match up...</div>"
            : $"<div class=\"next-up-drivers\">{nextUp}</div>";

        StringBuilder bracketHtml = new StringBuilder();
        if (state.Matches.Count == 0)
        {
            bracketHtml.AppendLine("<div class=\"empty-box\">No bracket data available yet.</div>");
        }
        else
        {
            var rounds = state.Matches
                .GroupBy(m => m.RoundLabel)
                .OrderBy(g => RoundSortKey(g.Key));

            foreach (var round in rounds)
            {
                string roundLabel = Html(RoundDisplayName(round.Key));
                bracketHtml.AppendLine($"  <div class=\"round-header\">{(string.IsNullOrWhiteSpace(roundLabel) ? "Round" : roundLabel)}</div>");
                bracketHtml.AppendLine("  <div class=\"match-list\">");

                foreach (LiveMatch match in round)
                {
                    string leftDriver  = Html(string.IsNullOrEmpty(match.LeftDriver)  ? match.Driver1 : match.LeftDriver);
                    string rightDriver = Html(string.IsNullOrEmpty(match.RightDriver) ? match.Driver2 : match.RightDriver);
                    double? leftDialIn = EffectiveDialIn(match.LeftDriverId, match.LeftDriverDialIn, submittedDialIns);
                    double? rightDialIn = EffectiveDialIn(match.RightDriverId, match.RightDriverDialIn, submittedDialIns);
                    bool resolved  = !string.IsNullOrWhiteSpace(match.WinnerName);

                    bracketHtml.AppendLine("    <div class=\"match-card\">");

                    if (resolved)
                    {
                        bool leftWon = string.Equals(match.WinnerName, string.IsNullOrEmpty(match.LeftDriver) ? match.Driver1 : match.LeftDriver, StringComparison.OrdinalIgnoreCase);
                        string leftClass  = leftWon  ? "winner" : "loser";
                        string rightClass = !leftWon ? "winner" : "loser";
                        string leftBadge  = leftWon  ? " <span class=\"win-badge\">WIN</span>" : string.Empty;
                        string rightBadge = !leftWon ? " <span class=\"win-badge\">WIN</span>" : string.Empty;
                        bracketHtml.AppendLine("      <div class=\"lane-slot\">");
                        bracketHtml.AppendLine("        <div class=\"lane-label\">Left</div>");
                        bracketHtml.AppendLine($"        <div class=\"driver {leftClass}\" data-driver-id=\"{match.LeftDriverId}\">{leftDriver}{DialInBadge(leftDialIn)}{leftBadge}</div>");
                        bracketHtml.AppendLine("      </div>");
                        bracketHtml.AppendLine("      <div class=\"vs\">vs</div>");
                        bracketHtml.AppendLine("      <div class=\"lane-slot\">");
                        bracketHtml.AppendLine("        <div class=\"lane-label\">Right</div>");
                        bracketHtml.AppendLine($"        <div class=\"driver {rightClass}\" data-driver-id=\"{match.RightDriverId}\">{rightDriver}{DialInBadge(rightDialIn)}{rightBadge}</div>");
                        bracketHtml.AppendLine("      </div>");
                    }
                    else
                    {
                        bracketHtml.AppendLine("      <div class=\"lane-slot\">");
                        bracketHtml.AppendLine("        <div class=\"lane-label\">Left</div>");
                        bracketHtml.AppendLine($"        <div class=\"driver\" data-driver-id=\"{match.LeftDriverId}\">{leftDriver}{DialInBadge(leftDialIn)}</div>");
                        bracketHtml.AppendLine("      </div>");
                        bracketHtml.AppendLine("      <div class=\"vs\">vs</div>");
                        bracketHtml.AppendLine("      <div class=\"lane-slot\">");
                        bracketHtml.AppendLine("        <div class=\"lane-label\">Right</div>");
                        bracketHtml.AppendLine($"        <div class=\"driver\" data-driver-id=\"{match.RightDriverId}\">{rightDriver}{DialInBadge(rightDialIn)}</div>");
                        bracketHtml.AppendLine("      </div>");
                    }

                    bracketHtml.AppendLine("    </div>");
                }

                bracketHtml.AppendLine("  </div>");
            }
        }

        string winnersHtml;
        if (state.Winners.Count == 0)
        {
            winnersHtml = "<div class=\"empty-box\">No winners recorded yet.</div>";
        }
        else
        {
            StringBuilder wb = new StringBuilder();
            wb.AppendLine("<table class=\"winners-table\">");
            wb.AppendLine("  <thead><tr><th>Round</th><th>Winner</th><th>Loser</th></tr></thead>");
            wb.AppendLine("  <tbody>");
            foreach (LiveWinner w in state.Winners)
            {
                wb.AppendLine($"    <tr><td>{Html(RoundDisplayName(w.RoundLabel))}</td><td class=\"winner-cell\">{Html(w.WinnerName)}</td><td class=\"loser-cell\">{Html(w.LoserName)}</td></tr>");
            }
            wb.AppendLine("  </tbody>");
            wb.AppendLine("</table>");
            winnersHtml = wb.ToString();
        }

        string rrHtml = string.IsNullOrWhiteSpace(state.RRStandings)
            ? string.Empty
            : $"\n            <h2 class=\"section-title\">Round Robin Standings</h2>\n            <pre class=\"rr-standings\">{Html(state.RRStandings)}</pre>\n            ";

        string classTypeBadge = string.IsNullOrWhiteSpace(classType) ? string.Empty : $"<span class=\"badge\">{classType}</span>";
        string raceTypeBadge  = string.IsNullOrWhiteSpace(raceType)  ? string.Empty : $"<span class=\"badge badge-race\">{raceType}</span>";

        return
            "        <!-- Event Header -->\n" +
            "        <div class=\"hero\">\n" +
            $"            <h1 class=\"title\">{(string.IsNullOrWhiteSpace(eventName) ? "RC Drag Live" : eventName)}</h1>\n" +
            $"            <p class=\"event-meta\">{(string.IsNullOrWhiteSpace(eventDate) ? "Waiting for event date" : eventDate)}</p>\n" +
            $"            {classTypeBadge}{raceTypeBadge}\n" +
            "        </div>\n\n" +
            "        <!-- Next Up -->\n" +
            "        <h2 class=\"section-title\">Next Up</h2>\n" +
            $"        <div class=\"panel\">\n            {nextUpHtml}\n        </div>\n\n" +
            "        <!-- Full Bracket -->\n" +
            "        <h2 class=\"section-title\">Full Bracket</h2>\n" +
            bracketHtml.ToString() +
            "        <!-- Winners List -->\n" +
            "        <h2 class=\"section-title\">Winners</h2>\n" +
            $"        <div class=\"panel\">\n            {winnersHtml}\n        </div>\n" +
            rrHtml + "\n";
    }

    private static string BuildDialInForm(List<(int Id, string Name, double? DialIn)> drivers, bool locked)
    {
        if (drivers.Count == 0) return string.Empty;

        // A generated round no longer hides this form. The driver can always save;
        // the notice tells them it lands in the next race.
        return
            "        <!-- Dial-In -->\n" +
            "        <h2 class=\"section-title\">Your Dial-In</h2>\n" +
            "        <div class=\"dialin-form\" id=\"dialin-card\">\n" +
            "            <h3>Driver Login</h3>\n" +
            "            <p class=\"dialin-help\">Pick your name and enter a 4-digit PIN. Your PIN is set the first time you log in and is needed to change your time later.</p>\n" +
            "            <div class=\"dialin-notice\" id=\"dialin-notice\" hidden></div>\n" +
            "            <form id=\"dialin-login\">\n" +
            "                <label for=\"dialin-name\">Your Name</label>\n" +
            "                <select id=\"dialin-name\" required></select>\n" +
            "                <label for=\"dialin-pin\">PIN (4 digits)</label>\n" +
            "                <input type=\"password\" id=\"dialin-pin\" maxlength=\"4\" inputmode=\"numeric\" pattern=\"[0-9]{4}\" autocomplete=\"off\" required placeholder=\"4-digit PIN\" />\n" +
            "                <button id=\"dialin-login-submit\" type=\"submit\">Log in</button>\n" +
            "            </form>\n" +
            "            <div id=\"dialin-panel\" hidden>\n" +
            "                <div class=\"dialin-who\" id=\"dialin-who\"></div>\n" +
            "                <div class=\"dialin-current\" id=\"dialin-current\"></div>\n" +
            "                <form id=\"dialin-form\">\n" +
            "                    <label for=\"dialin-value\">Dial-In (seconds)</label>\n" +
            "                    <input type=\"number\" id=\"dialin-value\" step=\"0.001\" min=\"0.001\" inputmode=\"decimal\" autocomplete=\"off\" required placeholder=\"e.g. 3.250\" />\n" +
            "                    <button id=\"dialin-submit\" type=\"submit\">Save Dial-In</button>\n" +
            "                </form>\n" +
            "                <button class=\"dialin-logout\" id=\"dialin-logout\" type=\"button\">Log out</button>\n" +
            "            </div>\n" +
            "            <div class=\"dialin-status\" id=\"dialin-status\" role=\"status\" aria-live=\"polite\"></div>\n" +
            "        </div>\n";
    }

    private static int RoundSortKey(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return 10000;
        var mRr = System.Text.RegularExpressions.Regex.Match(label, @"^RR(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (mRr.Success && int.TryParse(mRr.Groups[1].Value, out int rrN)) return 100 + rrN;
        var mR = System.Text.RegularExpressions.Regex.Match(label, @"^R(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (mR.Success && int.TryParse(mR.Groups[1].Value, out int rN)) return 200 + rN;
        var mLb = System.Text.RegularExpressions.Regex.Match(label, @"^LB-R(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (mLb.Success && int.TryParse(mLb.Groups[1].Value, out int lbN)) return 300 + lbN;
        if (string.Equals(label, "LB-SF", StringComparison.OrdinalIgnoreCase)) return 390;
        if (string.Equals(label, "LB-F",  StringComparison.OrdinalIgnoreCase)) return 399;
        if (string.Equals(label, "SF",    StringComparison.OrdinalIgnoreCase)) return 490;
        if (string.Equals(label, "F",     StringComparison.OrdinalIgnoreCase)) return 499;
        return 10000;
    }

    private static string RoundDisplayName(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return label ?? string.Empty;
        var mRr = System.Text.RegularExpressions.Regex.Match(label, @"^RR(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (mRr.Success) return "Round " + mRr.Groups[1].Value;
        var mR = System.Text.RegularExpressions.Regex.Match(label, @"^R(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (mR.Success) return "Round " + mR.Groups[1].Value;
        var mLb = System.Text.RegularExpressions.Regex.Match(label, @"^LB-R(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (mLb.Success) return "Buyback Round " + mLb.Groups[1].Value;
        if (string.Equals(label, "LB-SF", StringComparison.OrdinalIgnoreCase)) return "Buyback Semi Final";
        if (string.Equals(label, "LB-F",  StringComparison.OrdinalIgnoreCase)) return "Buyback Final";
        if (string.Equals(label, "SF",    StringComparison.OrdinalIgnoreCase)) return "Semi Final";
        if (string.Equals(label, "F",     StringComparison.OrdinalIgnoreCase)) return "Final";
        return label;
    }

    private static string DialInBadge(double? dialIn)
    {
        if (dialIn == null) return string.Empty;
        return $"<span class=\"dial-in-badge\">{dialIn.Value:F3}s</span>";
    }

    private static double? EffectiveDialIn(int driverId, double? liveDialIn, IReadOnlyDictionary<int, double?> submittedDialIns)
    {
        if (driverId > 0 && submittedDialIns.TryGetValue(driverId, out var submittedDialIn))
            return submittedDialIn;

        return liveDialIn;
    }

    private static string Html(string? value)
    {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }

    private static string UrlPathSegment(string? value)
    {
        return Uri.EscapeDataString(value ?? string.Empty);
    }

    private static string JavaScriptString(string? value)
    {
        return JsonSerializer.Serialize(value ?? string.Empty);
    }

    private static string FormatDialIn(double? dialIn)
    {
        return dialIn.HasValue
            ? dialIn.Value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
    }

    // The default encoder escapes <, > and & as \uXXXX, so the result stays safe
    // to inline inside a <script> block.
    private static readonly JsonSerializerOptions DialInPayloadJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class DialInEventPayload
    {
        public string EventKey { get; init; } = string.Empty;
        public string EventName { get; init; } = string.Empty;
        public bool Locked { get; init; }
        public List<DialInDriverPayload> Drivers { get; init; } = new();
    }

    private sealed class DialInDriverPayload
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string DialIn { get; init; } = string.Empty;
    }
}
