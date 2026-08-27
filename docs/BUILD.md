# BlindTerm build and local install

This is the local build path for BlindTerm. It creates the package you can test without publishing anything to GitHub.

## Prerequisites

- Windows 10 or newer with ConPTY support.
- .NET 9 SDK.
- Inno Setup 6, normally at `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`.
- NVDA for speech and braille verification. JAWS is optional.

## Commands

Build, package, and create the installer:

```powershell
.\build.bat build
```

Build and install silently:

```powershell
.\build.bat install
```

Remove generated package output:

```powershell
.\build.bat clean
```

The `build` command publishes a self-contained `win-x64` application, adds the update worker, creates `dist\BlindTerm-v0.1.4.zip`, creates `dist\BlindTerm-Setup-v0.1.4.exe`, and writes `dist\BlindTerm-update.json` with SHA-256 values.

## Installation

The installer uses Program Files and creates a Start Menu entry. It writes a marker beside the executable so the updater knows it is an installed copy. Settings belong in `%APPDATA%\BlindTerm` and are intentionally not replaced by an update or removed by uninstall.

The installer is currently unsigned for public distribution unless a signing certificate is supplied through the normal Inno Setup and Windows signing tooling. Local testing does not require a public certificate.

## Publishing a release

After tests and the local build pass, commit the versioned files and create the matching Git tag. Push the branch and tag, then publish one GitHub release containing the portable ZIP, installer, and `BlindTerm-update.json`. The tag and release title use `v<version>` and `BlindTerm v<version>` respectively.
