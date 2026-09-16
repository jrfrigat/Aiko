# Aiko - Установка

Aiko - локальный оркестратор ИИ-разработки: один loopback-демон, Kanban PWA, долговременная
память проекта и стабильный MCP-контракт для Claude Code, Codex, Cursor и ZCode.

Этот гайд описывает установку и запуск Aiko на Windows.

## Требования

- Windows 10/11 x64 (релиз - самодостаточная сборка win-x64).
- Для установки релиза больше ничего не нужно: ни .NET SDK, ни .NET runtime.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) - только если вы собираете и
  запускаете из исходников. Сборка из исходников публикуется framework-dependent, поэтому на этапе
  запуска нужен .NET 10 runtime.

## Установка

Одна строка в PowerShell:

```powershell
irm https://raw.githubusercontent.com/jrfrigat/Aiko/main/scripts/install.ps1 | iex
```

Установщик:

1. находит свежий релиз на GitHub (или указанный вами тег),
2. скачивает из него `aiko-<версия>-win-x64.zip`,
3. распаковывает в `%LOCALAPPDATA%\Aiko\bin`,
4. добавляет этот каталог в пользовательский `PATH`.

Ничего не ставится на всю машину, права администратора не нужны. Откройте новый терминал, чтобы
команда `aiko` подхватилась.

Установленная раскладка:

```text
%LOCALAPPDATA%\Aiko\bin\aiko.exe                 CLI
%LOCALAPPDATA%\Aiko\bin\aiko-stdio.exe           stdio <-> Streamable HTTP MCP-прокси
%LOCALAPPDATA%\Aiko\bin\server\Aiko.Server.exe   демон, PWA в server\wwwroot
```

### Параметры установщика

| Параметр | Действие |
| :-- | :-- |
| `-Version <tag>` | Установить конкретный релиз, например `v0.1.0`. По умолчанию - последний релиз. |
| `-InstallDir <path>` | Распаковать в другой каталог. По умолчанию `%LOCALAPPDATA%\Aiko\bin`. |
| `-NoPathUpdate` | Не менять пользовательский `PATH`. |

Параметры требуют формы со scriptblock, потому что `irm ... | iex` их не принимает:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/jrfrigat/Aiko/main/scripts/install.ps1))) `
    -Version v0.1.0 -InstallDir D:\Tools\Aiko
```

### Установка из исходников

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

Скрипт публикует текущее рабочее дерево framework-dependent (`aiko` CLI, демон, stdio-прокси) в тот
же каталог `%LOCALAPPDATA%\Aiko\bin`; на этапе запуска потребуется .NET 10 runtime.

Установите глобальные скиллы агентов (опционально, но рекомендуется - чтобы `/aiko-init`
работал везде):

```powershell
aiko agent install --scope user
```

## Запуск

Запустите демон (он слушает только loopback):

```powershell
aiko serve
```

Откройте UI - браузер сопрягается с демоном по одноразовому коду и открывает доску:

```powershell
aiko ui
```

`aiko status` показывает каталог данных, путь к базе, порт и состояние демона.

## Конфигурация

| Переменная окружения | Назначение |
| :-- | :-- |
| `AIKO_PORT` | Явный порт для следующего запуска; проверяется и сохраняется |
| `AIKO_URL` | Явный loopback-адрес (переопределяет выбор порта) |
| `AIKO_DATABASE` | Путь к базе SQLite (по умолчанию `%LOCALAPPDATA%\Aiko\aiko.db`) |
| `AIKO_TOKEN` | Фиксированный токен доступа (иначе генерируется и хранится в `access-token`) |
| `AIKO_PAIR_CODE` | Фиксированный код сопряжения (для тестов/скриптов) |
| `AIKO_INSECURE` | `1` отключает аутентификацию (только для локальной отладки) |

Демон предпочитает порт `24560`; если он занят - спрашивает другой порт (или выбирает свободный
из `18000-18999` в неинтерактивном режиме) и запоминает выбор в `settings.json` рядом с базой.

## Регистрация проекта

Либо в UI (кнопка "Добавить проект" на пустой доске), либо из терминала:

```powershell
aiko init C:\path\to\your\project --name "My Project" --git-policy local-only
```

Это создаёт каталог `.aiko` (workflow, проекции, память) и регистрирует проект. По умолчанию
git policy - `local-only` (весь `.aiko` добавляется в `.gitignore`); `track-project-knowledge`
хранит workflow и память в системе контроля версий.

## Подключение агента

```powershell
aiko agent install --project <projectId>
```

Записывает project-scoped MCP-конфигурацию и скиллы `/aiko-*` для найденных агентов.
`aiko agent list` показывает, какие агенты найдены. Перезапустите агента, чтобы он загрузил
новый MCP-сервер.

## Обновление и удаление

- Обновление: повторно запустите установщик (релизный или из исходников) - бинари заменяются, данные
  и настройки проектов сохраняются. Параметр `-Version` фиксирует конкретный релиз, если свежий не
  нужен.
- Удаление: удалите `%LOCALAPPDATA%\Aiko\bin` и уберите его из пользовательского `PATH`.
  Каталоги `.aiko` проектов и база никогда не удаляются автоматически.

## Проверка

- `aiko status` - состояние демона, порт, каталог данных.
- `GET http://127.0.0.1:<port>/health` - `healthy`.
- `aiko ui` - открывает доску.

Если `irm` блокируется политикой выполнения, запустите установщик явно:

```powershell
powershell -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/jrfrigat/Aiko/main/scripts/install.ps1 | iex"
```
