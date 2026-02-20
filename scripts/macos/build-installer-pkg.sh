#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
APP_BUNDLE_DIR="${APP_BUNDLE_DIR:-$ROOT_DIR/artifacts/macos/KnobForge.app}"
OUTPUT_DIR="${OUTPUT_DIR:-$ROOT_DIR/artifacts/macos}"
PLIST_BUDDY="/usr/libexec/PlistBuddy"
INSTALL_LOCATION="${INSTALL_LOCATION:-/Applications}"
BUILD_APP_IF_MISSING="${BUILD_APP_IF_MISSING:-1}"
INSTALLER_SIGN_IDENTITY="${INSTALLER_SIGN_IDENTITY:-}"
PKG_IDENTIFIER="${PKG_IDENTIFIER:-com.knobforge.app.pkg}"

if [[ ! -d "$APP_BUNDLE_DIR" ]]; then
  if [[ "$BUILD_APP_IF_MISSING" == "1" ]]; then
    echo ">>> App bundle not found. Building it first."
    bash "$ROOT_DIR/scripts/macos/build-app-bundle.sh"
  else
    echo "ERROR: app bundle not found at $APP_BUNDLE_DIR" >&2
    exit 1
  fi
fi

INFO_PLIST="$APP_BUNDLE_DIR/Contents/Info.plist"
if [[ ! -f "$INFO_PLIST" ]]; then
  echo "ERROR: Info.plist not found in bundle: $INFO_PLIST" >&2
  exit 1
fi

APP_VERSION="$("$PLIST_BUDDY" -c "Print :CFBundleShortVersionString" "$INFO_PLIST" 2>/dev/null || echo "1.0.0")"
BUILD_NUMBER="$("$PLIST_BUDDY" -c "Print :CFBundleVersion" "$INFO_PLIST" 2>/dev/null || echo "1")"
PKG_BASENAME="KnobForge-${APP_VERSION}.pkg"
UNSIGNED_PKG="$OUTPUT_DIR/KnobForge-${APP_VERSION}-unsigned.pkg"
FINAL_PKG="$OUTPUT_DIR/$PKG_BASENAME"

mkdir -p "$OUTPUT_DIR"
rm -f "$UNSIGNED_PKG" "$FINAL_PKG"

echo ">>> Building component package"
pkgbuild \
  --identifier "$PKG_IDENTIFIER" \
  --version "$BUILD_NUMBER" \
  --install-location "$INSTALL_LOCATION" \
  --component "$APP_BUNDLE_DIR" \
  "$UNSIGNED_PKG"

if [[ -n "$INSTALLER_SIGN_IDENTITY" ]]; then
  echo ">>> Signing installer package with identity: $INSTALLER_SIGN_IDENTITY"
  productsign \
    --timestamp \
    --sign "$INSTALLER_SIGN_IDENTITY" \
    "$UNSIGNED_PKG" \
    "$FINAL_PKG"
  rm -f "$UNSIGNED_PKG"
else
  mv "$UNSIGNED_PKG" "$FINAL_PKG"
fi

echo ">>> Verifying installer package signature"
pkgutil --check-signature "$FINAL_PKG" || true

echo ">>> Installer package ready: $FINAL_PKG"
echo ">>> To install locally: open \"$FINAL_PKG\""
