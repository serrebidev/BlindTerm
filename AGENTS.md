# Agent notes - BlindTerm

## Release

- On the Windows release host, release as `docs/BUILD.md` describes: bump `<Version>` in `Directory.Build.props`, add the `## vX.Y.Z - date` entry to `CHANGELOG.md`, commit `Release vX.Y.Z: ...`, run `.\build.ps1`, tag `vX.Y.Z`, push, and publish `BlindTerm vX.Y.Z` with the ZIP, installer and `BlindTerm-update.json`.
- Muse agent and cloud agents ONLY: `.github/workflows/cloud-release.yml` does the tag-and-publish half on a GitHub Windows runner. Commit and push the version bump and CHANGELOG entry to `master` first, then `gh workflow run cloud-release.yml -f dry_run=true` (tests, replay tests and build as workflow artifacts, publishes nothing), then `-f dry_run=false` (tags `v<Version>`, publishes with the CHANGELOG entry as notes, verifies `/releases/latest`). Watch: `gh run watch <id> --exit-status`. It refuses an existing tag or a missing CHANGELOG entry.
- Windows only. The app is WinForms on ConPTY and `BlindTerm.Core` targets `net9.0-windows` with Win32 interop, so macOS/Linux builds need a real port (UI toolkit, PTY, speech), not a build change.
