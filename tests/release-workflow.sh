#!/usr/bin/env bash

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$REPO_ROOT/.github/workflows/release.yml"

assert_line() {
  local pattern="$1"
  local message="$2"

  if ! grep -Eq "$pattern" "$WORKFLOW"; then
    echo "FAIL: $message" >&2
    exit 1
  fi
}

assert_line '^    branches: \[main\]$' 'release workflow must run for pushes to main'
assert_line '^  queue: max$' 'release workflow must retain overlapping main pushes'
assert_line 'ref: \$\{\{ github\.event\.inputs\.tag \|\| github\.sha \}\}' \
  'release workflow must pin the triggering commit'

if grep -Eq '^    tags:' "$WORKFLOW"; then
  echo 'FAIL: release tag pushes would recursively trigger another release' >&2
  exit 1
fi

echo 'Release workflow tests passed'
