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

**Windows / macOS / Linux** 向けのクロスプラットフォームなクリップボード履歴アプリ（**Avalonia + .NET 10**）。
PastePal 風の UI：カードパネル、検索（正規表現および en↔ru 配列非依存）、数字でのクイック選択、テーマと豊富な設定。

## スタック

| 層 | 技術 |
| --- | --- |
| UI | Avalonia 11, FluentTheme |
| Runtime | .NET 10 (LTS) |
| MVVM | CommunityToolkit.Mvvm |
| ホットキー | SharpHook (libuiohook) |
| OCR | Tesseract 5 (eng+rus) + Windows では WinRT |
| DI | Microsoft.Extensions.DependencyInjection |

データは `%LocalAppData%/ClipboardPal` に保存されます。

UI 言語：英語（デフォルト）、ロシア語、ドイツ語、フランス語、中国語、日本語 — 設定の先頭で切り替え。

## 機能

- グローバルホットキー（既定 Win/Cmd+Shift+V）
- 下/上/左/右パネルとテーマ
- 検索 + 正規表現 + 配列非依存検索
- クイック選択：修飾キー + 1…0
- ピン留め、名前変更、最大 100 000 件、除外アプリ
- トレイ、ログイン時起動、OCR、ホットエッジ、コピー音

## ビルド

[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) が必要です。

```bash
cd ClipboardPal
dotnet restore
dotnet run --project src/ClipboardPal.App -c Release
```

詳細（OCR、公開、アーキテクチャ）は [英語 README](../README.md) を参照。
