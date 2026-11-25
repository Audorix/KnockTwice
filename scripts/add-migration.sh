#!/usr/bin/env bash
# Generates a migration with the same name for BOTH database providers (no database is opened).
#   ./scripts/add-migration.sh AddUserProfiles
set -euo pipefail
cd "$(dirname "$0")/.."
name="${1:?Usage: add-migration.sh <MigrationName>}"
[[ "$name" =~ ^[A-Za-z][A-Za-z0-9_]*$ ]] || { echo "Invalid migration name: $name" >&2; exit 1; }

dotnet tool restore
for project in Audorix.KnockTwice.Persistence.Sqlite Audorix.KnockTwice.Persistence.PostgreSql; do
  path="src/Infrastructure/$project"
  echo "==> $project"
  dotnet ef migrations add "$name" --project "$path" --startup-project "$path" --output-dir Migrations
done
