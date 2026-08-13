#!/usr/bin/env bash
#
# Builds SB-Mac.app — a self-contained macOS application bundle.
#
# The published output is self-contained, so the .app runs on a Mac with no .NET
# runtime installed. Pass the architecture as the first argument; it defaults to
# whatever this machine is. The second argument is the version to stamp into the
# bundle, which is what Finder's Get Info and the About panel report.
#
#   ./build/make-app.sh                        # native architecture, unversioned
#   ./build/make-app.sh osx-arm64              # Apple Silicon
#   ./build/make-app.sh osx-x64 v1.2.0         # Intel, stamped as 1.2.0
#   SBMAC_VERSION=v1.2.0 ./build/make-app.sh   # same, via the environment
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

# The version to stamp, in order of preference: the argument, the environment, the
# tag this checkout sits exactly on. An untagged build reports 0.0.0 rather than
# borrowing the last release's number, because claiming to be a release it isn't is
# how a bundle ends up lying about what's inside it.
if [[ $# -ge 2 ]]; then
  VERSION="$2"
elif [[ -n "${SBMAC_VERSION:-}" ]]; then
  VERSION="$SBMAC_VERSION"
else
  VERSION="$(git describe --tags --exact-match 2>/dev/null || echo '0.0.0')"
fi

# Tags are written v1.2.0; CFBundleShortVersionString wants 1.2.0. Apple also requires
# both version keys to be one to three dot-separated integers, so a pre-release suffix
# is dropped here — the release assets carry the full tag in their filenames.
REQUESTED_VERSION="$VERSION"
VERSION="${VERSION#v}"
VERSION="${VERSION%%-*}"
VERSION="${VERSION%%+*}"

if [[ ! "$VERSION" =~ ^[0-9]+(\.[0-9]+){0,2}$ ]]; then
  echo "warning: '$REQUESTED_VERSION' is not a usable bundle version — stamping 0.0.0 instead" >&2
  VERSION="0.0.0"
fi

APP_NAME="SB-Mac"
BUNDLE="$REPO_ROOT/artifacts/$APP_NAME.app"
PUBLISH_DIR="$REPO_ROOT/artifacts/publish-$RUNTIME"

echo "==> Publishing for $RUNTIME (version $VERSION)"
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

# Stamped here rather than committed into build/Info.plist, so the version can't drift
# from the tag being built. This must happen before codesign below: editing Info.plist
# after signing invalidates the signature.
/usr/libexec/PlistBuddy \
  -c "Set :CFBundleShortVersionString $VERSION" \
  -c "Set :CFBundleVersion $VERSION" \
  "$BUNDLE/Contents/Info.plist" >/dev/null

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

# A bundle that reports the wrong version is the kind of thing nobody notices until a
# user reports a bug against a release that never shipped the code they're running.
STAMPED="$(/usr/libexec/PlistBuddy -c 'Print CFBundleShortVersionString' "$BUNDLE/Contents/Info.plist")"

[[ "$STAMPED" == "$VERSION" ]] \
  || { echo "error: bundle reports version '$STAMPED', expected '$VERSION'" >&2; exit 1; }

echo
echo "Built: $BUNDLE (version $VERSION)"
echo "Run it with:  open '$BUNDLE'"
