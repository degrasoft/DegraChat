# DegraChat

Нативное Windows-приложение для управления чатом стрима. Подключается к Twitch, GoodGame, Kick, VKPlay и YouTube, объединяет сообщения в единую ленту и транслирует их в OBS через HTML-оверлей.

## Технологии

- **C# 12+ / .NET 8** — основная платформа
- **Avalonia UI 11** — нативный UI-фреймворк
- **MVVM** — CommunityToolkit.Mvvm
- **TwitchLib** — Twitch IRC
- **ClientWebSocket** — GoodGame, Kick, VKPlay
- **Google.Apis.YouTube.v3** — YouTube Live Chat (REST polling)
- **HttpListener + System.Net.WebSockets** — локальный WS-сервер (порт 9274)
- **Scriban** — шаблонизатор оверлея
- **SQLite** (Microsoft.Data.Sqlite) — кэш
- **DPAPI** — шифрование токенов
- **Serilog** — логирование

## Структура проекта

```
DegraChat/
├── DegraChat.sln
├── src/
│   ├── DegraChat.App/              # Точка входа, MainWindow, DI, навигация
│   ├── DegraChat.Core/             # Доменные модели, IEventAggregator, события
│   ├── DegraChat.Chat.Abstractions/# IChatProvider, ChatProviderBase
│   ├── DegraChat.Chat.Twitch/      # TwitchLib IRC клиент
│   ├── DegraChat.Chat.GoodGame/    # GoodGame WebSocket
│   ├── DegraChat.Chat.Kick/        # Kick Pusher WebSocket
│   ├── DegraChat.Chat.VKPlay/      # VKPlay Centrifugo WebSocket
│   ├── DegraChat.Chat.YouTube/     # YouTube REST API polling
│   ├── DegraChat.Server/           # WebSocket сервер (HttpListener)
│   ├── DegraChat.Overlay.Engine/   # Scriban-генератор HTML/CSS
│   ├── DegraChat.Overlay.Assets/   # Статические файлы оверлея
│   ├── DegraChat.Editor/           # Визуальный редактор оверлея
│   └── DegraChat.Storage/          # JSON настройки + SQLite + DPAPI
├── docs/
│   └── ui-mockup.html              # Макет интерфейса
└── .github/workflows/
    ├── build.yml                   # CI: сборка + релиз
    └── pr-check.yml                # Проверка PR
```

## Сборка

### Требования

- .NET 8 SDK
- Windows 10/11

### Команды

```bash
# Восстановление зависимостей
dotnet restore DegraChat.sln

# Сборка (Debug)
dotnet build DegraChat.sln

# Сборка (Release)
dotnet build DegraChat.sln -c Release

# Публикация как single-file .exe
dotnet publish src/DegraChat.App/DegraChat.App.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=true \
  -o ./publish
```

## Интерфейс

4 вкладки в Activity Bar (стиль VS Code / Discord):

| Вкладка | Иконка | Назначение |
|---------|--------|------------|
| Подключения | link | Управление платформами, авторизация |
| Чат | chat | Единая лента сообщений со всех платформ |
| Редактор | paint-palette | Визуальный редактор оформления оверлея |
| Настройки | settings | Конфигурация, горячие клавиши, диагностика |

**Сервер** стартует автоматически при запуске приложения и перезапускается при изменении подключений. Диагностика сервера доступна через Настройки → Диагностика.

## OBS Setup

1. Запустите DegraChat
2. В OBS добавьте Browser Source
3. URL: `http://127.0.0.1:9274/overlay`
4. Ширина: 400, Высота: 800

## GitHub Actions

При пуше в `main` / `develop` автоматически запускается сборка. При создании тега `v*` (например, `v1.0.0`) создаётся релиз с собранным .exe:

```bash
git tag v1.0.0
git push origin v1.0.0
```

## Лицензия

Приватный проект. Все права защищены.
