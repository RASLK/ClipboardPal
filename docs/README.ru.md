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

Кроссплатформенный расширенный буфер обмена (**Windows / macOS / Linux**) на **Avalonia + .NET 10**.
Опыт в духе PastePal: панель карточек, поиск (Regex и без учёта раскладки en↔ru), быстрый выбор цифрами, темы, гибкие настройки.

## Стек

| Слой | Технология |
| --- | --- |
| UI | Avalonia 11, FluentTheme |
| Runtime | .NET 10 (LTS) |
| MVVM | CommunityToolkit.Mvvm |
| Hotkeys | SharpHook (libuiohook) |
| OCR | Tesseract 5 (eng+rus) + WinRT на Windows |
| DI | Microsoft.Extensions.DependencyInjection |

Данные: `%LocalAppData%/ClipboardPal` (`history.json`, `settings.json`, `images/`, `ocr/`, `sounds/`, `backups/`).

Языки интерфейса: английский (по умолчанию), русский, немецкий, французский, китайский, японский — переключатель первым пунктом в настройках.

## Возможности

- Глобальный хоткей (по умолчанию Win/Cmd+Shift+V)
- Панель снизу/сверху/слева/справа, темы
- Поиск + Regex + раскладко-независимый поиск
- Быстрый выбор: модификаторы + 1…0
- Закрепление, переименование, лимит до 100 000, исключения приложений
- Трей, автозапуск, OCR, горячий край, звук копирования

## Сборка

Нужен [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
cd ClipboardPal
dotnet restore
dotnet run --project src/ClipboardPal.App -c Release
```

Подробнее (OCR, publish, архитектура) — в [английской версии README](../README.md).
