<#
.SYNOPSIS
  Reproducible local validation for Audorix.ChatX (no CI provider is assumed).

.DESCRIPTION
  1. Restores, builds and tests the .NET solution (unit, architecture and integration tests).
     Integration tests use real, disposable SQLite files. PostgreSQL integration tests run
     only when -PostgresAdminConnectionString (or CHATX_TEST_POSTGRES) is provided; they
     create and drop uniquely named chatx_test_* databases and touch nothing else.
  2. Installs frontend dependencies from the lockfile, type-checks, tests and builds.

.EXAMPLE
  ./scripts/validate.ps1
.EXAMPLE
  ./scripts/validate.ps1 -PostgresAdminConnectionString "Host=localhost;Username=chatx_test;Password=..."
#>
[CmdletBinding()]
param(
    [string] $PostgresAdminConnectionString = $env:CHATX_TEST_POSTGRES,
    [switch] $SkipFrontend
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    function Invoke-Step([string] $Name, [scriptblock] $Command) {
        Write-Host "`n==> $Name" -ForegroundColor Cyan
        & $Command
        if ($LASTEXITCODE -ne 0) { throw "Step failed: $Name (exit code $LASTEXITCODE)" }
    }

    if ($PostgresAdminConnectionString) {
        $env:CHATX_TEST_POSTGRES = $PostgresAdminConnectionString
        Write-Host 'PostgreSQL integration tests: enabled'
    } else {
        Write-Host 'PostgreSQL integration tests: SKIPPED (set CHATX_TEST_POSTGRES to enable)' -ForegroundColor Yellow
    }

    Invoke-Step 'dotnet tool restore' { dotnet tool restore }
    Invoke-Step 'dotnet restore' { dotnet restore Audorix.ChatX.slnx }
    Invoke-Step 'dotnet build' { dotnet build Audorix.ChatX.slnx --no-restore --configuration Release }
    Invoke-Step 'dotnet test' { dotnet test --solution Audorix.ChatX.slnx --no-build --configuration Release }

    if (-not $SkipFrontend) {
        Push-Location web
        try {
            Invoke-Step 'npm ci' { npm ci --no-audit --no-fund }
            Invoke-Step 'typecheck' { npm run typecheck }
            Invoke-Step 'frontend tests' { npm test }
            Invoke-Step 'frontend build' { npm run build }
        } finally { Pop-Location }
    }

    Write-Host "`nValidation passed." -ForegroundColor Green
} finally {
    Pop-Location
}
