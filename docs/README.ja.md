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
| OCR | システムエンジン（Apple Vision / Windows OCR）+ 内蔵 PP-OCRv5（ONNX Runtime）|
| DI | Microsoft.Extensions.DependencyInjection |

データは `%LocalAppData%/ClipboardPal` に保存されます。

UI 言語：英語（デフォルト）、ロシア語、ドイツ語、フランス語、中国語、日本語 — 設定の先頭で切り替え。

## 機能

- グローバルホットキー（既定 Win/Cmd+Shift+V）
- 下/上/左/右パネルとテーマ
- パネル上部のタブで 3 つの空間を切り替え：**履歴**、**キュー**、**ゴミ箱**
- キューは独立した一時領域：カードの **+** ボタンでクリップを追加し、「キュー」タブに切り替えて 1 件ずつ貼り付け。貼り付けたクリップはキューから消えるが履歴には残り（設定可能）、キューは再起動後も保持される
- 検索 + 正規表現 + 配列非依存検索
- クイック選択：修飾キー + 1…0
- ピン留め、名前変更、最大 100 000 件、除外アプリ
- トレイ、ログイン時起動、OCR、ホットエッジ、コピー音

## OCR

完全オフラインで動作 — インストールもダウンロードも不要。まずシステムエンジン（macOS では Apple Vision、Windows では Windows OCR）を使用し、なければ内蔵の PP-OCRv5 モデル（ラテン文字 + キリル文字）が ONNX Runtime 上で動作します。モデルは実行ファイルに埋め込まれ、初回使用時に展開されます。

各画像カードには **OCR** ボタンがあり、領域選択画面が開きます：枠を描き、ハンドルをドラッグして調整し、ホイールでズーム（ビューは選択範囲に追従）。認識したテキストはクリップボードに入り、履歴に別のテキストカードとして追加されます。もう一度 OCR を押すと新しい領域を選択できます。

## ビルド

[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) が必要です。

```bash
cd ClipboardPal
dotnet restore
dotnet run --project src/ClipboardPal.App -c Release
```

リリース：タグ `v2.0.2` → Flameshot 方式で **MSI+ZIP**（Win）、**DMG**（macOS）、**AppImage+ZIP**（Linux）と **`.sha256sum`** を生成。

詳細（公開、アーキテクチャ）は [英語 README](../README.md) を参照。
