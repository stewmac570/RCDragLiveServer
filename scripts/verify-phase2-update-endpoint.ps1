param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src/RCDragLiveServer/RCDragLiveServer.csproj'
$validFixture = Join-Path $repoRoot 'scripts/fixtures/live-update.valid.json'
$invalidFixture = Join-Path $repoRoot 'scripts/fixtures/live-update.invalid-missing-matches.json'
$baseUrl = 'http://127.0.0.1:5099'
$updateUrl = "$baseUrl/api/update"

if (-not (Test-Path $projectPath)) { throw "Project file not found: $projectPath" }
if (-not (Test-Path $validFixture)) { throw "Valid fixture not found: $validFixture" }
if (-not (Test-Path $invalidFixture)) { throw "Invalid fixture not found: $invalidFixture" }

$env:ASPNETCORE_URLS = $baseUrl
$env:ApiKey = 'phase2-test-key'

$proc = $null
$staleProcesses = @()

function Stop-StaleServerProcesses {
    param([Parameter(Mandatory = $true)][string]$ProjectPath)

    $dotnetProcs = @()
    try {
        $dotnetProcs = Get-NetTCPConnection -State Listen -LocalPort 5099 -ErrorAction Stop |
            Select-Object -ExpandProperty OwningProcess -Unique |
            ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue } |
            Where-Object { $null -ne $_ }
    }
    catch {
        Write-Warning "Unable to query listeners on port 5099: $($_.Exception.Message)"
    }

    foreach ($dotnetProc in $dotnetProcs) {
        try {
            Stop-Process -Id $dotnetProc.Id -Force -ErrorAction Stop
            Write-Host "Stopped stale process PID $($dotnetProc.Id) on port 5099."
        }
        catch {
            Write-Warning "Unable to stop stale process PID $($dotnetProc.Id): $($_.Exception.Message)"
        }
    }
}

function Assert-JsonBody {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][hashtable]$Expected
    )

    $actual = $Content | ConvertFrom-Json
    foreach ($key in $Expected.Keys) {
        if (-not ($actual.PSObject.Properties.Name -contains $key)) {
            throw "Expected key '$key' in response body. Body: $Content"
        }

        if ($actual.$key -ne $Expected[$key]) {
            throw "Expected '$key'='$($Expected[$key])' but got '$($actual.$key)'. Body: $Content"
        }
    }
}

function Invoke-Update {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int]$ExpectedStatus,
        [Parameter()][string]$ApiKey,
        [Parameter(Mandatory = $true)][hashtable]$ExpectedBody
    )

    $body = Get-Content -Raw $Path
    $headers = @{}
    if ($null -ne $ApiKey) {
        $headers['X-API-KEY'] = $ApiKey
    }

    try {
        $response = Invoke-WebRequest -Method Post -Uri $updateUrl -Headers $headers -ContentType 'application/json' -Body $body
        if ($response.StatusCode -ne $ExpectedStatus) {
            throw "Expected status $ExpectedStatus but got $($response.StatusCode). Body: $($response.Content)"
        }

        Assert-JsonBody -Content $response.Content -Expected $ExpectedBody
    }
    catch {
        $webException = $_.Exception
        if ($webException.PSObject.Properties.Name -contains 'Response' -and $null -ne $webException.Response) {
            $status = [int]$webException.Response.StatusCode
            $reader = New-Object System.IO.StreamReader($webException.Response.GetResponseStream())
            $content = $reader.ReadToEnd()
            $reader.Dispose()

            if ($status -ne $ExpectedStatus) {
                throw "Expected status $ExpectedStatus but got $status. Body: $content"
            }

            Assert-JsonBody -Content $content -Expected $ExpectedBody
        }
        else {
            throw
        }
    }
}

try {
    Stop-StaleServerProcesses -ProjectPath $projectPath

    $proc = Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--project', $projectPath, '--no-build', '--no-launch-profile') -PassThru -WindowStyle Hidden

    $healthy = $false
    for ($i = 0; $i -lt 50; $i++) {
        Start-Sleep -Milliseconds 250
        try {
            Invoke-WebRequest -Method Post -Uri $updateUrl -Headers @{ 'X-API-KEY' = 'phase2-test-key' } -ContentType 'application/json' -Body (Get-Content -Raw $validFixture) | Out-Null
            $healthy = $true
            break
        }
        catch {
            if ($proc.HasExited) {
                throw "API process exited before becoming reachable. ExitCode=$($proc.ExitCode)"
            }
        }
    }

    if (-not $healthy) {
        throw "API did not become reachable at $baseUrl within timeout."
    }

    Invoke-Update -Path $validFixture -ExpectedStatus 200 -ApiKey 'phase2-test-key' -ExpectedBody @{ status = 'updated' }
    Invoke-Update -Path $validFixture -ExpectedStatus 401 -ExpectedBody @{ error = 'unauthorized' }
    Invoke-Update -Path $validFixture -ExpectedStatus 401 -ApiKey 'wrong-key' -ExpectedBody @{ error = 'unauthorized' }
    Invoke-Update -Path $invalidFixture -ExpectedStatus 400 -ApiKey 'phase2-test-key' -ExpectedBody @{ error = 'invalid_payload' }

    Write-Host 'Phase 2 update endpoint ingress checks passed.'
}
finally {
    if ($null -ne $proc -and -not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force
        $proc.WaitForExit(5000) | Out-Null
    }
}
