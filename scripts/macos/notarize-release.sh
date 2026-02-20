#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
APP_BUNDLE_DIR="${APP_BUNDLE_DIR:-$ROOT_DIR/artifacts/macos/KnobForge.app}"
PKG_PATH="${PKG_PATH:-}"
OUTPUT_DIR="${OUTPUT_DIR:-$ROOT_DIR/artifacts/macos}"
NOTARIZE_TARGET="${NOTARIZE_TARGET:-app}" # app or pkg
NOTARYTOOL_PROFILE="${NOTARYTOOL_PROFILE:-}"
APPLE_ID="${APPLE_ID:-}"
APPLE_TEAM_ID="${APPLE_TEAM_ID:-}"
APPLE_APP_PASSWORD="${APPLE_APP_PASSWORD:-}"

if ! command -v xcrun >/dev/null 2>&1; then
  echo "ERROR: xcrun not found. Install Xcode command line tools." >&2
  exit 1
fi

if [[ -z "$NOTARYTOOL_PROFILE" ]]; then
  if [[ -z "$APPLE_ID" || -z "$APPLE_TEAM_ID" || -z "$APPLE_APP_PASSWORD" ]]; then
    echo "ERROR: notarization credentials missing." >&2
    echo "Provide NOTARYTOOL_PROFILE, or APPLE_ID + APPLE_TEAM_ID + APPLE_APP_PASSWORD." >&2
    exit 1
  fi
fi

mkdir -p "$OUTPUT_DIR"

submit_for_notary() {
  local file_path="$1"
  if [[ -n "$NOTARYTOOL_PROFILE" ]]; then
    xcrun notarytool submit "$file_path" --keychain-profile "$NOTARYTOOL_PROFILE" --wait
  else
    xcrun notarytool submit \
      "$file_path" \
      --apple-id "$APPLE_ID" \
      --team-id "$APPLE_TEAM_ID" \
      --password "$APPLE_APP_PASSWORD" \
      --wait
  fi
}

case "$NOTARIZE_TARGET" in
  app)
    if [[ ! -d "$APP_BUNDLE_DIR" ]]; then
      echo "ERROR: app bundle not found at $APP_BUNDLE_DIR" >&2
      exit 1
    fi
    ZIP_PATH="$OUTPUT_DIR/KnobForge-notary.zip"
    rm -f "$ZIP_PATH"
    echo ">>> Creating zip for notarization: $ZIP_PATH"
    ditto -c -k --keepParent "$APP_BUNDLE_DIR" "$ZIP_PATH"
    echo ">>> Submitting app zip for notarization"
    submit_for_notary "$ZIP_PATH"
    echo ">>> Stapling notarization ticket to app bundle"
    xcrun stapler staple "$APP_BUNDLE_DIR"
    xcrun stapler validate "$APP_BUNDLE_DIR"
    ;;
  pkg)
    if [[ -z "$PKG_PATH" ]]; then
      echo "ERROR: PKG_PATH is required when NOTARIZE_TARGET=pkg" >&2
      exit 1
    fi
    if [[ ! -f "$PKG_PATH" ]]; then
      echo "ERROR: pkg not found at $PKG_PATH" >&2
      exit 1
    fi
    echo ">>> Submitting installer pkg for notarization"
    submit_for_notary "$PKG_PATH"
    echo ">>> Stapling notarization ticket to installer pkg"
    xcrun stapler staple "$PKG_PATH"
    xcrun stapler validate "$PKG_PATH"
    ;;
  *)
    echo "ERROR: unsupported NOTARIZE_TARGET '$NOTARIZE_TARGET'. Use 'app' or 'pkg'." >&2
    exit 1
    ;;
esac

echo ">>> Notarization flow completed."
