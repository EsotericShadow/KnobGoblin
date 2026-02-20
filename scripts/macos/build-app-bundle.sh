#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT_PATH="$ROOT_DIR/KnobForge.App/KnobForge.App.csproj"
INFO_PLIST_TEMPLATE="$ROOT_DIR/KnobForge.App/Info.plist"
PLIST_BUDDY="/usr/libexec/PlistBuddy"

CONFIGURATION="${CONFIGURATION:-Release}"
TARGET_FRAMEWORK="${TARGET_FRAMEWORK:-net8.0}"
RID="${RID:-}"
RID_SEGMENT="${RID:-current}"
APP_VERSION="${APP_VERSION:-1.0.0}"
BUILD_NUMBER="${BUILD_NUMBER:-1}"
BUNDLE_IDENTIFIER="${BUNDLE_IDENTIFIER:-com.knobforge.app}"
PUBLISH_DIR="${PUBLISH_DIR:-$ROOT_DIR/artifacts/macos/publish/$RID_SEGMENT}"
APP_BUNDLE_DIR="${APP_BUNDLE_DIR:-$ROOT_DIR/artifacts/macos/KnobForge.app}"
MACOS_EXECUTABLE_NAME="KnobForge"
PUBLISHED_APPHOST_NAME="KnobForge.App"
APP_ICON_SOURCE="${APP_ICON_SOURCE:-$ROOT_DIR/icon.ico}"
APP_ICON_FILENAME="${APP_ICON_FILENAME:-KnobForge.icns}"
RESTORE="${RESTORE:-1}"
CODESIGN_ENABLED="${CODESIGN_ENABLED:-1}"
APP_SIGN_IDENTITY="${APP_SIGN_IDENTITY:--}"
ENABLE_HARDENED_RUNTIME="${ENABLE_HARDENED_RUNTIME:-1}"
DEFAULT_ENTITLEMENTS_PATH="$ROOT_DIR/KnobForge.App/entitlements.macos.plist"
ENTITLEMENTS_PATH="${ENTITLEMENTS_PATH:-$DEFAULT_ENTITLEMENTS_PATH}"
REGISTER_LAUNCH_SERVICES="${REGISTER_LAUNCH_SERVICES:-1}"

set_plist_string() {
  local plist_file="$1"
  local key_path="$2"
  local raw_value="$3"
  local escaped_value="${raw_value//\"/\\\"}"

  if "$PLIST_BUDDY" -c "Set :$key_path \"$escaped_value\"" "$plist_file" >/dev/null 2>&1; then
    return 0
  fi

  "$PLIST_BUDDY" -c "Add :$key_path string \"$escaped_value\"" "$plist_file"
}

generate_app_icon() {
  local destination="$1"

  if [[ ! -f "$APP_ICON_SOURCE" ]]; then
    echo ">>> Icon source not found at $APP_ICON_SOURCE. Bundle will use default app icon."
    return 0
  fi

  local width
  width="$(sips -g pixelWidth "$APP_ICON_SOURCE" 2>/dev/null | awk '/pixelWidth:/ {print $2}' || true)"
  if [[ -n "$width" ]] && [[ "$width" -lt 1024 ]]; then
    echo ">>> WARNING: icon source is ${width}px wide; 1024px is recommended for crisp macOS icons."
  fi

  if command -v sips >/dev/null 2>&1 && command -v iconutil >/dev/null 2>&1; then
    local temp_dir
    temp_dir="$(mktemp -d)"
    local iconset_dir="$temp_dir/KnobForge.iconset"
    mkdir -p "$iconset_dir"

    local icon_sizes=(
      "icon_16x16.png:16"
      "icon_16x16@2x.png:32"
      "icon_32x32.png:32"
      "icon_32x32@2x.png:64"
      "icon_128x128.png:128"
      "icon_128x128@2x.png:256"
      "icon_256x256.png:256"
      "icon_256x256@2x.png:512"
      "icon_512x512.png:512"
      "icon_512x512@2x.png:1024"
    )

    local entry
    for entry in "${icon_sizes[@]}"; do
      local filename="${entry%%:*}"
      local size="${entry##*:}"
      sips -s format png -z "$size" "$size" "$APP_ICON_SOURCE" --out "$iconset_dir/$filename" >/dev/null
    done

    if iconutil --convert icns --output "$destination" "$iconset_dir" >/dev/null 2>&1; then
      rm -rf "$temp_dir"
      echo ">>> App icon generated with iconutil: $destination"
      return 0
    fi

    rm -rf "$temp_dir"
    echo ">>> WARNING: iconutil conversion failed. Falling back to Pillow-based icon generation."
  fi

  if command -v python3 >/dev/null 2>&1; then
    if python3 - "$APP_ICON_SOURCE" "$destination" <<'PY'
from PIL import Image
import sys

src = sys.argv[1]
dst = sys.argv[2]
img = Image.open(src)
img.save(dst, format="ICNS", sizes=[(16, 16), (32, 32), (64, 64), (128, 128), (256, 256), (512, 512), (1024, 1024)])
PY
    then
      echo ">>> App icon generated with Pillow: $destination"
      return 0
    fi
  fi

  echo ">>> WARNING: Unable to generate app icon from $APP_ICON_SOURCE."
}

echo ">>> Publishing KnobForge ($CONFIGURATION, $TARGET_FRAMEWORK, RID=${RID_SEGMENT})"
publish_args=(
  "$PROJECT_PATH"
  -c "$CONFIGURATION"
  -f "$TARGET_FRAMEWORK"
  --self-contained false
  -o "$PUBLISH_DIR"
)
if [[ -n "$RID" ]]; then
  publish_args+=(-r "$RID")
fi
if [[ "$RESTORE" != "1" ]]; then
  publish_args+=(--no-restore)
fi
dotnet publish "${publish_args[@]}"

required_publish_files=(
  "Avalonia.Themes.Fluent.dll"
  "Avalonia.Controls.ColorPicker.dll"
)
for required_file in "${required_publish_files[@]}"; do
  if [[ ! -f "$PUBLISH_DIR/$required_file" ]]; then
    echo "ERROR: publish output missing required assembly: $required_file" >&2
    echo "Hint: run with RESTORE=1 (default) and confirm package references." >&2
    exit 1
  fi
done

if [[ ! -f "$INFO_PLIST_TEMPLATE" ]]; then
  echo "ERROR: missing Info.plist template at $INFO_PLIST_TEMPLATE" >&2
  exit 1
fi

rm -rf "$APP_BUNDLE_DIR"
mkdir -p "$APP_BUNDLE_DIR/Contents/MacOS"
mkdir -p "$APP_BUNDLE_DIR/Contents/Resources/publish"

cp -R "$PUBLISH_DIR"/. "$APP_BUNDLE_DIR/Contents/Resources/publish/"
cp "$INFO_PLIST_TEMPLATE" "$APP_BUNDLE_DIR/Contents/Info.plist"

cat > "$APP_BUNDLE_DIR/Contents/MacOS/$MACOS_EXECUTABLE_NAME" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
APP_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PUBLISH_DIR="$APP_ROOT/Resources/publish"
APPHOST="$PUBLISH_DIR/KnobForge.App"

if [[ ! -x "$APPHOST" ]]; then
  chmod +x "$APPHOST" 2>/dev/null || true
fi

exec "$APPHOST" "$@"
SH

set_plist_string "$APP_BUNDLE_DIR/Contents/Info.plist" "CFBundleIdentifier" "$BUNDLE_IDENTIFIER"
set_plist_string "$APP_BUNDLE_DIR/Contents/Info.plist" "CFBundleVersion" "$BUILD_NUMBER"
set_plist_string "$APP_BUNDLE_DIR/Contents/Info.plist" "CFBundleShortVersionString" "$APP_VERSION"
set_plist_string "$APP_BUNDLE_DIR/Contents/Info.plist" "CFBundleIconFile" "$APP_ICON_FILENAME"
set_plist_string "$APP_BUNDLE_DIR/Contents/Info.plist" "CFBundleIconName" "${APP_ICON_FILENAME%.icns}"
set_plist_string "$APP_BUNDLE_DIR/Contents/Info.plist" "CFBundleDocumentTypes:0:CFBundleTypeIconFile" "$APP_ICON_FILENAME"
set_plist_string "$APP_BUNDLE_DIR/Contents/Info.plist" "UTExportedTypeDeclarations:0:UTTypeIconFile" "$APP_ICON_FILENAME"

generate_app_icon "$APP_BUNDLE_DIR/Contents/Resources/$APP_ICON_FILENAME"

chmod +x "$APP_BUNDLE_DIR/Contents/MacOS/$MACOS_EXECUTABLE_NAME"

if [[ -x "$APP_BUNDLE_DIR/Contents/Resources/publish/$PUBLISHED_APPHOST_NAME" ]]; then
  true
else
  chmod +x "$APP_BUNDLE_DIR/Contents/Resources/publish/$PUBLISHED_APPHOST_NAME" 2>/dev/null || true
fi

LSREGISTER="/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister"
if [[ "$REGISTER_LAUNCH_SERVICES" == "1" ]] && [[ -x "$LSREGISTER" ]]; then
  echo ">>> Registering app bundle with LaunchServices"
  "$LSREGISTER" -f "$APP_BUNDLE_DIR" >/dev/null 2>&1 || true
fi

if [[ "$CODESIGN_ENABLED" == "1" ]]; then
  if [[ "$APP_SIGN_IDENTITY" == "-" ]]; then
    echo ">>> Applying ad-hoc codesign"
    codesign --force --deep --sign - "$APP_BUNDLE_DIR"
  else
    echo ">>> Signing app bundle with identity: $APP_SIGN_IDENTITY"
    sign_args=(
      --force
      --deep
      --sign "$APP_SIGN_IDENTITY"
      --timestamp
    )
    if [[ "$ENABLE_HARDENED_RUNTIME" == "1" ]]; then
      sign_args+=(--options runtime)
    fi
    if [[ -n "$ENTITLEMENTS_PATH" ]]; then
      if [[ ! -f "$ENTITLEMENTS_PATH" ]]; then
        echo "ERROR: entitlements file not found at $ENTITLEMENTS_PATH" >&2
        exit 1
      fi
      sign_args+=(--entitlements "$ENTITLEMENTS_PATH")
    fi

    codesign "${sign_args[@]}" "$APP_BUNDLE_DIR"
  fi

  echo ">>> Verifying app signature"
  codesign --verify --deep --strict --verbose=2 "$APP_BUNDLE_DIR"
fi

echo ">>> App bundle ready at: $APP_BUNDLE_DIR"
echo ">>> You can now set .knob files to open with this app in Finder."
