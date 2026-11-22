<#
.SYNOPSIS
  Generates a migration with the same name for BOTH database providers.

.DESCRIPTION
  Each persistence project is its own design-time startup project, so generating the
  SQLite migration needs no PostgreSQL server and vice versa. No database is opened.
  Review both generated migrations before committing; they are provider-specific.

.EXAMPLE
  ./scripts/add-migration.ps1 -Name AddUserProfiles
#>
[CmdletBinding()]
param([Parameter(Mandatory)] [ValidatePattern('^[A-Za-z][A-Za-z0-9_]*$')] [string] $Name)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet tool restore
    foreach ($project in 'Audorix.ChatX.Persistence.Sqlite', 'Audorix.ChatX.Persistence.PostgreSql') {
        $path = "src/Infrastructure/$project"
        Write-Host "==> $project" -ForegroundColor Cyan
        dotnet ef migrations add $Name --project $path --startup-project $path --output-dir Migrations
        if ($LASTEXITCODE -ne 0) { throw "Migration generation failed for $project" }
    }
} finally {
    Pop-Location
}
