## Language / Язык / Sprache / Langue / 语言 / 言語

| | |
| --- | --- |
| English (default) | [../README.md](../README.md) |
| Русский | [README.ru.md](./README.ru.md) |
| Deutsch | [README.de.md](./README.de.md) |
| Français | [README.fr.md](./README.fr.md) |
| 中文 | [README.zh.md](./README.zh.md) |
| 日本語 | [README.ja.md](./README.ja.md) |

---

# ClipboardPal

Plattformübergreifende Zwischenablage-Historie für **Windows / macOS / Linux** mit **Avalonia + .NET 10**.
PastePal-ähnliche Oberfläche: Kartenpanel, Suche (Regex und layoutunabhängig en↔ru), Schnellauswahl per Ziffern, Themes, umfangreiche Einstellungen.

## Stack

| Schicht | Technologie |
| --- | --- |
| UI | Avalonia 11, FluentTheme |
| Runtime | .NET 10 (LTS) |
| MVVM | CommunityToolkit.Mvvm |
| Hotkeys | SharpHook (libuiohook) |
| OCR | Tesseract 5 (eng+rus) + WinRT unter Windows |
| DI | Microsoft.Extensions.DependencyInjection |

Daten unter `%LocalAppData%/ClipboardPal`.

Oberflächensprachen: Englisch (Standard), Russisch, Deutsch, Französisch, Chinesisch, Japanisch — Umschalter als erster Eintrag in den Einstellungen.

## Funktionen

- Globaler Hotkey (Standard Win/Cmd+Shift+V)
- Panel unten/oben/links/rechts, Themes
- Suche + Regex + layoutunabhängige Suche
- Schnellauswahl: Modifikatoren + 1…0
- Anheften, Umbenennen, bis 100 000 Einträge, ausgeschlossene Apps
- Tray, Autostart, OCR, Hot Edge, Kopier-Sound

## Build

Erfordert [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
cd ClipboardPal
dotnet restore
dotnet run --project src/ClipboardPal.App -c Release
```

Details (OCR, Publish, Architektur) — siehe [englische README](../README.md).
