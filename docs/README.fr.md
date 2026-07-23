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

Historique du presse-papiers multiplateforme pour **Windows / macOS / Linux**, avec **Avalonia + .NET 10**.
Expérience type PastePal : panneau de cartes, recherche (regex et indépendante de la disposition en↔ru), sélection rapide par chiffres, thèmes, réglages riches.

## Stack

| Couche | Technologie |
| --- | --- |
| UI | Avalonia 11, FluentTheme |
| Runtime | .NET 10 (LTS) |
| MVVM | CommunityToolkit.Mvvm |
| Raccourcis | SharpHook (libuiohook) |
| OCR | Tesseract 5 (eng+rus) + WinRT sous Windows |
| DI | Microsoft.Extensions.DependencyInjection |

Données dans `%LocalAppData%/ClipboardPal`.

Langues de l’interface : anglais (par défaut), russe, allemand, français, chinois, japonais — sélecteur en premier dans les paramètres.

## Fonctionnalités

- Raccourci global (par défaut Win/Cmd+Shift+V)
- Panneau bas/haut/gauche/droite, thèmes
- Recherche + regex + recherche indépendante de la disposition
- Sélection rapide : modificateurs + 1…0
- Épinglage, renommage, jusqu’à 100 000 éléments, apps exclues
- Icône de barre, démarrage automatique, OCR, bord chaud, son de copie

## Compilation

Nécessite le [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
cd ClipboardPal
dotnet restore
dotnet run --project src/ClipboardPal.App -c Release
```

Plus de détails (OCR, publication, architecture) — voir le [README anglais](../README.md).
