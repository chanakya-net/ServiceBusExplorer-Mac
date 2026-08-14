#!/usr/bin/env bash

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION_SCRIPT="$REPO_ROOT/build/next-release-version.sh"

if [[ ! -f "$VERSION_SCRIPT" ]]; then
  echo "FAIL: missing build/next-release-version.sh" >&2
  exit 1
fi

assert_version() {
  local latest="$1"
  local expected="$2"
  local actual

  actual="$(bash "$VERSION_SCRIPT" "$latest")"
  if [[ "$actual" != "$expected" ]]; then
    echo "FAIL: after $latest expected $expected, got $actual" >&2
    exit 1
  fi
}

assert_version v1.3.0 v1.3.1
assert_version v2.9.99 v2.9.100
assert_version v0.0.0 v0.0.1
assert_version v1.2.9223372036854775807 v1.2.9223372036854775808

assert_rejected() {
  local tag="$1"

  if bash "$VERSION_SCRIPT" "$tag" >/dev/null 2>&1; then
    echo "FAIL: malformed release tag '$tag' was accepted" >&2
    exit 1
  fi
}

assert_rejected release-1.3.0
assert_rejected v1.08.0
assert_rejected v01.3.0

echo "Release version tests passed"
