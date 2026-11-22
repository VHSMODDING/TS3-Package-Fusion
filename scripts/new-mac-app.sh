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
  <key>CFBundleDocumentTypes</key><array>
    <dict><key>CFBundleTypeName</key><string>Sims 3 Package</string><key>CFBundleTypeRole</key><string>Viewer</string><key>LSHandlerRank</key><string>Alternate</string><key>CFBundleTypeExtensions</key><array><string>package</string><string>sims3pack</string></array><key>LSItemContentTypes</key><array><string>public.data</string></array></dict>
    <dict><key>CFBundleTypeName</key><string>Folder</string><key>CFBundleTypeRole</key><string>Viewer</string><key>LSHandlerRank</key><string>Alternate</string><key>LSItemContentTypes</key><array><string>public.folder</string></array></dict>
  </array>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST

# Signature du bundle complet. Sans cela, la signature ad hoc posée par dotnet publish
# sur l'exécutable ne couvre pas le bundle et macOS affiche « app endommagée » après téléchargement.
# Par défaut : signature ad hoc ("-"). Avec un certificat Developer ID :
#   CODESIGN_IDENTITY="Developer ID Application: Nom (TEAMID)" zsh new-mac-app.sh ...
identity="${CODESIGN_IDENTITY:--}"
sign_options=(--force --timestamp=none --sign "$identity")
if [[ "$identity" != "-" ]]; then
  entitlements="$(mktemp -t ts3pf-entitlements).plist"
  cat > "$entitlements" <<ENT
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>com.apple.security.cs.allow-jit</key><true/>
  <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
  <key>com.apple.security.cs.disable-library-validation</key><true/>
</dict></plist>
ENT
  sign_options=(--force --timestamp --options runtime --entitlements "$entitlements" --sign "$identity")
fi

xattr -cr "$app_path"
# Tous les fichiers de Contents/MacOS (dylib, dll, json…) sont vus comme du code par macOS :
# chacun doit être signé, sauf l'exécutable principal qui est signé avec le bundle.
find "$macos_path" -type f ! -path "$macos_path/TS3PackageFusion" -print0 | while IFS= read -r -d '' file; do
  codesign "${sign_options[@]}" "$file" 2>/dev/null
done
codesign "${sign_options[@]}" "$app_path"
codesign --verify --deep --strict "$app_path"

print "$app_path"
