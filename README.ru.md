# Aiko - локальный оркестратор ИИ-разработки

<p align="center"><img src="assets/logo.svg" alt="Aiko" width="96" /></p>

<p align="center">🌐 <a href="README.md">English</a> - <b>Русский</b></p>

[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![Статус](https://img.shields.io/badge/status-MVP%20foundation-orange)](docs/ru/technical-specification.md)
[![Тесты](https://img.shields.io/badge/tests-54%20xUnit-green)](#тесты)

Aiko (AI kanban orchestrator) - **локальный оркестратор разработки с участием ИИ**: один loopback-демон
дает Claude Code, Codex, Cursor и ZCode общий контекст проекта, настраиваемый Kanban-конвейер,
устойчивую память и стабильный MCP-контракт - при этом все данные остаются на диске в
Git-дружелюбных Markdown и JSON.

Aiko не заменяет агентов. Он связывает их: карточки двигаются по настраиваемым этапам workflow,
каждый этап может выполнять другой агент, а незавершенный этап (например, после исчерпания лимита)
передается следующему агенту без потери истории.

**File-first хранение - типизированный граф карточек - эффективные приоритеты - handoff между агентами с полной историей попыток - устойчивая память проекта - только loopback по дизайну**

---

## Возможности

- **Локальный демон, один на пользователя** - единый процесс ASP.NET Core обслуживает все
  зарегистрированные проекты: REST API, проектный MCP over Streamable HTTP, health-endpoint'ы и PWA
- **Kanban PWA** на Blazor WebAssembly ([Flare.Blazor](https://github.com/jrfrigat/Flare)):
  проекции доски (Tasks / Stories / Combined), редактор карточек, артефакты и выполнения
- **Карточки-каталоги** - каждая Story/Task это папка с `card.json` (оптимистичные ревизии) и
  Markdown-артефактами; все дерево `.aiko` читаемо, сравнимо в diff и дружелюбно к Git
- **Типизированный граф карточек** - связи `implements`, `parent-child`, `blocks` (с запретом
  циклов) и симметричная `relates-to`; блокировки учитываются планировщиком
- **Эффективные приоритеты** - у каждой карточки своя оценка; приоритет задачи объединяется с
  максимальной родительской по настраиваемым весам (версионируемая формула)
- **Цикл исполнения** - `StageExecution` владеет рабочей областью; `AgentAttempt` фиксирует каждую
  попытку агента, поэтому handoff, resume, pause и rate-limit не теряют историю
- **Устойчивая память** - решения, соглашения и уроки живут в `.aiko/memory` как Markdown и
  ищутся через FTS5-индекс SQLite
- **Единый установщик агентов** - обнаруживает установки Claude Code, Codex, Cursor и ZCode и
  идемпотентно применяет проектную конфигурацию (MCP-записи, управляемые блоки, собственные
  skills/команды), сохраняя настройки пользователя; планы по адаптерам и точечное удаление
- **SQLite-проекции, перестраиваемые** - глобальная база SQLite это индекс и runtime-состояние;
  ее удаление безопасно, `reindex` восстанавливает всё из файлов `.aiko`
- **Безопасность по умолчанию** - привязка только на loopback, проверка `Host`/`Origin` против
  DNS-rebinding и удаленных origin, без wildcard CORS

---

## Быстрый старт

Требуется [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```sh
git clone <url-репозитория>
cd Aiko

# собрать всё (сервер, PWA, stdio-прокси, тесты)
dotnet build Aiko.slnx

# запустить демон (раздает PWA и MCP endpoint'ы)
dotnet run --project src/Aiko.Server
```

Демон предпочитает порт **24560**; если он занят, выбирает свободный из `18000-18999` и запоминает
выбор в `settings.json` рядом с базой. Откройте UI на `http://127.0.0.1:24560` и зарегистрируйте
проект через интерфейс (или API ниже).

Зарегистрировать проект и получить MCP-endpoint:

```sh
curl -X POST http://127.0.0.1:24560/api/v1/projects/initialize \
     -H "Content-Type: application/json" \
     -d "{ \"rootPath\": \"C:/путь/к/проекту\" }"
# => { "id": "<projectId>", ... }   MCP: http://127.0.0.1:24560/mcp/projects/<projectId>
```

### Конфигурация

| Переменная окружения | Назначение |
| :-- | :-- |
| `AIKO_PORT` | Явный порт для запуска; проверяется и сохраняется |
| `AIKO_URL` | Явный loopback-origin (переопределяет выбор порта) |
| `AIKO_DATABASE` | Путь к базе SQLite (по умолчанию `%LocalAppData%/Aiko/aiko.db`) |

### Тесты

```sh
dotnet test Aiko.slnx
```

54 xUnit-проверки в трех наборах: доменные правила, инфраструктура (файлы/SQLite) и интеграция
MCP. MCP-набор сам поднимает демон на случайном порту с изолированной базой - ручной
оркестратор не нужен.

---

## Как подключаются агенты

Каждому проекту выдается собственный Streamable HTTP MCP-endpoint:

```text
http://127.0.0.1:<port>/mcp/projects/<projectId>
```

Клиенты без надежного Streamable HTTP используют тонкий stdio-прокси (без второго хранилища,
форвардинг на уровне транспорта, только loopback):

```text
aiko-stdio --url http://127.0.0.1:<port>/mcp/projects/<projectId>
```

Стабильный набор инструментов (29 tools): контекст проекта, CRUD карточек и связи, взятие
карточки, полный цикл выполнения этапа (start / report progress / request scope expansion /
complete / pause / handoff / resume / report agent state), поиск и запись памяти, открытие UI.
Описания инструментов требуют сначала получать контекст проекта; установленные skills, rules и
блоки `AGENTS.md` усиливают это для каждого агента.

---

## Структура репозитория

| Проект | Ответственность |
| :-- | :-- |
| `src/Aiko.Domain` | Карточки, связи, workflow, приоритеты, выполнения - без I/O и агентов |
| `src/Aiko.Application` | Сценарии и порты (хранилища, координатор, контракты установщика) |
| `src/Aiko.Infrastructure` | Файловые хранилища, SQLite-проекции, FTS5-память, адаптеры агентов |
| `src/Aiko.Server` | Демон ASP.NET Core: REST API, MCP-endpoint'ы, хостинг PWA |
| `src/Aiko.Pwa` | Blazor WebAssembly PWA на Flare.Blazor |
| `src/Aiko.StdioProxy` | Короткоживущий stdio <-> Streamable HTTP MCP-прокси |
| `tests/*` | Наборы xUnit: Domain.Specs, Infrastructure.Specs, Mcp.Specs (самодостаточные) |

---

## Документация

- [Установка](docs/ru/installation.md) - установка, запуск, настройка, обновление и удаление
- [Быстрый старт](docs/ru/getting-started.md) - первый проект за несколько минут
- [Руководство пользователя](docs/ru/user-guide.md) - доска, карточки, workflow, настройки, executions, память
- [Интеграция агентов](docs/ru/agent-integration.md) - MCP-эндпоинты, инструменты, скиллы, вывод установщика
- [Решение проблем](docs/ru/troubleshooting.md) - типовые проблемы и решения
- [Техническая спецификация](docs/ru/technical-specification.md) - нормативная спецификация MVP
- [Журнал уточнения требований](docs/ru/requirements-discussion.md) - история решений
- [Исходное ТЗ](docs/ru/mcp-flow.md) - исторический исходный документ

---

## Статус

Фундамент MVP в активной разработке (см. [этапы MVP](docs/ru/technical-specification.md#25-mvp) в
спецификации). Демон, хранение, слой MCP, установщик и самодостаточные тесты готовы; далее
достраиваются доска PWA и автоматический цикл исполнения.
