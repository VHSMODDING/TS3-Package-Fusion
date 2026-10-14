#!/bin/zsh
set -euo pipefail

if [[ $# -lt 2 || $# -gt 3 ]]; then
  print "Usage: $0 <publish-directory> <output-directory> [version]"
  exit 64
fi

publish_directory="$1"
output_directory="$2"
version="${3:-1.0.0}"
script_directory="${0:A:h}"
icon_path="$script_directory/../TS3PackageFusion/Assets/TS3PackageFusion.icns"

if [[ ! "$version" =~ ^[0-9]+(\.[0-9]+){1,3}$ ]]; then
  print "La version doit être numérique (par exemple 1.0.2)."
  exit 64
fi
app_path="$output_directory/TS3 Package Fusion.app"
macos_path="$app_path/Contents/MacOS"

if [[ -e "$app_path" ]]; then
  print "Le bundle existe déjà : $app_path"
  exit 1
fi

if [[ ! -f "$icon_path" ]]; then
  print "Icône macOS introuvable : $icon_path"
  exit 1
fi

mkdir -p "$macos_path" "$app_path/Contents/Resources"
cp -R "$publish_directory/." "$macos_path/"
cp "$icon_path" "$app_path/Contents/Resources/TS3PackageFusion.icns"
chmod +x "$macos_path/TS3PackageFusion"

cat > "$app_path/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleDisplayName</key><string>TS3 Package Fusion</string>
  <key>CFBundleExecutable</key><string>TS3PackageFusion</string>
  <key>CFBundleIdentifier</key><string>com.ts3packagefusion.app</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleIconFile</key><string>TS3PackageFusion.icns</string>
  <key>CFBundleName</key><string>TS3 Package Fusion</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST

print "$app_path"
