#!/usr/bin/env bash

# Prints the patch release after an existing stable vMAJOR.MINOR.PATCH tag.
set -euo pipefail

LATEST_TAG="${1:-}"
if [[ ! "$LATEST_TAG" =~ ^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]]; then
  echo "error: expected a stable release tag such as v1.3.0" >&2
  exit 1
fi

MAJOR="${BASH_REMATCH[1]}"
MINOR="${BASH_REMATCH[2]}"
PATCH="${BASH_REMATCH[3]}"

increment_decimal() {
  local value="$1"
  local result=''
  local carry=1
  local index digit

  for ((index = ${#value} - 1; index >= 0; index--)); do
    digit="${value:index:1}"
    if ((carry)); then
      if [[ "$digit" == 9 ]]; then
        digit=0
      else
        digit="$((digit + 1))"
        carry=0
      fi
    fi
    result="$digit$result"
  done

  if ((carry)); then
    result="1$result"
  fi

  printf '%s' "$result"
}

printf 'v%s.%s.%s\n' "$MAJOR" "$MINOR" "$(increment_decimal "$PATCH")"
