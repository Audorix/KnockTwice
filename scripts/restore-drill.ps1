<#
.SYNOPSIS
  Disposable backup-and-restore drill (Implementation §12.4 "Restore from backup").

.DESCRIPTION
  Follows docs/backup-restore.md end to end on throwaway data, never on a real installation:

    1. Starts the API on a new database and attachment folder in a temporary directory.
    2. Creates two accounts, a contact, a direct conversation, a message and an attachment.
    3. Stops the API and takes a backup: the SQLite file, or pg_dump --format=custom, plus the
       attachment folder.
    4. Restores the backup into a different location (a new SQLite file or a new PostgreSQL
       database, and a new attachment folder), runs "migrate" as after any restore, and starts the
       API on the restored copy.
    5. Checks that the accounts sign in with their passwords, the contact and the history are
       there, and the attachment downloads with the same SHA-256 as the uploaded file.

  Requires a Release build (dotnet build -c Release). PostgreSQL needs the CHATX_TEST_POSTGRES
  connection string of an account that may CREATE DATABASE (as for the integration tests) and the
  PostgreSQL client tools (pg_dump, pg_restore, psql); the drill databases are dropped at the end.
  Nothing is written outside the temporary directory, and no password is written to disk.

.EXAMPLE
  ./scripts/restore-drill.ps1 -Provider Sqlite

.EXAMPLE
  $env:CHATX_TEST_POSTGRES = "Host=localhost;Port=5432;Username=postgres;Password=...;Database=postgres"
  ./scripts/restore-drill.ps1 -Provider PostgreSql -PgBin "C:\Program Files\PostgreSQL\18\bin"
#>
[CmdletBinding()]
param(
    [ValidateSet('Sqlite', 'PostgreSql')] [string] $Provider = 'Sqlite',
    [string] $PgBin,
    [int] $Port = 5091,
    [switch] $Keep
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$root = Split-Path -Parent $PSScriptRoot
$apiDir = Join-Path $root 'src/Api/Audorix.ChatX.Api/bin/Release/net10.0'
if (-not (Test-Path (Join-Path $apiDir 'Audorix.ChatX.Api.dll'))) { throw 'Build first: dotnet build Audorix.ChatX.slnx -c Release' }

$work = Join-Path ([IO.Path]::GetTempPath()) ('chatx-restore-drill-' + [guid]::NewGuid().ToString('N'))
$live = Join-Path $work 'live'
$backup = Join-Path $work 'backup'
$restored = Join-Path $work 'restored'
New-Item -ItemType Directory -Force $live, $backup, $restored | Out-Null
$password = 'drill horse battery ' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$baseUrl = "http://localhost:$Port"
$checks = New-Object System.Collections.Generic.List[string]

# ---- PostgreSQL helpers ------------------------------------------------------------------------
$pg = $null
if ($Provider -eq 'PostgreSql') {
    if (-not $env:CHATX_TEST_POSTGRES) { throw 'Set CHATX_TEST_POSTGRES (see docs/development/testing.md).' }
    $pg = @{}
    foreach ($part in $env:CHATX_TEST_POSTGRES.Split(';')) {
        $pair = $part.Split('=', 2)
        if ($pair.Count -eq 2) { $pg[$pair[0].Trim().ToLowerInvariant()] = $pair[1] }
    }
    $pg.port = if ($pg.port) { $pg.port } else { '5432' }
    $pg.user = if ($pg.username) { $pg.username } else { $pg['user id'] }
    $suffix = [guid]::NewGuid().ToString('N').Substring(0, 12)
    $pg.live = "chatx_drill_live_$suffix"
    $pg.restored = "chatx_drill_restored_$suffix"
    $env:PGPASSWORD = $pg.password # only for this process and the tools it starts
}

function Get-PgTool([string] $name) {
    if ($PgBin) {
        foreach ($candidate in @("$name.exe", $name)) {
            $path = Join-Path $PgBin $candidate
            if (Test-Path $path) { return $path }
        }
        throw "$name not found in $PgBin."
    }
    $command = Get-Command $name -ErrorAction SilentlyContinue
    if (-not $command) { throw "$name not found: put the PostgreSQL client tools on PATH or pass -PgBin." }
    return $command.Source
}

function Get-ConnectionString([string] $where) {
    if ($Provider -eq 'Sqlite') { return "Data Source=$(Join-Path $where 'chatx.db')" }
    $database = if ($where -eq $live) { $pg.live } else { $pg.restored }
    return "Host=$($pg.host);Port=$($pg.port);Username=$($pg.user);Password=$($pg.password);Database=$database"
}

# ---- API process -----------------------------------------------------------------------------
# The connection string (it may hold a password) goes to the API through its environment, not
# the command line.
function Get-ApiArguments([string] $where) {
    $env:Database__ConnectionString = Get-ConnectionString $where
    return @(
        '--environment', 'Development', '--urls', $baseUrl,
        "--Database:Provider=$Provider", '--Database:MigrateOnStartup=false',
        '--Setup:Completed=true', "--Setup:SettingsPath=$(Join-Path $where 'chatx.settings.json')",
        "--Storage:Local:Path=$(Join-Path $where 'attachments')",
        '--Identity:AutoConfirmEmail=true', '--Captcha:Provider=None', '--DemoData:Enabled=false', "--App:PublicBaseUrl=$baseUrl",
        '--Email:Provider=Smtp', '--Email:Smtp:Host=localhost')
}

function Invoke-Api([string] $where, [string[]] $command) {
    $arguments = @((Join-Path $apiDir 'Audorix.ChatX.Api.dll')) + $command + (Get-ApiArguments $where)
    $process = Start-Process dotnet -ArgumentList ($arguments | ForEach-Object { '"' + $_ + '"' }) -WorkingDirectory $apiDir -NoNewWindow -PassThru -Wait `
        -RedirectStandardOutput (Join-Path $work 'command.log') -RedirectStandardError (Join-Path $work 'command.err')
    if ($process.ExitCode -ne 0) { throw "API command '$($command -join ' ')' failed; see $work" }
}

function Start-Api([string] $where) {
    $arguments = @((Join-Path $apiDir 'Audorix.ChatX.Api.dll')) + (Get-ApiArguments $where)
    $process = Start-Process dotnet -ArgumentList ($arguments | ForEach-Object { '"' + $_ + '"' }) -WorkingDirectory $apiDir -PassThru `
        -RedirectStandardOutput (Join-Path $where 'api.log') -RedirectStandardError (Join-Path $where 'api.err') -WindowStyle Hidden
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 500
        try { if ((Invoke-WebRequest "$baseUrl/health/ready" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200) { return $process } } catch { }
        if ($process.HasExited) { throw "The API stopped; see $where" }
    }
    Stop-Process $process -Force
    throw "The API did not become ready; see $where"
}

function Stop-Api($process) {
    if ($process -and -not $process.HasExited) { Stop-Process $process -Force; $process.WaitForExit() }
    Start-Sleep -Milliseconds 500 # let SQLite release the file
}

# ---- A browser-like client: cookies plus the antiforgery header -----------------------------------
function New-Browser {
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.CookieContainer = New-Object System.Net.CookieContainer
    $client = New-Object System.Net.Http.HttpClient($handler)
    $client.BaseAddress = [Uri] $baseUrl
    return [pscustomobject]@{ Client = $client; Csrf = $null; Id = $null }
}

function Send-Request($browser, [string] $method, [string] $path, $body = $null, $content = $null) {
    $request = New-Object System.Net.Http.HttpRequestMessage((New-Object System.Net.Http.HttpMethod($method)), $path)
    if ($method -ne 'GET') {
        if (-not $browser.Csrf) { $browser.Csrf = (Send-Request $browser 'GET' '/api/v1/auth/csrf').token }
        $request.Headers.Add('X-CSRF-TOKEN', $browser.Csrf)
    }
    if ($null -ne $body) { $request.Content = New-Object System.Net.Http.StringContent(($body | ConvertTo-Json -Depth 6), [Text.Encoding]::UTF8, 'application/json') }
    if ($null -ne $content) { $request.Content = $content }
    $response = $browser.Client.SendAsync($request).GetAwaiter().GetResult()
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if (-not $response.IsSuccessStatusCode) { throw "$method $path -> $([int]$response.StatusCode) $text" }
    if ($text) { return $text | ConvertFrom-Json }
}

function Connect-Person([string] $email) {
    $browser = New-Browser
    Send-Request $browser 'POST' '/api/v1/auth/login' @{ email = $email; password = $password; rememberMe = $false } | Out-Null
    $browser.Csrf = $null # the token is bound to the signed-in user
    $browser.Id = (Send-Request $browser 'GET' '/api/v1/users/me').id
    return $browser
}

function Assert-That([bool] $condition, [string] $what) {
    if (-not $condition) { throw "FAILED: $what" }
    $checks.Add($what)
    Write-Host "  ok  $what" -ForegroundColor Green
}

$apiProcess = $null
try {
    Write-Host "==> Restore drill ($Provider) in $work" -ForegroundColor Cyan
    if ($pg) { & (Get-PgTool 'psql') -h $pg.host -p $pg.port -U $pg.user -d postgres -v ON_ERROR_STOP=1 -q -c "CREATE DATABASE $($pg.live);" | Out-Null }

    # 1-2. A live installation with some data.
    Invoke-Api $live @('migrate')
    $apiProcess = Start-Api $live
    foreach ($person in @(@('ana@drill.test', 'Ana Drill'), @('ben@drill.test', 'Ben Drill'))) {
        Send-Request (New-Browser) 'POST' '/api/v1/auth/register' @{ email = $person[0]; password = $password; displayName = $person[1]; captchaToken = 't' } | Out-Null
    }
    $ana = Connect-Person 'ana@drill.test'
    $ben = Connect-Person 'ben@drill.test'
    Send-Request $ana 'POST' '/api/v1/contacts/requests' @{ userId = $ben.Id } | Out-Null
    Send-Request $ben 'POST' '/api/v1/contacts/requests' @{ userId = $ana.Id } | Out-Null
    $direct = Send-Request $ana 'POST' '/api/v1/conversations/direct' @{ userId = $ben.Id }

    $png = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==')
    $form = New-Object System.Net.Http.MultipartFormDataContent
    $file = New-Object System.Net.Http.ByteArrayContent(, $png)
    $file.Headers.ContentType = New-Object System.Net.Http.Headers.MediaTypeHeaderValue('image/png')
    $form.Add($file, 'file', 'drill.png')
    $attachment = Send-Request $ana 'POST' "/api/v1/conversations/$($direct.id)/attachments" -content $form
    Send-Request $ana 'POST' "/api/v1/conversations/$($direct.id)/messages" @{ clientMessageId = 'drill-1'; body = 'Before the backup'; attachmentIds = @($attachment.id) } | Out-Null
    $sha = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($png)).Replace('-', '')

    # 3. Backup: stop, then copy the database and the attachment folder (docs/backup-restore.md).
    Stop-Api $apiProcess; $apiProcess = $null
    if ($Provider -eq 'Sqlite') {
        Copy-Item (Join-Path $live 'chatx.db*') $backup
    } else {
        & (Get-PgTool 'pg_dump') -h $pg.host -p $pg.port -U $pg.user -d $pg.live --format=custom -f (Join-Path $backup 'chatx.dump')
        if ($LASTEXITCODE -ne 0) { throw 'pg_dump failed' }
    }
    Copy-Item -Recurse (Join-Path $live 'attachments') (Join-Path $backup 'attachments')
    Write-Host '  backup taken'

    # 4. Restore into a different place and start the API there.
    if ($Provider -eq 'Sqlite') {
        Copy-Item (Join-Path $backup 'chatx.db*') $restored
    } else {
        & (Get-PgTool 'psql') -h $pg.host -p $pg.port -U $pg.user -d postgres -v ON_ERROR_STOP=1 -q -c "CREATE DATABASE $($pg.restored);" | Out-Null
        & (Get-PgTool 'pg_restore') -h $pg.host -p $pg.port -U $pg.user -d $pg.restored --no-owner --exit-on-error (Join-Path $backup 'chatx.dump')
        if ($LASTEXITCODE -ne 0) { throw 'pg_restore failed' }
    }
    Copy-Item -Recurse (Join-Path $backup 'attachments') (Join-Path $restored 'attachments')
    Invoke-Api $restored @('migrate')
    $apiProcess = Start-Api $restored
    Write-Host '  restored; checking'

    # 5. Everything is back and consistent.
    $ana = Connect-Person 'ana@drill.test'
    Assert-That ($null -ne $ana.Id) 'Accounts sign in with their passwords after the restore'
    $ben = Connect-Person 'ben@drill.test'
    $contacts = @(Send-Request $ben 'GET' '/api/v1/contacts')
    Assert-That (@($contacts | Where-Object { $_.user.id -eq $ana.Id }).Count -eq 1) 'Contacts are restored'
    $history = Send-Request $ben 'GET' "/api/v1/conversations/$($direct.id)/history"
    $message = @($history.items | Where-Object { $_.kind -eq 'message' -and $_.body -eq 'Before the backup' })
    Assert-That ($message.Count -eq 1) 'The message is in the history'
    Assert-That ($message[0].attachments[0].id -eq $attachment.id) 'The message still refers to its attachment'
    $download = $ben.Client.GetAsync("/api/v1/attachments/$($attachment.id)/content").GetAwaiter().GetResult()
    Assert-That $download.IsSuccessStatusCode 'The attachment downloads'
    $bytes = $download.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
    $downloadedSha = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($bytes)).Replace('-', '')
    Assert-That ($downloadedSha -eq $sha) 'The attachment content is identical (SHA-256)'
    Send-Request $ben 'POST' "/api/v1/conversations/$($direct.id)/messages" @{ clientMessageId = 'drill-2'; body = 'After the restore' } | Out-Null
    Assert-That $true 'New messages can be sent on the restored installation'

    Write-Host "==> Restore drill passed: $($checks.Count) checks ($Provider)" -ForegroundColor Green
}
finally {
    Stop-Api $apiProcess
    if ($pg) {
        foreach ($database in @($pg.live, $pg.restored)) {
            & (Get-PgTool 'psql') -h $pg.host -p $pg.port -U $pg.user -d postgres -q -c "DROP DATABASE IF EXISTS $database WITH (FORCE);" 2>$null | Out-Null
        }
        Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    }
    Remove-Item Env:Database__ConnectionString -ErrorAction SilentlyContinue
    if ($Keep) { Write-Host "Kept $work" } else { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
}
