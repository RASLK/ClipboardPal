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

跨平台剪贴板历史工具（**Windows / macOS / Linux**），基于 **Avalonia + .NET 10**。
类 PastePal 体验：卡片面板、搜索（Regex 与 en↔ru 布局无关搜索）、数字快捷选择、主题与丰富设置。

## 技术栈

| 层 | 技术 |
| --- | --- |
| UI | Avalonia 11, FluentTheme |
| Runtime | .NET 10 (LTS) |
| MVVM | CommunityToolkit.Mvvm |
| 快捷键 | SharpHook (libuiohook) |
| OCR | 系统引擎（Apple Vision / Windows OCR）+ 内置 PP-OCRv5（ONNX Runtime）|
| DI | Microsoft.Extensions.DependencyInjection |

数据目录：`%LocalAppData%/ClipboardPal`。

界面语言：英语（默认）、俄语、德语、法语、中文、日语 — 设置中第一项即可切换。

## 功能

- 全局热键（默认 Win/Cmd+Shift+V）
- 底部/顶部/左/右面板与主题
- 面板顶部的标签页切换三个剪贴空间：**历史**、**队列**、**回收站**
- 队列是独立的临时空间：用卡片上的 **+** 按钮暂存剪贴，切到「队列」标签后逐条粘贴。已粘贴的条目会离开队列，但仍保留在历史中（可配置）；队列在重启后保留。
- 搜索 + Regex + 布局无关搜索
- 快速选择：修饰键 + 1…0
- 固定、重命名、最多 100 000 条、排除应用
- 托盘、开机启动、离线 OCR、热边缘、复制提示音

## OCR

开箱即用，完全离线 — 无需安装或下载任何内容。优先使用系统引擎（macOS 上的 Apple Vision，Windows 上的 Windows OCR），否则使用基于 ONNX Runtime 的内置 PP-OCRv5 模型（拉丁字母 + 西里尔字母）；模型内嵌在可执行文件中，首次使用时解包。

每张图片卡片都有 **OCR** 按钮：打开区域选择器 — 画出选框，拖动手柄调整，滚轮缩放（视图跟随选区）。识别出的文本会放入剪贴板，并作为单独的文本卡片进入历史。再次点击 OCR 可选择新区域。

## 构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。

```bash
cd ClipboardPal
dotnet restore
dotnet run --project src/ClipboardPal.App -c Release
```

发布：推送标签 `v2.0.2` → 与 Flameshot 类似：**MSI+ZIP**（Win）、**DMG**（macOS）、**AppImage+ZIP**（Linux）及 **`.sha256sum`** 校验文件。

更多细节（发布、架构）见 [英文 README](../README.md)。
