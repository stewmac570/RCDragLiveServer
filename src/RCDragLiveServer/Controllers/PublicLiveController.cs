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

    public PublicLiveController(ILiveRaceStateStore stateStore)
    {
        this.stateStore = stateStore;
    }

    [HttpGet("")]
    public ContentResult Home()
    {
        Dictionary<string, LiveRaceState> classes = stateStore.GetAll();

        ApplyNoCacheHeaders();

        string html = BuildHomePage(classes);

        return Content(html, "text/html; charset=utf-8");
    }

    [HttpGet("api/live")]
    public ActionResult<LiveRaceState> GetLive()
    {
        ApplyNoCacheHeaders();

        return Ok(stateStore.GetLatest());
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

    private static string BuildHomePage(Dictionary<string, LiveRaceState> classes)
    {
        if (classes.Count == 0)
        {
            return BuildNoEventPage();
        }

        var sortedKeys = classes.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

        StringBuilder tabButtons = new StringBuilder();
        StringBuilder tabPanels = new StringBuilder();

        for (int i = 0; i < sortedKeys.Count; i++)
        {
            string key = sortedKeys[i];
            LiveRaceState state = classes[key];
            string safeKey = Html(key);
            string tabId = $"tab-{WebUtility.UrlEncode(key)}";

            tabButtons.AppendLine($"  <button class=\"tab-btn\" data-tab=\"{safeKey}\" data-panel=\"{tabId}\">{safeKey}</button>");
            tabPanels.AppendLine($"<div id=\"{tabId}\" class=\"tab-panel\">");
            tabPanels.Append(BuildClassPanel(state));
            tabPanels.AppendLine("</div>");
        }

        string firstTabName = Html(sortedKeys[0]);

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>RC Drag Live</title>
    <style>
        * {
            box-sizing: border-box;
        }

        body {
            margin: 0;
            padding: 0;
            font-family: Arial, Helvetica, sans-serif;
            background: #0a0f1a;
            color: #f1f5f9;
        }

        .wrap {
            width: 100%;
            max-width: 760px;
            margin: 0 auto;
            padding: 16px;
        }

        /* ---- Event Header ---- */
        .hero {
            background: #1e293b;
            border: 1px solid #334155;
            border-radius: 16px;
            padding: 20px;
            margin-bottom: 16px;
        }

        .title {
            font-size: 32px;
            font-weight: 900;
            line-height: 1.1;
            margin: 0 0 6px 0;
            color: #f8fafc;
        }

        .event-meta {
            font-size: 15px;
            color: #94a3b8;
            margin: 0 0 10px 0;
        }

        .badge {
            display: inline-block;
            background: #1d4ed8;
            color: #e0f2fe;
            font-size: 12px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.07em;
            padding: 3px 10px;
            border-radius: 999px;
            margin-right: 6px;
        }

        .badge-race {
            background: #7c3aed;
            color: #ede9fe;
        }

        /* ---- Tabs ---- */
        .tab-strip {
            display: flex;
            gap: 6px;
            flex-wrap: wrap;
            margin-bottom: 16px;
        }

        .tab-btn {
            background: #1e293b;
            border: 1px solid #334155;
            border-radius: 999px;
            color: #94a3b8;
            cursor: pointer;
            font-size: 13px;
            font-weight: 700;
            padding: 6px 18px;
            text-transform: uppercase;
            letter-spacing: 0.07em;
            transition: background 0.15s, color 0.15s, border-color 0.15s;
        }

        .tab-btn:hover {
            background: #263348;
            color: #e2e8f0;
        }

        .tab-btn.active {
            background: #1d4ed8;
            border-color: #1d4ed8;
            color: #fff;
        }

        .tab-panel {
            display: none;
        }

        .tab-panel.active {
            display: block;
        }

        /* ---- Section chrome ---- */
        .section-title {
            font-size: 13px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.1em;
            color: #64748b;
            margin: 20px 0 8px 0;
        }

        .panel {
            background: #1e293b;
            border: 1px solid #334155;
            border-radius: 16px;
            padding: 16px;
            margin-bottom: 12px;
        }

        .empty-state,
        .empty-box {
            background: #1e293b;
            border: 1px dashed #475569;
            border-radius: 12px;
            padding: 18px;
            text-align: center;
            color: #94a3b8;
            font-size: 15px;
            margin-bottom: 12px;
        }

        /* ---- Next Up ---- */
        .next-up-drivers {
            font-size: 36px;
            font-weight: 900;
            color: #fbbf24;
            line-height: 1.15;
            word-break: break-word;
            padding: 10px 0 4px;
        }

        /* ---- Bracket ---- */
        .round-header {
            font-size: 13px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.1em;
            color: #38bdf8;
            margin: 14px 0 6px 0;
            padding-bottom: 4px;
            border-bottom: 1px solid #1e40af;
        }

        .match-list {
            display: grid;
            gap: 10px;
            margin-bottom: 4px;
        }

        .match-card {
            background: #1e293b;
            border: 1px solid #334155;
            border-radius: 12px;
            padding: 14px 16px;
            text-align: center;
        }

        .driver {
            font-size: 22px;
            font-weight: 800;
            line-height: 1.2;
            word-break: break-word;
        }

        .driver.winner {
            color: #4ade80;
        }

        .driver.loser {
            color: #64748b;
            font-size: 17px;
            font-weight: 600;
            text-decoration: line-through;
            margin-top: 4px;
        }

        .win-badge {
            display: inline-block;
            background: #14532d;
            color: #86efac;
            font-size: 11px;
            font-weight: 700;
            letter-spacing: 0.08em;
            padding: 2px 7px;
            border-radius: 999px;
            vertical-align: middle;
            margin-left: 6px;
        }

        .vs {
            font-size: 12px;
            font-weight: 700;
            color: #64748b;
            margin: 8px 0;
            text-transform: uppercase;
            letter-spacing: 0.1em;
        }

        /* ---- Winners table ---- */
        .winners-table {
            width: 100%;
            border-collapse: collapse;
            font-size: 15px;
        }

        .winners-table th {
            text-align: left;
            font-size: 11px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.08em;
            color: #64748b;
            padding: 6px 10px;
            border-bottom: 1px solid #334155;
        }

        .winners-table td {
            padding: 8px 10px;
            border-bottom: 1px solid #1e293b;
            vertical-align: middle;
        }

        .winners-table tr:last-child td {
            border-bottom: none;
        }

        .winner-cell {
            font-weight: 700;
            color: #4ade80;
        }

        .loser-cell {
            color: #64748b;
        }

        /* ---- RR Standings ---- */
        .rr-standings {
            background: #0f172a;
            border: 1px solid #334155;
            border-radius: 12px;
            padding: 14px 16px;
            font-family: 'Courier New', Courier, monospace;
            font-size: 13px;
            color: #e2e8f0;
            overflow-x: auto;
            white-space: pre;
            line-height: 1.5;
        }

        /* ---- Footer ---- */
        .footer {
            margin-top: 20px;
            text-align: center;
            color: #475569;
            font-size: 12px;
        }

        @media (min-width: 640px) {
            .match-list {
                grid-template-columns: 1fr 1fr;
            }
        }
    </style>
</head>
<body>
    <div class="wrap">

        <!-- Tab Strip -->
        <div class="tab-strip">
{{tabButtons}}        </div>

        <!-- Tab Panels -->
{{tabPanels}}
        <div class="footer">Auto-refreshes every 5 seconds</div>
    </div>
    <script>
        (function () {
            var STORAGE_KEY = 'rcDragActiveTab';
            var buttons = Array.from(document.querySelectorAll('.tab-btn'));
            var panels = Array.from(document.querySelectorAll('.tab-panel'));

            function activate(tabName) {
                buttons.forEach(function (b) {
                    b.classList.toggle('active', b.dataset.tab === tabName);
                });
                panels.forEach(function (p) {
                    var btn = buttons.find(function (b) { return b.dataset.panel === p.id; });
                    p.classList.toggle('active', btn ? btn.dataset.tab === tabName : false);
                });
                sessionStorage.setItem(STORAGE_KEY, tabName);
            }

            buttons.forEach(function (btn) {
                btn.addEventListener('click', function () {
                    activate(btn.dataset.tab);
                });
            });

            // Restore saved tab or default to first
            var saved = sessionStorage.getItem(STORAGE_KEY);
            var initial = (saved && buttons.some(function (b) { return b.dataset.tab === saved; }))
                ? saved
                : '{{firstTabName}}';
            activate(initial);

            // Auto-refresh preserving active tab
            setTimeout(function () { location.reload(); }, 5000);
        })();
    </script>
</body>
</html>
""";
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
        .no-event h1 {
            font-size: 24px;
            font-weight: 900;
            margin: 0 0 10px 0;
            color: #f8fafc;
        }
        .no-event p {
            color: #94a3b8;
            margin: 0;
            font-size: 15px;
        }
        .footer {
            margin-top: 24px;
            color: #475569;
            font-size: 12px;
        }
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
        string raceType = Html(state.RaceType);
        string nextUp = Html(state.NextUp);

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
                    bool resolved = !string.IsNullOrWhiteSpace(match.WinnerName);

                    bracketHtml.AppendLine("    <div class=\"match-card\">");

                    if (resolved)
                    {
                        string winner = Html(match.WinnerName);
                        string loser = match.WinnerName == match.Driver1 ? driver2 : driver1;
                        bracketHtml.AppendLine($"      <div class=\"driver winner\">{winner} <span class=\"win-badge\">WIN</span></div>");
                        bracketHtml.AppendLine($"      <div class=\"driver loser\">{loser}</div>");
                    }
                    else
                    {
                        bracketHtml.AppendLine($"      <div class=\"driver driver1\">{driver1}</div>");
                        bracketHtml.AppendLine("      <div class=\"vs\">vs</div>");
                        bracketHtml.AppendLine($"      <div class=\"driver driver2\">{driver2}</div>");
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
            : $"""

            <h2 class="section-title">Round Robin Standings</h2>
            <pre class="rr-standings">{Html(state.RRStandings)}</pre>
            """;

        string classTypeBadge = string.IsNullOrWhiteSpace(classType) ? string.Empty : $"<span class=\"badge\">{classType}</span>";
        string raceTypeBadge = string.IsNullOrWhiteSpace(raceType) ? string.Empty : $"<span class=\"badge badge-race\">{raceType}</span>";

        return $"""
        <!-- Event Header -->
        <div class="hero">
            <h1 class="title">{(string.IsNullOrWhiteSpace(eventName) ? "RC Drag Live" : eventName)}</h1>
            <p class="event-meta">{(string.IsNullOrWhiteSpace(eventDate) ? "Waiting for event date" : eventDate)}</p>
            {classTypeBadge}{raceTypeBadge}
        </div>

        <!-- Next Up -->
        <h2 class="section-title">Next Up</h2>
        <div class="panel">
            {nextUpHtml}
        </div>

        <!-- Full Bracket -->
        <h2 class="section-title">Full Bracket</h2>
        {bracketHtml}
        <!-- Winners List -->
        <h2 class="section-title">Winners</h2>
        <div class="panel">
            {winnersHtml}
        </div>
        {rrHtml}
""";
    }

    private static string Html(string? value)
    {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }
}
