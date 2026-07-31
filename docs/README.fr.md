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
| OCR | Moteurs système (Apple Vision / Windows OCR) + PP-OCRv5 intégré sur ONNX Runtime |
| DI | Microsoft.Extensions.DependencyInjection |

Données dans `%LocalAppData%/ClipboardPal`.

Langues de l’interface : anglais (par défaut), russe, allemand, français, chinois, japonais — sélecteur en premier dans les paramètres.

## Fonctionnalités

- Raccourci global (par défaut Win/Cmd+Shift+V)
- Panneau bas/haut/gauche/droite, thèmes
- Trois espaces de clips via des onglets dans l’en-tête du panneau : **Historique**, **File d’attente**, **Corbeille**
- File d’attente — un espace temporaire séparé : mettez des clips de côté avec le bouton **+** sur une carte, passez à l’onglet « File d’attente » et collez-les un par un. Un clip collé quitte la file mais reste dans l’historique (configurable) ; la file survit aux redémarrages.
- Recherche + regex + recherche indépendante de la disposition
- Sélection rapide : modificateurs + 1…0
- Épinglage, renommage, jusqu’à 100 000 éléments, apps exclues
- Icône de barre, démarrage automatique, OCR, bord chaud, son de copie

## OCR

Fonctionne entièrement hors ligne, prêt à l’emploi — rien à installer ni à télécharger. Le moteur système est utilisé en premier (Apple Vision sur macOS, Windows OCR sur Windows), sinon le modèle intégré PP-OCRv5 (latin + cyrillique) sur ONNX Runtime ; il est embarqué dans l’exécutable et décompressé au premier usage.

Chaque carte-image a un bouton **OCR** : il ouvre l’image dans un sélecteur de zone — tracez un cadre, ajustez-le par ses poignées, zoomez à la molette (la vue suit la sélection). Le texte reconnu est copié dans le presse-papiers et devient une carte texte séparée dans l’historique. Un nouvel appui sur **OCR** — une nouvelle zone.

## Compilation

Nécessite le [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
cd ClipboardPal
dotnet restore
dotnet run --project src/ClipboardPal.App -c Release
```

Plus de détails (publication, architecture) — voir le [README anglais](../README.md).
