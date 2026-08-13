#!/usr/bin/env bash
#
# Builds SB-Mac.app — a self-contained macOS application bundle.
#
# The published output is self-contained, so the .app runs on a Mac with no .NET
# runtime installed. Pass the architecture as the first argument; it defaults to
# whatever this machine is.
#
#   ./build/make-app.sh              # native architecture
#   ./build/make-app.sh osx-arm64    # Apple Silicon
#   ./build/make-app.sh osx-x64      # Intel
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

# Default to this machine's architecture so a plain run does the obvious thing.
if [[ $# -ge 1 ]]; then
  RUNTIME="$1"
elif [[ "$(uname -m)" == "arm64" ]]; then
  RUNTIME="osx-arm64"
else
  RUNTIME="osx-x64"
fi

APP_NAME="SB-Mac"
BUNDLE="$REPO_ROOT/artifacts/$APP_NAME.app"
PUBLISH_DIR="$REPO_ROOT/artifacts/publish-$RUNTIME"

echo "==> Publishing for $RUNTIME"
rm -rf "$PUBLISH_DIR" "$BUNDLE"

dotnet publish src/SbMac.App/SbMac.App.csproj \
  --configuration Release \
  --runtime "$RUNTIME" \
  --self-contained true \
  --output "$PUBLISH_DIR" \
  -p:PublishSingleFile=false \
  -p:DebugType=none \
  --nologo

echo "==> Assembling $APP_NAME.app"
mkdir -p "$BUNDLE/Contents/MacOS" "$BUNDLE/Contents/Resources"

cp "$REPO_ROOT/build/Info.plist" "$BUNDLE/Contents/Info.plist"
cp -R "$PUBLISH_DIR/." "$BUNDLE/Contents/MacOS/"

if [[ -f "$REPO_ROOT/build/AppIcon.icns" ]]; then
  cp "$REPO_ROOT/build/AppIcon.icns" "$BUNDLE/Contents/Resources/AppIcon.icns"
else
  # Without an icon file the bundle still runs; macOS just shows the generic app icon.
  echo "    (no build/AppIcon.icns — using the system default icon)"
fi

chmod +x "$BUNDLE/Contents/MacOS/SbMac"

# Apple Silicon refuses to execute a Mach-O that carries no signature at all, and
# copying the published apphost into the bundle invalidates the one the SDK applied.
# Re-signing ad-hoc is therefore required for the app to launch locally.
#
# It does NOT make the bundle pass Gatekeeper: an ad-hoc signature has no Developer ID
# behind it, so a downloaded (quarantined) copy is still rejected. Clearing quarantine
# is what makes a downloaded build run — see the install notes in README.md.
echo "==> Signing (ad-hoc)"
codesign --force --deep --sign - "$BUNDLE"

# Freshly built bundles inherit no quarantine flag, but a copied or downloaded one
# will; clearing it here keeps `open` working after the bundle is moved around.
xattr -cr "$BUNDLE" 2>/dev/null || true

# Sanity-check the bits that actually determine whether this thing runs.
#
# `codesign --verify` is deliberately not used: .NET puts its managed assemblies and
# config files alongside the host in Contents/MacOS, and codesign treats everything in
# that directory as nested code, so it reports the unsigned .dll/.json files as broken
# subcomponents even though the bundle launches. What matters is that the host binary
# is Mach-O and carries a signature.
echo "==> Verifying"
EXECUTABLE="$BUNDLE/Contents/MacOS/SbMac"

file "$EXECUTABLE" | grep -q 'Mach-O' \
  || { echo "error: $EXECUTABLE is not a Mach-O executable" >&2; exit 1; }

# codesign exits non-zero here because of the nested-code complaint described above, so
# its output is captured first — piping it straight into grep would let that exit status
# fail the check under `set -o pipefail` even when the signature is present.
SIGNATURE_INFO="$(codesign -dv "$BUNDLE" 2>&1 || true)"

grep -q 'Signature=adhoc' <<<"$SIGNATURE_INFO" \
  || { echo "error: the bundle carries no ad-hoc signature" >&2; echo "$SIGNATURE_INFO" >&2; exit 1; }

/usr/libexec/PlistBuddy -c 'Print CFBundleIdentifier' "$BUNDLE/Contents/Info.plist" >/dev/null \
  || { echo "error: Info.plist is missing or malformed" >&2; exit 1; }

echo
echo "Built: $BUNDLE"
echo "Run it with:  open '$BUNDLE'"
