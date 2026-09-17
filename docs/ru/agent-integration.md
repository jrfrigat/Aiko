# Aiko - Интеграция агентов

Aiko связывает Claude Code, Codex, Cursor и ZCode через MCP-эндпоинт проекта.

## MCP-эндпоинты

Каждый проект получает Streamable HTTP MCP-эндпоинт:

```text
http://127.0.0.1:<port>/mcp/projects/<projectId>
```

Daemon-level эндпоинт (без проекта) отдаёт глобальные операции:

```text
http://127.0.0.1:<port>/mcp
```

Клиенты без надёжного Streamable HTTP используют stdio-прокси:

```text
aiko-stdio --url http://127.0.0.1:<port>/mcp/projects/<projectId>
```

## Набор инструментов (27 tools)

- **Контекст проекта** - `aiko_get_project_context`, `aiko_open_ui`.
- **Карточки** - `aiko_list_cards`, `aiko_get_card`, `aiko_create_card`, `aiko_update_card`,
  `aiko_move_card`, `aiko_take_card`, `aiko_link_cards`.
- **Execution** - `aiko_start_stage`, `aiko_report_progress`, `aiko_request_scope_expansion`,
  `aiko_complete_stage`, `aiko_pause_execution`, `aiko_handoff_execution`,
  `aiko_resume_execution`, `aiko_report_agent_state`, `aiko_report_commit`, `aiko_approve_commit`.
- **Память** - `aiko_search_memory`, `aiko_store_memory`.
- **Daemon (глобальные)** - `aiko_init_project`, `aiko_list_projects`,
  `aiko_create_card_in_project`, `aiko_doctor`, `aiko_reindex`, `aiko_get_settings`, `aiko_token`,
  `aiko_backup`.

Описания инструментов требуют сначала читать контекст проекта; установленные скиллы и правила
подкрепляют это для каждого агента.

## Скиллы и команды

Project-scoped скиллы/команды (устанавливаются `aiko agent install --project <id>`):

`/aiko-story-create`, `/aiko-task-create`, `/aiko-next-stage`, `/aiko-analyze`,
`/aiko-implement`, `/aiko-review`, `/aiko-complete`, `/aiko-scope`, `/aiko-handoff`,
`/aiko-memory`, `/aiko-status`, `/aiko-ui`.

Глобальные скиллы/команды (устанавливаются `aiko agent install --scope user`):

`/aiko-init`, `/aiko-list-projects`, `/aiko-status`, `/aiko-doctor`, `/aiko-repair`, `/aiko-agents`,
`/aiko-token`, `/aiko-backup`, `/aiko-ui`.

Глобальный scope — это подключение на уровне машины: именно его карточка *Agents* на дашборде показывает
как **подключён**, а кнопки «Подключить» / «Отключить» записывают и удаляют ровно эти файлы. MCP-запись
для проекта — отдельная и своя у каждого проекта, поэтому агент может быть подключён глобально и всё равно
требовать `aiko agent install --project <id>` для конкретного проекта.

Контракт поведения: любая работа начинается с карточки; читайте контекст перед действиями;
предупреждайте об изменениях вне declared scope; сообщайте прогресс, фактические файлы и коммиты
через Aiko.

## Паритет: скилл, tool, UI

Одну и ту же работу можно выполнить из агента, через MCP и - большую часть - с доски. Там, где пути в
UI пока нет, таблица говорит это прямо, а не делает вид, что наборы уже совпадают.

| Действие | Скилл агента | MCP tool | UI |
| :-- | :-- | :-- | :-- |
| Зарегистрировать проект | `/aiko-init` | `aiko_init_project` | Дашборд - *Добавить проект* (с обзором папок) |
| Список проектов | `/aiko-list-projects` | `aiko_list_projects` | Дашборд - список проектов |
| Открыть доску | `/aiko-ui` | `aiko_open_ui` | `aiko ui` или адрес в верхней панели |
| Создать story / task | `/aiko-story-create`, `/aiko-task-create` | `aiko_create_card`, `aiko_create_card_in_project` | Доска - *Создать карточку* |
| Прочитать доску | `/aiko-status` | `aiko_list_cards`, `aiko_get_card` | Доска и карточка |
| Изменить карточку | — | `aiko_update_card` | Карточка - *Сохранить* |
| Перевести карточку по этапам | `/aiko-next-stage` | `aiko_move_card` | Перетаскивание между колонками |
| Запустить этап | — | `aiko_start_stage` | — (доска покажет результат) |
| Отчитаться о прогрессе | `/aiko-analyze`, `/aiko-implement`, `/aiko-review` | `aiko_report_progress` | Карточка - история исполнения |
| Запросить расширение scope | `/aiko-scope` | `aiko_request_scope_expansion` | Карточка - declared и actual файлы |
| Передать этап другому агенту | `/aiko-handoff` | `aiko_handoff_execution` | Карточка - история исполнения |
| Завершить этап | `/aiko-complete` | `aiko_complete_stage` | Перетаскивание в следующую колонку |
| Записать и найти память | `/aiko-memory` | `aiko_store_memory`, `aiko_search_memory` | — (в UI пока нет) |
| Изменить конвейер | — | — | Страница Workflow |
| Прочитать настройки | — | `aiko_get_settings` | Страница Settings |
| Изменить настройки | — (планируется `/aiko-settings`) | — (планируется `aiko_update_settings`) | Страница Settings |
| Перестроить проекции | — | `aiko_reindex` | — (`aiko reindex`) |
| Диагностика установки | `/aiko-doctor` | `aiko_doctor` | — (`aiko doctor`) |
| Починка установки | `/aiko-repair` | — | — (`aiko repair --fix`) |
| Резервная копия проекта | `/aiko-backup` | `aiko_backup` | — |
| Показать токен доступа | `/aiko-token` | `aiko_token` | — (`aiko token show`) |
| Управление интеграциями агентов | `/aiko-agents` | — (`aiko agent list` / `install` / `uninstall`) | Дашборд - карточка *Agents*: обнаружение и Подключить / Отключить |

## Что пишет установщик

В проект (`aiko agent install --project <id>`):

| Агент | Файлы |
| :-- | :-- |
| Claude Code | `.mcp.json`, `.claude/skills/aiko/SKILL.md`, `.claude/commands/aiko-*.md` |
| Codex | `.codex/config.toml` (`mcp_servers.aiko`), `.agents/skills/aiko/SKILL.md`, блок `AGENTS.md` |
| Cursor | `.cursor/mcp.json`, `.cursor/rules/aiko.mdc` |
| ZCode | `.zcode/config.json` (нативный `mcp.servers`), `.zcode/skills/aiko/SKILL.md`, `.zcode/commands/aiko-*.md` |

Глобально, для всего пользователя (`aiko agent install --scope user`, это и запускает установщик):

| Агент | Файлы |
| :-- | :-- |
| Claude Code | `~/.claude/skills/aiko/SKILL.md`, `~/.claude/commands/aiko-*.md` |
| Codex | `~/.codex/skills/aiko/SKILL.md`, `~/.agents/skills/aiko/SKILL.md` |
| Cursor | `~/.cursor/rules/aiko.mdc` |
| ZCode | `~/.zcode/skills/aiko/SKILL.md`, `~/.zcode/commands/aiko-*.md` |

Codex читает user-scope скиллы из собственного корня `~/.codex/skills` (его встроенные скиллы лежат в
`~/.codex/skills/.system`), поэтому глобальный скилл пишется туда; копия в переносимом
`~/.agents/skills` остаётся для раскладок, которые читают это дерево. Переменная `AIKO_USER_HOME`
переключает установку в другой домашний каталог.

Установка идемпотентна и сохраняет ваши настройки; удаление убирает только Aiko-управляемый
контент (MCP-записи, managed-блоки и файлы с маркером владения Aiko).

## Rate limit и handoff

Когда агент сообщает `rate-limited`, передайте execution другому агенту через
`aiko_handoff_execution`. Полная история попыток сохраняется.
