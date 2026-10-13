param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$publishPath = (Resolve-Path -LiteralPath $PublishDirectory).Path
$appPath = Join-Path $OutputDirectory 'TS3 Package Fusion.app'
$macOsPath = Join-Path $appPath 'Contents/MacOS'
$resourcesPath = Join-Path $appPath 'Contents/Resources'

New-Item -ItemType Directory -Force -Path $macOsPath, $resourcesPath | Out-Null
Get-ChildItem -LiteralPath $publishPath -Force | Copy-Item -Destination $macOsPath -Recurse -Force

$infoPlist = @'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key><string>en</string>
  <key>CFBundleDisplayName</key><string>TS3 Package Fusion</string>
  <key>CFBundleExecutable</key><string>TS3PackageFusion</string>
  <key>CFBundleIdentifier</key><string>com.ts3packagefusion.app</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleName</key><string>TS3 Package Fusion</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
'@

Set-Content -LiteralPath (Join-Path $appPath 'Contents/Info.plist') -Value $infoPlist -Encoding utf8NoBOM
Write-Output $appPath
