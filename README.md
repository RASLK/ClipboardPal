## Language / Язык / Sprache / Langue / 语言 / 言語

| | |
| --- | --- |
| English (default) | [README.md](./README.md) |
| Русский | [docs/README.ru.md](./docs/README.ru.md) |
| Deutsch | [docs/README.de.md](./docs/README.de.md) |
| Français | [docs/README.fr.md](./docs/README.fr.md) |
| 中文 | [docs/README.zh.md](./docs/README.zh.md) |
| 日本語 | [docs/README.ja.md](./docs/README.ja.md) |

---

# ClipboardPal

Cross-platform clipboard history for **Windows / macOS / Linux**, built with **Avalonia + .NET 10**.
PastePal-like UX: card panel, search (Regex and layout-independent en↔ru), quick number selection, themes, rich settings.

## Stack

| Layer | Technology |
| --- | --- |
| UI | Avalonia 11, FluentTheme |
| Runtime | .NET 10 (LTS) |
| MVVM | CommunityToolkit.Mvvm (source generators) |
| Hotkeys | SharpHook (libuiohook) — Win / macOS / Linux |
| OCR | Tesseract 5 (eng+rus) + WinRT on Windows |
| DI | Microsoft.Extensions.DependencyInjection |

Data lives under `%LocalAppData%/ClipboardPal` (`history.json`, `settings.json`, `images/`, `ocr/`, `sounds/`, `backups/`) and stays compatible across app versions.

- New settings fields get defaults; existing values are kept.
- Unknown fields from newer versions are ignored on read.
- Corrupt JSON is copied to `backups/` before reset.
- Data path does not change on upgrade — history and settings are preserved.

## Features

- Global hotkey (default Win/Cmd+Shift+V)
- Card panel: bottom / top / left / right, dark / light / system theme
- Search + Regex + layout-independent search
- Quick select: hotkey modifiers + 1…0
- Pin, rename, up to 100 000 items, excluded apps
- Tray icon, launch at login, persistent history
- Image OCR (eng+rus), hot edge, copy sound — all platforms
- Text and image capture on Win / macOS / Linux
- UI languages: English (default), Russian, German, French, Chinese, Japanese

## OCR

On first recognition the app downloads `eng`/`rus` models into `%LocalAppData%/ClipboardPal/ocr/tessdata`.

| OS | Engine |
| --- | --- |
| Windows | WinRT OCR → Tesseract (in-process) → CLI |
| macOS / Linux | system `tesseract` → conda-forge binaries → in-process when native libs exist |

Optional manual install:

```bash
# macOS
brew install tesseract

# Debian/Ubuntu
sudo apt install tesseract-ocr tesseract-ocr-eng tesseract-ocr-rus
```

Enable **Recognize text in images** in Settings and grant Screen Recording if needed.

## Build

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
cd ClipboardPal
dotnet restore
dotnet run --project src/ClipboardPal.App -c Release
```

### Local publish (single-file, self-contained)

Any `-r <rid>` publish produces one executable (native libs extract on first run):

```bash
dotnet publish src/ClipboardPal.App -c Release -r win-x64 -o publish/win-x64
dotnet publish src/ClipboardPal.App -c Release -r win-x86 -o publish/win-x86
dotnet publish src/ClipboardPal.App -c Release -r linux-x64 -o publish/linux-x64
dotnet publish src/ClipboardPal.App -c Release -r osx-x64 -o publish/osx-x64
dotnet publish src/ClipboardPal.App -c Release -r osx-arm64 -o publish/osx-arm64
```

### GitHub Releases (CI)

Push a version tag. Layout mirrors [Flameshot’s workflows](https://github.com/flameshot-org/flameshot/tree/master/.github/workflows):

| Workflow | Assets |
| --- | --- |
| `windows-pack.yml` | `.msi` + `.zip` + `.sha256sum` |
| `macos-pack.yml` | `.dmg` + `.sha256sum` |
| `linux-pack.yml` | `.AppImage` + `.zip` + `.sha256sum` |
| `release.yml` | on tag → run packs → create GitHub Release |

**`.sha256sum`** — small text file with the SHA-256 hash of the matching asset. Lets users check the download is intact (`sha256sum -c file.sha256sum`). Not an installer.

**Source code (zip/tar.gz)** — auto-added by GitHub (repo snapshot), not our build.

```bash
git tag v2.0.1
git push origin v2.0.1
```

Pack jobs can also be run manually via **Actions → Packaging (Windows|macOS|Linux) → Run workflow**.

**macOS:** DMGs are unsigned (no Apple Developer ID). First launch may need right-click → Open.  
**OCR** bootstraps at runtime into app data — not packed into installers.  
**Linux:** AppImage is the modern portable format (like Flameshot). deb/rpm/Flatpak/Snap are Qt/distro-specific and not mirrored for Avalonia yet.
## Architecture

```
src/
  ClipboardPal.Core/     # models, VMs, store, platform abstractions
  ClipboardPal.App/      # Avalonia UI + platform implementations
```

Platform contracts (`IClipboardWatcher`, `IGlobalHotkeyService`, `IPasteService`, `IOcrService`, …)
are implemented under `Platform/` with `OperatingSystem.Is*()` branches.

## License

See repository license file if present; otherwise all rights reserved by the author until specified.
