#!/usr/bin/env bash
#
# Wraps a built SB-Mac.app into a drag-to-install .dmg.
#
#   ./build/make-dmg.sh <path-to-.app> <output.dmg> [volume-name]
#
set -euo pipefail

APP_PATH="${1:?usage: make-dmg.sh <path-to-.app> <output.dmg> [volume-name]}"
DMG_PATH="${2:?usage: make-dmg.sh <path-to-.app> <output.dmg> [volume-name]}"
VOLUME_NAME="${3:-SB-Mac}"

if [[ ! -d "$APP_PATH" ]]; then
  echo "error: $APP_PATH does not exist. Run build/make-app.sh first." >&2
  exit 1
fi

STAGING="$(mktemp -d)"
trap 'rm -rf "$STAGING"' EXIT

cp -R "$APP_PATH" "$STAGING/"

# The /Applications symlink is what makes the window a drag-to-install target.
ln -s /Applications "$STAGING/Applications"

rm -f "$DMG_PATH"
mkdir -p "$(dirname "$DMG_PATH")"

# UDZO is zlib-compressed and read-only, which is what a distributable disk image wants.
hdiutil create \
  -volname "$VOLUME_NAME" \
  -srcfolder "$STAGING" \
  -ov \
  -format UDZO \
  "$DMG_PATH" >/dev/null

echo "Built: $DMG_PATH"
