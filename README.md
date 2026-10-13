<p align="center">
  <img src="docs/assets/ts3-package-fusion-icon.png" width="180" alt="TS3 Package Fusion icon">
</p>

<h1 align="center">TS3 Package Fusion</h1>

<p align="center">A simple desktop tool for combining The Sims 3 .package files.</p>

## Download

Download the latest version from the [Releases page](../../releases/latest).

| Platform | Download | Installation |
| --- | --- | --- |
| Windows 64-bit | `TS3PackageFusion.exe` | Download and run the file. No .NET installation is required. |
| macOS Apple Silicon | `TS3 Package Fusion.app.zip` | Extract it, then move the app to Applications. |
| macOS Intel | `TS3 Package Fusion-macos-intel.app.zip` | Extract it, then move the app to Applications. |

## How to use

1. Open TS3 Package Fusion.
2. Select **Add packages** and choose the `.package` files to combine.
3. Review the conflict notice if one appears.
4. Select **Merge and save**.
5. Choose the location and name of the new `.package` file.

The original files are never modified.

## Features

- Combine multiple `.package` files into one package.
- Import `.sims3pack` files and extract their internal packages directly into the project.
- Detect resource conflicts before merging.
- Choose **Keep A**, **Keep B**, **Do nothing**, or **Cancel** for each conflict.
- Apply **Do nothing** to all following conflicts when needed.
- Ignore truly identical resources automatically.
- Split the result into multiple packages when it would exceed the 2 GB limit recommended for The Sims 3.
- Open, save, and continue projects with `.ts3pf.json` project files.
- Read large source packages without loading the full file into memory.

## Project folder

TS3 Package Fusion creates its workspace here:

```text
Documents/TS3PackageFusion/
|- Projects/
|- Extracted Packages/
`- Merged Packages/
```

## Notes

- The Sims 3 can become unstable with packages above 2 GB. The app offers to create numbered output packages before reaching that limit.
- A resource conflict means two packages contain different data with the same DBPF resource key. It does not automatically mean one of the mods is broken.
- Back up your Mods folder before testing a new merged package in game.

## Build from source

Requirements: [.NET SDK 10](https://dotnet.microsoft.com/download).

```powershell
dotnet run --project .\TS3PackageFusion\TS3PackageFusion.csproj
dotnet test .\TS3PackageFusion.Tests\TS3PackageFusion.Tests.csproj
```

Create a Windows release:

```powershell
dotnet publish .\TS3PackageFusion\TS3PackageFusion.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\release\TS3PackageFusion-win-x64
```

Create a macOS Apple Silicon bundle on a Mac:

```zsh
dotnet publish ./TS3PackageFusion/TS3PackageFusion.csproj -c Release -r osx-arm64 --self-contained true -o ./release/TS3PackageFusion-osx-arm64
zsh ./scripts/new-mac-app.sh ./release/TS3PackageFusion-osx-arm64 ./release
```

For macOS Intel, replace `osx-arm64` with `osx-x64` in the publish command.

Sign and notarize the macOS app before distributing it publicly.
