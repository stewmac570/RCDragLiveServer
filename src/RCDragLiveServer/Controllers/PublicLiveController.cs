using System.Net;
using System.Text;
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

    public PublicLiveController(ILiveRaceStateStore stateStore, IDialInStore dialInStore)
    {
        this.stateStore = stateStore;
        this.dialInStore = dialInStore;
    }

    [HttpGet("")]
    public ContentResult Home()
    {
        ApplyNoCacheHeaders();

        var events = stateStore.GetActiveEvents();

        if (events.Count == 0)
            return Content(BuildNoEventPage(), "text/html; charset=utf-8");

        if (events.Count == 1)
        {
            var classes = stateStore.GetEvent(events[0].EventId) ?? new Dictionary<string, LiveRaceState>();
            return Content(BuildHomePage(classes, dialInStore.IsLocked), "text/html; charset=utf-8");
        }

        return Content(BuildEventSelectorPage(events), "text/html; charset=utf-8");
    }

    [HttpGet("event/{eventId}")]
    public ContentResult GetEventPage(string eventId)
    {
        ApplyNoCacheHeaders();

        var classes = stateStore.GetEvent(eventId);

        if (classes == null || classes.Count == 0)
            return Content(BuildNoEventPage(), "text/html; charset=utf-8");

        return Content(BuildHomePage(classes, dialInStore.IsLocked), "text/html; charset=utf-8");
    }

    [HttpGet("api/live")]
    public ActionResult<IEnumerable<LiveRaceState>> GetLive()
    {
        ApplyNoCacheHeaders();

        return Ok(stateStore.GetAll().Values.ToList());
    }

    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        ApplyNoCacheHeaders();

        return Ok(new { status = "healthy" });
    }

    private void ApplyNoCacheHeaders()
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
    }

    private static string BuildHomePage(Dictionary<string, LiveRaceState> classes, bool dialInLocked)
    {
        if (classes.Count == 0)
        {
            return BuildNoEventPage();
        }

        var sortedKeys = classes.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        bool multiClass = sortedKeys.Count > 1;

        var allDriverNames = classes.Values
            .SelectMany(s => s.Matches)
            .SelectMany(m => new[] { m.Driver1, m.Driver2 })
            .Where(n => !string.IsNullOrWhiteSpace(n) && !string.Equals(n, "BYE", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        StringBuilder content = new StringBuilder();

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
                content.Append(BuildClassPanel(classes[sortedKeys[i]]));
                content.AppendLine("</div>");
            }
        }
        else
        {
            content.Append(BuildClassPanel(classes[sortedKeys[0]]));
        }

        content.Append(BuildDialInForm(allDriverNames, dialInLocked));

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
        .match-card { background:#1e293b; border:1px solid #334155; border-radius:12px; padding:14px 16px; text-align:center; }
        .driver { font-size:22px; font-weight:800; line-height:1.2; word-break:break-word; }
        .driver.winner { color:#4ade80; }
        .driver.loser { color:#64748b; font-size:17px; font-weight:600; text-decoration:line-through; margin-top:4px; }
        .dial-in-badge { display:inline-block; background:#0f2d4a; color:#7dd3fc; font-size:12px; font-weight:700; letter-spacing:.04em; padding:2px 8px; border-radius:999px; vertical-align:middle; margin-left:8px; font-family:'Courier New',Courier,monospace; }
        .win-badge { display:inline-block; background:#14532d; color:#86efac; font-size:11px; font-weight:700; letter-spacing:.08em; padding:2px 7px; border-radius:999px; vertical-align:middle; margin-left:6px; }
        .vs { font-size:12px; font-weight:700; color:#64748b; margin:8px 0; text-transform:uppercase; letter-spacing:.1em; }
        .winners-table { width:100%; border-collapse:collapse; font-size:15px; }
        .winners-table th { text-align:left; font-size:11px; font-weight:700; text-transform:uppercase; letter-spacing:.08em; color:#64748b; padding:6px 10px; border-bottom:1px solid #334155; }
        .winners-table td { padding:8px 10px; border-bottom:1px solid #1e293b; vertical-align:middle; }
        .winners-table tr:last-child td { border-bottom:none; }
        .winner-cell { font-weight:700; color:#4ade80; }
        .loser-cell { color:#64748b; }
        .rr-standings { background:#0f172a; border:1px solid #334155; border-radius:12px; padding:14px 16px; font-family:'Courier New',Courier,monospace; font-size:13px; color:#e2e8f0; overflow-x:auto; white-space:pre; line-height:1.5; }
        .dialin-form { background:#1e293b; border:1px solid #334155; border-radius:16px; padding:20px; margin-bottom:16px; }
        .dialin-form h3 { margin:0 0 14px 0; font-size:16px; font-weight:700; color:#f8fafc; }
        .dialin-form label { display:block; font-size:12px; font-weight:700; text-transform:uppercase; letter-spacing:.08em; color:#94a3b8; margin-bottom:4px; }
        .dialin-form select,.dialin-form input[type=number],.dialin-form input[type=password] { width:100%; background:#0f172a; border:1px solid #475569; border-radius:8px; color:#f1f5f9; font-size:15px; padding:8px 12px; margin-bottom:12px; outline:none; }
        .dialin-form select:focus,.dialin-form input:focus { border-color:#3b82f6; }
        .dialin-form .form-row { display:grid; grid-template-columns:1fr 1fr; gap:12px; }
        .dialin-form button { width:100%; background:#1d4ed8; border:none; border-radius:8px; color:#fff; cursor:pointer; font-size:15px; font-weight:700; padding:10px; transition:background .15s; }
        .dialin-form button:hover:not(:disabled) { background:#2563eb; }
        .dialin-form button:disabled { background:#334155; color:#64748b; cursor:not-allowed; }
        .dialin-status { margin-top:10px; font-size:14px; font-weight:600; min-height:20px; text-align:center; }
        .dialin-status.ok { color:#4ade80; } .dialin-status.err { color:#f87171; } .dialin-status.info { color:#94a3b8; font-style:italic; }
        .dialin-locked-notice { background:#451a03; border:1px solid #92400e; border-radius:12px; color:#fcd34d; font-size:14px; font-weight:600; padding:12px 16px; text-align:center; }
        .footer { margin-top:20px; text-align:center; color:#475569; font-size:12px; }
        @media(min-width:640px) { .match-list { grid-template-columns:1fr 1fr; } }
""";

        var script = """
        (function () {
            var STORAGE_KEY = 'rcDragActiveClass';
            var DIALIN_KEY = 'rcDialInForm';
            var CYCLE_MS = 8000;
            var buttons = Array.from(document.querySelectorAll('.tab-btn'));
            var panels = Array.from(document.querySelectorAll('.tab-panel'));
            var count = buttons.length;
            var cycleTimer = null;

            function saveDialInState() {
                try {
                    var nameEl = document.getElementById('dialin-name');
                    var valEl = document.getElementById('dialin-value');
                    var pinEl = document.getElementById('dialin-pin');
                    sessionStorage.setItem(DIALIN_KEY, JSON.stringify({
                        name: nameEl ? nameEl.value : '',
                        val: valEl ? valEl.value : '',
                        pin: pinEl ? pinEl.value : ''
                    }));
                } catch (e) {}
            }

            function restoreDialInState() {
                try {
                    var raw = sessionStorage.getItem(DIALIN_KEY);
                    if (!raw) return;
                    var s = JSON.parse(raw);
                    var nameEl = document.getElementById('dialin-name');
                    var valEl = document.getElementById('dialin-value');
                    var pinEl = document.getElementById('dialin-pin');
                    if (nameEl && s.name) nameEl.value = s.name;
                    if (valEl && s.val) valEl.value = s.val;
                    if (pinEl && s.pin) pinEl.value = s.pin;
                } catch (e) {}
            }

            function clearDialInState() {
                try { sessionStorage.removeItem(DIALIN_KEY); } catch (e) {}
            }

            function isDialInFocused() {
                var f = document.activeElement;
                return f && (f.id === 'dialin-name' || f.id === 'dialin-value' || f.id === 'dialin-pin');
            }

            function scheduleReload() {
                if (isDialInFocused()) { setTimeout(scheduleReload, 3000); return; }
                saveDialInState();
                location.reload();
            }

            if (count === 0) { setTimeout(scheduleReload, 5000); return; }

            function activate(index) {
                buttons.forEach(function (b, i) { b.classList.toggle('active', i === index); });
                panels.forEach(function (p, i) { p.classList.toggle('active', i === index); });
            }
            function startCycle(fromIndex) {
                if (cycleTimer) clearInterval(cycleTimer);
                var current = fromIndex;
                cycleTimer = setInterval(function () { current = (current + 1) % count; activate(current); }, CYCLE_MS);
            }

            var storedClass = null;
            try { storedClass = localStorage.getItem(STORAGE_KEY); } catch (e) {}
            var activeIndex = 0;
            var userHasSelection = false;
            if (storedClass) {
                var found = -1;
                buttons.forEach(function (btn, i) { if (btn.textContent.trim() === storedClass) { found = i; } });
                if (found >= 0) { activeIndex = found; userHasSelection = true; }
                else { try { localStorage.removeItem(STORAGE_KEY); } catch (e) {} }
            }
            activate(activeIndex);
            if (!userHasSelection) startCycle(activeIndex);
            buttons.forEach(function (btn, i) {
                btn.addEventListener('click', function () {
                    activate(i);
                    try { localStorage.setItem(STORAGE_KEY, btn.textContent.trim()); } catch (e) {}
                    if (cycleTimer) { clearInterval(cycleTimer); cycleTimer = null; }
                });
            });

            setTimeout(scheduleReload, 5000);

            var form = document.getElementById('dialin-form');
            if (form) {
                restoreDialInState();
                var nameSelect = document.getElementById('dialin-name');
                var dialInInput = document.getElementById('dialin-value');
                var pinInput = document.getElementById('dialin-pin');
                var submitBtn = document.getElementById('dialin-submit');
                var statusEl = document.getElementById('dialin-status');
                submitBtn.addEventListener('click', function () {
                    var name = nameSelect ? nameSelect.value : '';
                    var val = parseFloat(dialInInput.value);
                    var pin = pinInput.value.trim() || null;
                    if (!name) { showStatus('Please select your name.', 'err'); return; }
                    if (isNaN(val) || val <= 0) { showStatus('Enter a valid dial-in time (e.g. 3.250).', 'err'); return; }
                    submitBtn.disabled = true;
                    showStatus('Saving…', 'info');
                    fetch('/api/dialin', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ driverName: name, dialIn: val, pin: pin })
                    })
                    .then(function (r) { return r.json().then(function (j) { return { ok: r.ok, status: r.status, body: j }; }); })
                    .then(function (res) {
                        submitBtn.disabled = false;
                        if (res.ok) { clearDialInState(); showStatus('Dial-in saved: ' + val.toFixed(3) + 's', 'ok'); }
                        else if (res.status === 423) { showStatus('Round in progress — dial-in locked.', 'err'); }
                        else if (res.body && res.body.error === 'invalid_pin') { showStatus('Incorrect PIN.', 'err'); }
                        else if (res.body && res.body.error === 'invalid_pin_format') { showStatus('PIN must be exactly 4 digits.', 'err'); }
                        else { showStatus('Error saving dial-in.', 'err'); }
                    })
                    .catch(function () { submitBtn.disabled = false; showStatus('Network error — try again.', 'err'); });
                });
                function showStatus(msg, cls) { statusEl.textContent = msg; statusEl.className = 'dialin-status ' + cls; }
            }
        })();
""";

        return
            "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n" +
            "    <meta charset=\"utf-8\" />\n" +
            "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n" +
            "    <title>RC Drag Live</title>\n" +
            "    <style>\n" + css + "    </style>\n" +
            "</head>\n<body>\n    <div class=\"wrap\">\n\n" +
            content.ToString() +
            "        <div class=\"footer\">Auto-refreshes every 5 seconds</div>\n" +
            "    </div>\n" +
            "    <script>\n" + script + "    </script>\n" +
            "</body>\n</html>\n";
    }

    private static string BuildEventSelectorPage(IReadOnlyList<EventSummary> events)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"event-selector\">");
        sb.AppendLine("  <h1 class=\"selector-title\">Live Events</h1>");
        sb.AppendLine("  <p class=\"selector-subtitle\">Select an event to view its live scoreboard</p>");
        sb.AppendLine("  <div class=\"event-list\">");

        foreach (var ev in events)
        {
            string name = Html(ev.EventName);
            string date = Html(ev.EventDate);
            string classInfo = ev.ClassCount == 1 ? "1 class" : ev.ClassCount + " classes";
            sb.AppendLine($"    <a class=\"event-card\" href=\"/event/{Html(ev.EventId)}\">");
            sb.AppendLine($"      <div class=\"event-card-name\">{(string.IsNullOrWhiteSpace(name) ? "Unnamed Event" : name)}</div>");
            if (!string.IsNullOrWhiteSpace(date))
                sb.AppendLine($"      <div class=\"event-card-meta\">{date} &middot; {classInfo}</div>");
            else
                sb.AppendLine($"      <div class=\"event-card-meta\">{classInfo}</div>");
            sb.AppendLine("    </a>");
        }

        sb.AppendLine("  </div>");
        sb.AppendLine("  <div class=\"footer\">Auto-refreshes every 5 seconds</div>");
        sb.AppendLine("</div>");

        var css = """
        * { box-sizing: border-box; }
        body { margin:0; padding:0; font-family:Arial,Helvetica,sans-serif; background:#0a0f1a; color:#f1f5f9; display:flex; align-items:center; justify-content:center; min-height:100vh; }
        .event-selector { width:100%; max-width:600px; padding:32px 16px; }
        .selector-title { font-size:32px; font-weight:900; margin:0 0 8px 0; color:#f8fafc; text-align:center; }
        .selector-subtitle { color:#94a3b8; font-size:15px; text-align:center; margin:0 0 28px 0; }
        .event-list { display:grid; gap:14px; }
        .event-card { background:#1e293b; border:1px solid #334155; border-radius:16px; padding:20px 24px; text-decoration:none; color:inherit; display:block; transition:background .15s,border-color .15s; }
        .event-card:hover { background:#263348; border-color:#3b82f6; }
        .event-card-name { font-size:22px; font-weight:800; color:#f8fafc; margin-bottom:6px; }
        .event-card-meta { font-size:14px; color:#94a3b8; }
        .footer { margin-top:28px; text-align:center; color:#475569; font-size:12px; }
""";

        return
            "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n" +
            "    <meta charset=\"utf-8\" />\n" +
            "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n" +
            "    <title>RC Drag Live — Select Event</title>\n" +
            "    <style>\n" + css + "    </style>\n" +
            "</head>\n<body>\n" +
            sb.ToString() +
            "    <script>setTimeout(function () { location.reload(); }, 5000);</script>\n" +
            "</body>\n</html>\n";
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
        <div class="footer">Auto-refreshes every 5 seconds</div>
    </div>
    <script>
        setTimeout(function () { location.reload(); }, 5000);
    </script>
</body>
</html>
""";
    }

    private static string BuildClassPanel(LiveRaceState state)
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
                .OrderBy(g => g.Key);

            foreach (var round in rounds)
            {
                string roundLabel = Html(round.Key);
                bracketHtml.AppendLine($"  <div class=\"round-header\">{(string.IsNullOrWhiteSpace(roundLabel) ? "Round" : roundLabel)}</div>");
                bracketHtml.AppendLine("  <div class=\"match-list\">");

                foreach (LiveMatch match in round)
                {
                    string driver1 = Html(match.Driver1);
                    string driver2 = Html(match.Driver2);
                    bool resolved  = !string.IsNullOrWhiteSpace(match.WinnerName);

                    bracketHtml.AppendLine("    <div class=\"match-card\">");

                    if (resolved)
                    {
                        bool d1Won       = string.Equals(match.WinnerName, match.Driver1, StringComparison.OrdinalIgnoreCase);
                        string winName   = Html(match.WinnerName);
                        string loseName  = d1Won ? driver2 : driver1;
                        double? winDialIn = d1Won ? match.LeftDriverDialIn : match.RightDriverDialIn;
                        double? losDialIn = d1Won ? match.RightDriverDialIn : match.LeftDriverDialIn;
                        bracketHtml.AppendLine($"      <div class=\"driver winner\">{winName}{DialInBadge(winDialIn)} <span class=\"win-badge\">WIN</span></div>");
                        bracketHtml.AppendLine($"      <div class=\"driver loser\">{loseName}{DialInBadge(losDialIn)}</div>");
                    }
                    else
                    {
                        bracketHtml.AppendLine($"      <div class=\"driver driver1\">{driver1}{DialInBadge(match.LeftDriverDialIn)}</div>");
                        bracketHtml.AppendLine("      <div class=\"vs\">vs</div>");
                        bracketHtml.AppendLine($"      <div class=\"driver driver2\">{driver2}{DialInBadge(match.RightDriverDialIn)}</div>");
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
                wb.AppendLine($"    <tr><td>{Html(w.RoundLabel)}</td><td class=\"winner-cell\">{Html(w.WinnerName)}</td><td class=\"loser-cell\">{Html(w.LoserName)}</td></tr>");
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

    private static string BuildDialInForm(List<string> driverNames, bool locked)
    {
        if (driverNames.Count == 0) return string.Empty;

        if (locked)
        {
            return
                "        <!-- Dial-In (Locked) -->\n" +
                "        <h2 class=\"section-title\">Your Dial-In</h2>\n" +
                "        <div class=\"dialin-locked-notice\">\n" +
                "            Round in progress &mdash; dial-in updates are locked until the next round.\n" +
                "        </div>\n";
        }

        StringBuilder options = new StringBuilder();
        options.AppendLine("                <option value=\"\">&#8212; select your name &#8212;</option>");
        foreach (var name in driverNames)
            options.AppendLine($"                <option value=\"{Html(name)}\">{Html(name)}</option>");

        return
            "        <!-- Dial-In Update Form -->\n" +
            "        <h2 class=\"section-title\">Your Dial-In</h2>\n" +
            "        <div class=\"dialin-form\" id=\"dialin-form\">\n" +
            "            <h3>Set Your Dial-In</h3>\n" +
            "            <label for=\"dialin-name\">Your Name</label>\n" +
            $"            <select id=\"dialin-name\">\n{options}            </select>\n" +
            "            <div class=\"form-row\">\n" +
            "                <div>\n" +
            "                    <label for=\"dialin-value\">Dial-In (seconds)</label>\n" +
            "                    <input type=\"number\" id=\"dialin-value\" step=\"0.001\" min=\"0.001\" placeholder=\"e.g. 3.250\" />\n" +
            "                </div>\n" +
            "                <div>\n" +
            "                    <label for=\"dialin-pin\">PIN (optional)</label>\n" +
            "                    <input type=\"password\" id=\"dialin-pin\" maxlength=\"4\" placeholder=\"4-digit PIN\" />\n" +
            "                </div>\n" +
            "            </div>\n" +
            "            <button id=\"dialin-submit\">Save Dial-In</button>\n" +
            "            <div class=\"dialin-status\" id=\"dialin-status\"></div>\n" +
            "        </div>\n";
    }

    private static string DialInBadge(double? dialIn)
    {
        if (dialIn == null) return string.Empty;
        return $"<span class=\"dial-in-badge\">{dialIn.Value:F3}s</span>";
    }

    private static string Html(string? value)
    {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }
}
