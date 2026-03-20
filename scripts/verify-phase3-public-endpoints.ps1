param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src/RCDragLiveServer/RCDragLiveServer.csproj'
$validFixturePath = Join-Path $repoRoot 'scripts/fixtures/live-update.valid.json'
$baseUrl = 'http://127.0.0.1:5099'
$liveUrl = "$baseUrl/api/live"
$healthUrl = "$baseUrl/health"
$updateUrl = "$baseUrl/api/update"
$apiKey = 'phase3-test-key'

if (-not (Test-Path $projectPath)) { throw "Project file not found: $projectPath" }
if (-not (Test-Path $validFixturePath)) { throw "Fixture file not found: $validFixturePath" }

function Stop-StaleServerProcesses {
    $candidatePids = [System.Collections.Generic.HashSet[int]]::new()

    try {
        $matchingProcesses = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction Stop |
            Where-Object { $_.CommandLine -like '*RCDragLiveServer*' }

        foreach ($process in $matchingProcesses) {
            [void]$candidatePids.Add([int]$process.ProcessId)
        }
    }
    catch {
        # Best effort. Continue with port-based detection.
    }

    $listenerPids = @()

    try {
        $listenerPids = Get-NetTCPConnection -State Listen -LocalPort 5099 -ErrorAction Stop |
            Select-Object -ExpandProperty OwningProcess -Unique
    }
    catch {
        $listenerPids = @()
    }

    foreach ($pid in $listenerPids) {
        [void]$candidatePids.Add([int]$pid)
    }

    foreach ($pid in $candidatePids) {
        try {
            $process = Get-Process -Id $pid -ErrorAction Stop
            Stop-Process -Id $process.Id -Force -ErrorAction Stop
            Write-Host "Stopped stale process on port 5099 (PID $($process.Id))."
        }
        catch {
            Write-Warning "Failed to stop stale process PID ${pid}: $($_.Exception.Message)"
        }
    }
}

function Assert-NoCacheHeaders {
    param([Parameter(Mandatory = $true)]$Response, [Parameter(Mandatory = $true)][string]$Endpoint)

    $cacheControl = [string]$Response.Headers['Cache-Control']
    $pragma = [string]$Response.Headers['Pragma']
    $expires = [string]$Response.Headers['Expires']

    if ($cacheControl -notmatch 'no-store' -or $cacheControl -notmatch 'no-cache') {
        throw "$Endpoint missing expected Cache-Control no-store/no-cache header. Actual: '$cacheControl'"
    }

    if ($pragma -ne 'no-cache') {
        throw "$Endpoint missing expected Pragma header. Actual: '$pragma'"
    }

    if ($expires -ne '0') {
        throw "$Endpoint missing expected Expires header. Actual: '$expires'"
    }
}

function ConvertTo-JsonMap {
    param([Parameter(Mandatory = $true)][string]$Json)

    return ConvertFrom-Json -InputObject $Json
}

function Get-JsonValue {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    foreach ($property in $Object.PSObject.Properties) {
        if ([string]::Equals($property.Name, $Name, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $property.Value
        }
    }

    return $null
}

function Has-JsonKey {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    foreach ($property in $Object.PSObject.Properties) {
        if ([string]::Equals($property.Name, $Name, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Wait-ForServer {
    param([Parameter(Mandatory = $true)][Diagnostics.Process]$Process)

    for ($attempt = 1; $attempt -le 60; $attempt++) {
        Start-Sleep -Milliseconds 250

        if ($Process.HasExited) {
            $stderr = if (Test-Path $stderrPath) { (Get-Content -Raw $stderrPath) } else { '' }
            $stdout = if (Test-Path $stdoutPath) { (Get-Content -Raw $stdoutPath) } else { '' }
            throw "API process exited before readiness. ExitCode=$($Process.ExitCode)`nSTDERR:`n$stderr`nSTDOUT:`n$stdout"
        }

        try {
            $response = Invoke-WebRequest -Method Get -Uri $healthUrl -UseBasicParsing
            if ($response.StatusCode -eq 200) {
                return
            }
        }
        catch {
            # keep polling
        }
    }

    throw "API did not become reachable at $baseUrl within timeout."
}

$proc = $null
$stdoutPath = Join-Path $repoRoot 'scripts/.phase3-verify.stdout.log'
$stderrPath = Join-Path $repoRoot 'scripts/.phase3-verify.stderr.log'

try {
    Stop-StaleServerProcesses

    $env:ASPNETCORE_URLS = $baseUrl
    $env:ApiKey = $apiKey

    Remove-Item -Force -ErrorAction SilentlyContinue $stdoutPath, $stderrPath
    $dotnetArgs = @('run', '--project', "`"$projectPath`"", '--no-build', '--no-launch-profile')
    $proc = Start-Process -FilePath 'dotnet' -ArgumentList $dotnetArgs -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath

    Wait-ForServer -Process $proc

    $healthResponse = Invoke-WebRequest -Method Get -Uri $healthUrl -UseBasicParsing
    if ($healthResponse.StatusCode -ne 200) {
        throw "GET /health expected 200 but got $($healthResponse.StatusCode)"
    }

    $healthBody = ConvertTo-JsonMap -Json $healthResponse.Content
    if ((Get-JsonValue -Object $healthBody -Name 'status') -ne 'healthy') {
        throw "GET /health expected body status='healthy' but got: $($healthResponse.Content)"
    }

    Assert-NoCacheHeaders -Response $healthResponse -Endpoint 'GET /health'

    $preSeedLiveResponse = Invoke-WebRequest -Method Get -Uri $liveUrl -UseBasicParsing
    if ($preSeedLiveResponse.StatusCode -ne 200) {
        throw "GET /api/live expected 200 before seeding but got $($preSeedLiveResponse.StatusCode)"
    }

    Assert-NoCacheHeaders -Response $preSeedLiveResponse -Endpoint 'GET /api/live (pre-seed)'

    $preSeedLive = ConvertTo-JsonMap -Json $preSeedLiveResponse.Content
    $requiredKeys = @('eventName', 'eventDate', 'currentRound', 'nextUp', 'matches')
    foreach ($key in $requiredKeys) {
        if (-not (Has-JsonKey -Object $preSeedLive -Name $key)) {
            throw "GET /api/live before seeding missing key '$key'. Body: $($preSeedLiveResponse.Content)"
        }
    }

    if ($preSeedLiveResponse.Content -notmatch '(?i)"matches"\s*:\s*\[\s*\]') {
        throw "GET /api/live before seeding expected an empty matches array. Body: $($preSeedLiveResponse.Content)"
    }

    $validPayloadRaw = Get-Content -Raw $validFixturePath
    $fixturePayload = ConvertTo-JsonMap -Json $validPayloadRaw
    $pascalMatches = @()
    $camelMatches = @()
    foreach ($match in @(Get-JsonValue -Object $fixturePayload -Name 'matches')) {
        $driver1 = Get-JsonValue -Object $match -Name 'driver1'
        $driver2 = Get-JsonValue -Object $match -Name 'driver2'
        $pascalMatches += @{
            Driver1 = $driver1
            Driver2 = $driver2
        }
        $camelMatches += @{
            driver1 = $driver1
            driver2 = $driver2
        }
    }

    $eventName = [string](Get-JsonValue -Object $fixturePayload -Name 'eventName')
    $eventDate = [string](Get-JsonValue -Object $fixturePayload -Name 'eventDate')
    $currentRound = [string](Get-JsonValue -Object $fixturePayload -Name 'currentRound')
    $nextUp = [string](Get-JsonValue -Object $fixturePayload -Name 'nextUp')
    $eventNameJson = $eventName | ConvertTo-Json -Compress
    $eventDateJson = $eventDate | ConvertTo-Json -Compress
    $currentRoundJson = $currentRound | ConvertTo-Json -Compress
    $nextUpJson = $nextUp | ConvertTo-Json -Compress
    $matchesPascalJson = $pascalMatches | ConvertTo-Json -Depth 10 -Compress
    $matchesCamelJson = $camelMatches | ConvertTo-Json -Depth 10 -Compress

    $payloadJson = @"
{"EventName":$eventNameJson,"EventDate":$eventDateJson,"CurrentRound":$currentRoundJson,"NextUp":$nextUpJson,"matches":$matchesCamelJson,"Matches":$matchesPascalJson}
"@

    $updateResponse = Invoke-WebRequest -Method Post -Uri $updateUrl -UseBasicParsing -Headers @{ 'X-API-KEY' = $apiKey } -ContentType 'application/json' -Body $payloadJson
    if ($updateResponse.StatusCode -ne 200) {
        throw "POST /api/update expected 200 but got $($updateResponse.StatusCode)"
    }

    $updateBody = ConvertTo-JsonMap -Json $updateResponse.Content
    if ((Get-JsonValue -Object $updateBody -Name 'status') -ne 'updated') {
        throw "POST /api/update expected body status='updated' but got: $($updateResponse.Content)"
    }

    $postSeedLiveResponse = Invoke-WebRequest -Method Get -Uri $liveUrl -UseBasicParsing
    if ($postSeedLiveResponse.StatusCode -ne 200) {
        throw "GET /api/live expected 200 after seeding but got $($postSeedLiveResponse.StatusCode)"
    }

    Assert-NoCacheHeaders -Response $postSeedLiveResponse -Endpoint 'GET /api/live (post-seed)'

    $expectedLive = $fixturePayload
    $actualLive = ConvertTo-JsonMap -Json $postSeedLiveResponse.Content

    if ((Get-JsonValue -Object $actualLive -Name 'eventName') -ne (Get-JsonValue -Object $expectedLive -Name 'eventName')) { throw "eventName mismatch after seeding. Body: $($postSeedLiveResponse.Content)" }
    if ((Get-JsonValue -Object $actualLive -Name 'eventDate') -ne (Get-JsonValue -Object $expectedLive -Name 'eventDate')) { throw "eventDate mismatch after seeding. Body: $($postSeedLiveResponse.Content)" }
    if ((Get-JsonValue -Object $actualLive -Name 'currentRound') -ne (Get-JsonValue -Object $expectedLive -Name 'currentRound')) { throw "currentRound mismatch after seeding. Body: $($postSeedLiveResponse.Content)" }
    if ((Get-JsonValue -Object $actualLive -Name 'nextUp') -ne (Get-JsonValue -Object $expectedLive -Name 'nextUp')) { throw "nextUp mismatch after seeding. Body: $($postSeedLiveResponse.Content)" }

    $expectedMatches = @((Get-JsonValue -Object $expectedLive -Name 'matches'))
    $actualMatches = @((Get-JsonValue -Object $actualLive -Name 'matches'))

    if ($actualMatches.Count -ne $expectedMatches.Count) {
        throw "matches count mismatch after seeding. Expected $($expectedMatches.Count), got $($actualMatches.Count)."
    }

    for ($i = 0; $i -lt $expectedMatches.Count; $i++) {
        if ($actualMatches[$i].driver1 -ne $expectedMatches[$i].driver1 -or $actualMatches[$i].driver2 -ne $expectedMatches[$i].driver2) {
            throw "matches[$i] mismatch after seeding."
        }
    }

    Write-Host 'Phase 3 public endpoint verification passed.'
}
finally {
    if ($null -ne $proc) {
        try {
            if (-not $proc.HasExited) {
                Stop-Process -Id $proc.Id -Force -ErrorAction Stop
                $proc.WaitForExit(5000) | Out-Null
            }
        }
        catch {
            Write-Warning "Failed to stop spawned process PID $($proc.Id): $($_.Exception.Message)"
        }
    }

    Remove-Item -Force -ErrorAction SilentlyContinue $stdoutPath, $stderrPath
}
