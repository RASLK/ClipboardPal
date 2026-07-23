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
| OCR | Tesseract 5 (eng+rus) + Windows 上的 WinRT |
| DI | Microsoft.Extensions.DependencyInjection |

数据目录：`%LocalAppData%/ClipboardPal`。

界面语言：英语（默认）、俄语、德语、法语、中文、日语 — 设置中第一项即可切换。

## 功能

- 全局热键（默认 Win/Cmd+Shift+V）
- 底部/顶部/左/右面板与主题
- 搜索 + Regex + 布局无关搜索
- 快速选择：修饰键 + 1…0
- 固定、重命名、最多 100 000 条、排除应用
- 托盘、开机启动、OCR、热边缘、复制提示音

## 构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。

```bash
cd ClipboardPal
dotnet restore
dotnet run --project src/ClipboardPal.App -c Release
```

更多细节（OCR、发布、架构）见 [英文 README](../README.md)。
