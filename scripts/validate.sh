#!/usr/bin/env bash
# Reproducible local validation for Knock Twice (no CI provider is assumed).
# PostgreSQL integration tests run only when CHATX_TEST_POSTGRES is set; they create and
# drop uniquely named chatx_test_* databases and touch nothing else.
#   ./scripts/validate.sh
#   CHATX_TEST_POSTGRES="Host=localhost;Username=chatx_test;Password=..." ./scripts/validate.sh
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ -n "${CHATX_TEST_POSTGRES:-}" ]]; then
  echo "PostgreSQL integration tests: enabled"
else
  echo "PostgreSQL integration tests: SKIPPED (set CHATX_TEST_POSTGRES to enable)"
fi

step() { echo; echo "==> $1"; shift; "$@"; }

step "dotnet tool restore" dotnet tool restore
step "dotnet restore" dotnet restore Audorix.KnockTwice.slnx
step "dotnet build" dotnet build Audorix.KnockTwice.slnx --no-restore --configuration Release
step "dotnet test" dotnet test --solution Audorix.KnockTwice.slnx --no-build --configuration Release

if [[ "${SKIP_FRONTEND:-0}" != "1" ]]; then
  pushd web > /dev/null
  step "npm ci" npm ci --no-audit --no-fund
  step "typecheck" npm run typecheck
  step "frontend tests" npm test
  step "frontend build" npm run build
  popd > /dev/null
fi

echo; echo "Validation passed."
