# KnobForge macOS Release Guide

This guide covers building a production-style macOS release with:

- app bundle icon
- `.knob` document association
- signed `.app`
- signed installer `.pkg`
- notarization and stapling

## 0. Local Desktop Update (No Apple Developer Account)

Use this flow when you just want to update your own local app in Dock.

```bash
cd /Users/main/Desktop/KnobForge
APP_ICON_SOURCE=/Users/main/Desktop/KnobForge/icon.ico \
APP_SIGN_IDENTITY=- \
CODESIGN_ENABLED=1 \
REGISTER_LAUNCH_SERVICES=0 \
RESTORE=1 \
bash scripts/macos/build-app-bundle.sh
mkdir -p /Users/main/Applications
rsync -a artifacts/macos/KnobForge.app /Users/main/Applications/
xattr -dr com.apple.quarantine /Users/main/Applications/KnobForge.app
open /Users/main/Applications/KnobForge.app
```

If the icon does not update, regenerate `KnobForge.icns` and re-sign:

```bash
python3 - <<'PY'
from PIL import Image
src='/Users/main/Desktop/KnobForge/icon.ico'
dst='/Users/main/Desktop/KnobForge/artifacts/macos/KnobForge.app/Contents/Resources/KnobForge.icns'
img=Image.open(src)
img.save(dst, format='ICNS', sizes=[(16,16),(32,32),(64,64),(128,128),(256,256),(512,512),(1024,1024)])
print("wrote:", dst)
PY
codesign --force --deep --sign - /Users/main/Desktop/KnobForge/artifacts/macos/KnobForge.app
rsync -a /Users/main/Desktop/KnobForge/artifacts/macos/KnobForge.app /Users/main/Applications/
```

## Prerequisites

- macOS with Xcode command line tools installed (`xcode-select --install`)
- `.NET 8 SDK`
- Apple Developer membership (for Developer ID signing + notarization)
- Signing certificates installed in Keychain:
  - `Developer ID Application: ...`
  - `Developer ID Installer: ...`

Optional (recommended): create a notarytool profile once:

```bash
xcrun notarytool store-credentials "knobforge-notary" \
  --apple-id "<apple-id-email>" \
  --team-id "<team-id>" \
  --password "<app-specific-password>"
```

## 1. Build and Sign the App Bundle

From repo root:

```bash
APP_VERSION=1.2.0 \
BUILD_NUMBER=120 \
BUNDLE_IDENTIFIER=com.knobforge.app \
APP_ICON_SOURCE=/Users/main/Desktop/KnobForge/icon.ico \
APP_SIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)" \
bash scripts/macos/build-app-bundle.sh
```

Output:

```text
artifacts/macos/KnobForge.app
```

Notes:

- `APP_ICON_SOURCE` can be `.ico` or another image format supported by `sips`.
- For best icon quality, use a 1024x1024 source image.
- To build without signing (local testing): `APP_SIGN_IDENTITY=-`.
- Default entitlements are read from `/Users/main/Desktop/KnobForge/KnobForge.App/entitlements.macos.plist`.

## 2. Build and Sign the Installer Package

```bash
INSTALLER_SIGN_IDENTITY="Developer ID Installer: Your Name (TEAMID)" \
bash scripts/macos/build-installer-pkg.sh
```

Output:

```text
artifacts/macos/KnobForge-<version>.pkg
```

## 3. Notarize and Staple

Notarize the installer package:

```bash
NOTARYTOOL_PROFILE=knobforge-notary \
NOTARIZE_TARGET=pkg \
PKG_PATH=/Users/main/Desktop/KnobForge/artifacts/macos/KnobForge-1.2.0.pkg \
bash scripts/macos/notarize-release.sh
```

Alternative target for app bundle:

```bash
NOTARYTOOL_PROFILE=knobforge-notary \
NOTARIZE_TARGET=app \
bash scripts/macos/notarize-release.sh
```

## 4. Verify Before Distribution

Validate app signature:

```bash
codesign --verify --deep --strict --verbose=2 artifacts/macos/KnobForge.app
spctl --assess --type execute --verbose artifacts/macos/KnobForge.app
```

Validate installer signature:

```bash
pkgutil --check-signature artifacts/macos/KnobForge-1.2.0.pkg
spctl --assess --type install --verbose artifacts/macos/KnobForge-1.2.0.pkg
```

## 5. Finder File Association Check

1. Right-click any `.knob` file and choose `Get Info`.
2. In `Open with`, select `KnobForge.app`.
3. Click `Change All...`.

After that, double-clicking a `.knob` file opens it directly in KnobForge.
