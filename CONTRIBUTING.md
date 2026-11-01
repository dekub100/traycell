# Contributing

## Requirements

- Windows 10/11 — the app is Windows-only (WinForms plus PnP/HID interop).
- .NET 10 SDK.

## Build and run

```powershell
dotnet build TrayCell.slnx

# the tray app
dotnet run --project src/TrayCell.App
```

Left-click the tray icon for the flyout; right-click for the context menu.

## Probe

The console harness is the test bed for the hardware side:

```powershell
dotnet run --project src/TrayCell.Probe -- all
dotnet run --project src/TrayCell.Probe -- controller
dotnet run --project src/TrayCell.Probe -- xinput
dotnet run --project src/TrayCell.Probe -- razer
dotnet run --project src/TrayCell.Probe -- hid
dotnet run --project src/TrayCell.Probe -- notify --seconds 60
dotnet run --project src/TrayCell.Probe -- battery
dotnet run --project src/TrayCell.Probe -- watch
dotnet run --project src/TrayCell.Probe -- reportids
```

## Tests

There is no automated test project yet. Logic that can be exercised without hardware belongs in one; until then, `TrayCell.Probe` plus a manual run of the app is the check.

## Hardware notes

Read [`docs/hardware-findings.md`](docs/hardware-findings.md) before changing device code. It records the verified protocol, the response codes seen in practice, and the dead ends that should not be retried.

## CI / CD

The workflow lives at `.github/workflows/ci.yml`.

- **Push to `main`**: restore and Release build on `windows-latest`.
- **Push a tag matching `v*`**: the same build, then a self-contained `win-x64` single-file publish, zipped as `TrayCell-v<version>-win-x64.zip` and attached to a GitHub release, with notes generated from conventional commits by [git-cliff](https://git-cliff.org) (`cliff.toml`).

The release runs only after the build job passes.

## Commit messages

Follow the [Conventional Commits](https://www.conventionalcommits.org/) spec:

```
<type>[optional scope]: <description>

[optional body]

[optional footer(s)]
```

| Type        | Usage                          |
|-------------|--------------------------------|
| `feat`      | A new feature                  |
| `fix`       | A bug fix                      |
| `build`     | Build system / dependencies    |
| `chore`     | Routine tasks, maintenance     |
| `ci`        | CI configuration               |
| `docs`      | Documentation only             |
| `perf`      | Performance improvement        |
| `refactor`  | Code change with no behaviour change |
| `style`     | Formatting only                |
| `test`      | Adding or fixing tests         |

Use `!` after the type/scope or a `BREAKING CHANGE` footer for breaking changes.

## Release workflow

Releases are tag-driven:

```powershell
git tag v0.2.0
git push origin v0.2.0
```

GitHub Actions builds the executable, generates notes, and creates the release.

### Manual fallback

```powershell
dotnet publish src/TrayCell.App/TrayCell.App.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -o publish

Compress-Archive -Path publish/* -DestinationPath TrayCell-v0.2.0-win-x64.zip

git-cliff --current --tag v0.2.0 -o RELEASE_NOTES.md
gh release create v0.2.0 -F RELEASE_NOTES.md TrayCell-v0.2.0-win-x64.zip
```

Pitfalls:

- Re-upload: `gh release delete-asset vX.Y.Z <filename> --yes && gh release upload vX.Y.Z <filename> --clobber`
- `cliff.toml` at the repo root drives both CI and local notes.

## License

MIT — see [`LICENSE`](LICENSE).
