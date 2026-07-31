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
| OCR | Systemmodule (Apple Vision / Windows OCR) + integriertes PP-OCRv5 auf ONNX Runtime |
| DI | Microsoft.Extensions.DependencyInjection |

Daten unter `%LocalAppData%/ClipboardPal`.

Oberflächensprachen: Englisch (Standard), Russisch, Deutsch, Französisch, Chinesisch, Japanisch — Umschalter als erster Eintrag in den Einstellungen.

## Funktionen

- Globaler Hotkey (Standard Win/Cmd+Shift+V)
- Panel unten/oben/links/rechts, Themes
- Drei Clip-Bereiche mit Tabs in der Panel-Kopfzeile: **Verlauf**, **Warteschlange**, **Papierkorb**
- Warteschlange — ein separater temporärer Bereich: Clips per **+** auf der Karte vormerken, zum Tab „Warteschlange“ wechseln und nacheinander einfügen. Ein eingefügter Clip verlässt die Warteschlange, bleibt aber im Verlauf (konfigurierbar); die Warteschlange übersteht Neustarts.
- Suche + Regex + layoutunabhängige Suche
- Schnellauswahl: Modifikatoren + 1…0
- Anheften, Umbenennen, bis 100 000 Einträge, ausgeschlossene Apps
- Tray, Autostart, Hot Edge, Kopier-Sound

## OCR

Funktioniert offline ab Werk — nichts zu installieren, nichts herunterzuladen. Zuerst wird die System-Engine verwendet (Apple Vision unter macOS, Windows OCR unter Windows), andernfalls das integrierte PP-OCRv5-Modell (Lateinisch + Kyrillisch) auf ONNX Runtime; es ist in die ausführbare Datei eingebettet und wird beim ersten Einsatz entpackt.

Jede Bildkarte hat eine **OCR**-Schaltfläche: Sie öffnet das Bild in einer Bereichsauswahl — Rahmen aufziehen, an den Griffen anpassen, mit dem Mausrad zoomen (die Ansicht folgt der Auswahl). Der erkannte Text landet in der Zwischenablage und als separate Textkarte im Verlauf. Erneutes Drücken — neue Bereichsauswahl.

## Build

Erfordert [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
cd ClipboardPal
dotnet restore
dotnet run --project src/ClipboardPal.App -c Release
```

Details (OCR, Publish, Architektur) — siehe [englische README](../README.md).
