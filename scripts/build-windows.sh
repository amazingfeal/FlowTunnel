#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${1:-$(tr -d '[:space:]' < "$ROOT/VERSION")}"
SING_BOX_VERSION="1.14.4"
SING_BOX_ZIP_SHA256="99185d75d314a07dbba4a130c8ad986cbeb629a07927975636b341ca61f991df"
SING_BOX_URL="https://github.com/SagerNet/sing-box/releases/download/v${SING_BOX_VERSION}/sing-box-${SING_BOX_VERSION}-windows-amd64.zip"
OUTPUT="$ROOT/artifacts/FlowTunnel-x64.msi"

if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Version must use MSI format MAJOR.MINOR.PATCH, for example 0.2.0." >&2
  exit 2
fi

for tool in curl unzip wixl shasum; do
  command -v "$tool" >/dev/null || { echo "Missing required tool: $tool" >&2; exit 2; }
done

DOTNET="${FLOWTUNNEL_DOTNET:-}"
if [[ -z "$DOTNET" ]] && command -v dotnet >/dev/null && dotnet --version >/dev/null 2>&1; then
  DOTNET="$(command -v dotnet)"
elif [[ -z "$DOTNET" ]] && [[ -x /private/tmp/flowtunnel-dotnet/dotnet ]]; then
  DOTNET=/private/tmp/flowtunnel-dotnet/dotnet
fi
if [[ -z "$DOTNET" ]]; then
  echo "Install .NET SDK 10 or set FLOWTUNNEL_DOTNET to its dotnet executable." >&2
  exit 2
fi

BUILD_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/flowtunnel-build.XXXXXX")"
trap 'rm -rf "$BUILD_ROOT"' EXIT
export DOTNET_CLI_HOME="$BUILD_ROOT/dotnet-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

ZIP="$BUILD_ROOT/sing-box.zip"
ENGINE_DIR="$BUILD_ROOT/engine"
DESKTOP_DIR="$BUILD_ROOT/desktop"
SERVICE_DIR="$BUILD_ROOT/service"
PAYLOAD_DIR="$BUILD_ROOT/payload"
mkdir -p "$ENGINE_DIR" "$DESKTOP_DIR" "$SERVICE_DIR" "$PAYLOAD_DIR" "$ROOT/artifacts"

echo "Downloading verified sing-box ${SING_BOX_VERSION}..."
curl -fL --retry 3 -o "$ZIP" "$SING_BOX_URL"
ACTUAL_ZIP_SHA256="$(shasum -a 256 "$ZIP" | awk '{print $1}')"
if [[ "$ACTUAL_ZIP_SHA256" != "$SING_BOX_ZIP_SHA256" ]]; then
  echo "sing-box archive hash mismatch: $ACTUAL_ZIP_SHA256" >&2
  exit 1
fi
unzip -q "$ZIP" -d "$ENGINE_DIR"
ENGINE_EXE="$(find "$ENGINE_DIR" -type f -name sing-box.exe -print -quit)"
ENGINE_LICENSE="$(find "$ENGINE_DIR" -type f -name LICENSE -print -quit)"
[[ -n "$ENGINE_EXE" && -n "$ENGINE_LICENSE" ]] || { echo "Invalid sing-box archive." >&2; exit 1; }
cp "$ENGINE_EXE" "$PAYLOAD_DIR/sing-box.exe"
cp "$ENGINE_LICENSE" "$PAYLOAD_DIR/sing-box-LICENSE.txt"
ENGINE_SHA256="$(shasum -a 256 "$PAYLOAD_DIR/sing-box.exe" | awk '{print $1}')"
printf '{\n  "version": "%s",\n  "sha256": "%s"\n}\n' "$SING_BOX_VERSION" "$ENGINE_SHA256" > "$PAYLOAD_DIR/engine.json"

echo "Running tests..."
for project in \
  "$ROOT/tests/FlowTunnel.Core.Tests/FlowTunnel.Core.Tests.csproj" \
  "$ROOT/tests/FlowTunnel.Application.Tests/FlowTunnel.Application.Tests.csproj" \
  "$ROOT/tests/FlowTunnel.Networking.Tests/FlowTunnel.Networking.Tests.csproj"; do
  "$DOTNET" test "$project" -c Release -p:EnableWindowsTargeting=true
done

COMMON_PUBLISH=(
  -c Release -r win-x64 --self-contained true
  -p:EnableWindowsTargeting=true
  -p:PublishSingleFile=true
  -p:EnableCompressionInSingleFile=true
  -p:IncludeNativeLibrariesForSelfExtract=true
  -p:DebugType=None
)

echo "Publishing Windows executables..."
"$DOTNET" publish "$ROOT/src/FlowTunnel.Desktop/FlowTunnel.Desktop.csproj" "${COMMON_PUBLISH[@]}" -o "$DESKTOP_DIR"
"$DOTNET" publish "$ROOT/src/FlowTunnel.Service/FlowTunnel.Service.csproj" "${COMMON_PUBLISH[@]}" -o "$SERVICE_DIR"

echo "Building FlowTunnel ${VERSION} MSI..."
wixl -a x64 \
  -D ProductVersion="$VERSION" \
  -D DesktopExe="$DESKTOP_DIR/FlowTunnel.Desktop.exe" \
  -D ServiceExe="$SERVICE_DIR/FlowTunnel.Service.exe" \
  -D SingBoxExe="$PAYLOAD_DIR/sing-box.exe" \
  -D EngineMetadata="$PAYLOAD_DIR/engine.json" \
  -D SingBoxLicense="$PAYLOAD_DIR/sing-box-LICENSE.txt" \
  -o "$OUTPUT" "$ROOT/installer/Package.wixl.wxs"

shasum -a 256 "$OUTPUT" > "$OUTPUT.sha256"
echo "Built: $OUTPUT"
cat "$OUTPUT.sha256"
