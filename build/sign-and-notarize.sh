#!/usr/bin/env bash
#
# Re-signs an already-built SB-Mac.app with a Developer ID identity, notarizes it with
# Apple, and staples the ticket. Replaces the ad-hoc signature from make-app.sh.
#
#   ./build/sign-and-notarize.sh <path-to-.app>
#
# Reads its configuration from the environment so it works identically in CI and locally:
#
#   APPLE_SIGNING_IDENTITY   e.g. "Developer ID Application: Jane Doe (ABCDE12345)"
#   APPLE_ID                 the Apple ID email used for notarization
#   APPLE_TEAM_ID            10-character team identifier
#   APPLE_APP_PASSWORD       an app-specific password, NOT the account password
#
# Everything here is a no-op for ad-hoc builds; release.yml only calls this when the
# signing secrets are present.
#
set -euo pipefail

APP_PATH="${1:?usage: sign-and-notarize.sh <path-to-.app>}"

: "${APPLE_SIGNING_IDENTITY:?APPLE_SIGNING_IDENTITY is not set}"
: "${APPLE_ID:?APPLE_ID is not set}"
: "${APPLE_TEAM_ID:?APPLE_TEAM_ID is not set}"
: "${APPLE_APP_PASSWORD:?APPLE_APP_PASSWORD is not set}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENTITLEMENTS="$REPO_ROOT/build/entitlements.plist"

echo "==> Signing nested binaries"
# Signatures must be applied innermost-first: signing the bundle seals its contents, so a
# nested binary signed afterwards invalidates the outer signature. A self-contained .NET
# publish puts its native libraries next to the host, so they are signed individually.
find "$APP_PATH/Contents/MacOS" -type f \( -name "*.dylib" -o -name "*.so" \) -print0 \
  | xargs -0 -r -n1 codesign \
      --force \
      --timestamp \
      --options runtime \
      --entitlements "$ENTITLEMENTS" \
      --sign "$APPLE_SIGNING_IDENTITY"

echo "==> Signing the bundle"
codesign \
  --force \
  --timestamp \
  --options runtime \
  --entitlements "$ENTITLEMENTS" \
  --sign "$APPLE_SIGNING_IDENTITY" \
  "$APP_PATH"

echo "==> Verifying the Developer ID signature"
codesign --verify --verbose=2 "$APP_PATH"

echo "==> Submitting to Apple for notarization"
# notarytool takes a zip or dmg, not a bundle directory. ditto preserves the symlinks and
# extended attributes that a plain `zip` mangles, which would invalidate the signature.
NOTARIZE_ZIP="$(mktemp -d)/SB-Mac-notarize.zip"
ditto -c -k --sequesterRsrc --keepParent "$APP_PATH" "$NOTARIZE_ZIP"

# --wait blocks until Apple returns a verdict; without it the staple below would race.
xcrun notarytool submit "$NOTARIZE_ZIP" \
  --apple-id "$APPLE_ID" \
  --team-id "$APPLE_TEAM_ID" \
  --password "$APPLE_APP_PASSWORD" \
  --wait

echo "==> Stapling the ticket"
# Stapling attaches the ticket to the bundle so first launch works offline. Without it a
# user with no network sees the same "unidentified developer" rejection.
xcrun stapler staple "$APP_PATH"

echo "==> Final Gatekeeper assessment"
# This is the check that matters: it is what macOS runs on a quarantined download.
spctl --assess --type execute --verbose=2 "$APP_PATH"

echo
echo "Notarized: $APP_PATH"
