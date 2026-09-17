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

## Набор инструментов

- **Контекст проекта** - `aiko_get_project_context`, `aiko_open_ui`.
- **Карточки** - `aiko_list_cards`, `aiko_get_card`, `aiko_create_card`, `aiko_update_card`,
  `aiko_estimate_card`, `aiko_move_card`, `aiko_take_card`, `aiko_link_cards`.
- **Execution** - `aiko_start_stage`, `aiko_report_progress`, `aiko_request_scope_expansion`,
  `aiko_complete_stage`, `aiko_pause_execution`, `aiko_handoff_execution`,
  `aiko_resume_execution`, `aiko_report_agent_state`, `aiko_report_commit`, `aiko_approve_commit`.
- **Память** - `aiko_search_memory`, `aiko_store_memory`.
- **Daemon (глобальные)** - `aiko_init_project`, `aiko_list_projects`, `aiko_list_templates`,
  `aiko_create_card_in_project`, `aiko_doctor`, `aiko_reindex`, `aiko_get_settings`, `aiko_token`,
  `aiko_backup`.

Описания инструментов требуют сначала читать контекст проекта; установленные скиллы и правила
подкрепляют это для каждого агента.

## Скиллы и команды

Project-scoped скиллы/команды (устанавливаются `aiko agent install --project <id>`):

`/aiko-create`, `/aiko-create-sub`, `/aiko-estimate`, `/aiko-run`, `/aiko-scope`, `/aiko-handoff`,
`/aiko-memory`, `/aiko-status`, `/aiko-ui`.

`/aiko-create <тип> <описание>` принимает тип карточки первым аргументом (`/aiko-create bug Не работает
выпадающий список`) и определяет его по контексту проекта, поэтому покрывает любой тип без перегенерации.
Карточку называет Aiko - человек никогда не придумывает id - и она всегда создаётся в этапе backlog своего
типа: карточка, которую ещё не проработали, не должна начинаться нигде больше. В том же проходе агент
оценивает карточку: определяет шаг размера и все критерии по описанию и вызывает `aiko_estimate_card`,
поэтому карточка, созданная командой, не остаётся без оценки. Рядом демон ставит по
одной команде `/aiko-create-<тип>` на каждый тип карточек проекта - `/aiko-create-story`,
`/aiko-create-task` и по одной на каждый тип, добавленный в редакторе workflow. Эти команды - проекция
workflow проекта: они перезаписываются при создании и удалении типа, и только для агентов, уже
подключённых к этому проекту.

`/aiko-create-sub <parentCardId> <тип> <описание>` - то же создание, но под уже существующей карточкой:
агент создаёт подзадачу, связывает её через `aiko_link_cards`, передавая родителя как `sourceCardId`, новую
карточку - как `targetCardId`, а `parent-child` - как тип связи, и оценивает её. Ребро направлено от
родителя к ребёнку; именно так доска читает «эта карточка лежит под той».

`/aiko-estimate <cardId>` пересчитывает оценку карточки отдельно - именно это действие *Попросить оценить*
на странице карточки передаёт агенту. Агент читает карточку и контекст проекта (описания шагов сетки
размеров и диапазоны каждого критерия) и вызывает `aiko_estimate_card` с шагом размера и оценками.

`/aiko-run <cardId> [stageId]` запускает карточку: берёт её текущий этап и инструкцию этого этапа из
контекста проекта, выполняет работу, отчитывается о прогрессе и завершает этап. Указанный `stageId`
сначала переводит карточку туда - поэтому отдельной команды перевода больше нет. Ничего в ней не называет
этап по имени, поэтому она работает в любом конвейере: заменённые ею команды (`/aiko-analyze`,
`/aiko-implement`, `/aiko-review`, `/aiko-complete` и `/aiko-next-stage`) называли этапы шаблона по
умолчанию - то же зашивание, которого у типов карточек больше нет.

Глобальные скиллы/команды (устанавливаются `aiko agent install --scope user`):

`/aiko-init [templateId]`, `/aiko-list-projects`, `/aiko-status`, `/aiko-doctor`, `/aiko-repair`, `/aiko-agents`,
`/aiko-token`, `/aiko-backup`, `/aiko-ui`.

Глобальный scope — это подключение на уровне машины: именно его карточка *Agents* на дашборде показывает
как **подключён**, а кнопки «Подключить» / «Отключить» записывают и удаляют ровно эти файлы. MCP-запись
для проекта — отдельная и своя у каждого проекта, поэтому агент может быть подключён глобально и всё равно
требовать `aiko agent install --project <id>` для конкретного проекта.

Контракт поведения: любая работа начинается с карточки; читайте контекст перед действиями;
предупреждайте об изменениях вне declared scope; сообщайте прогресс, фактические файлы и коммиты
через Aiko. Контекст проекта перечисляет все типы карточек проекта и этапы их конвейеров, поэтому тип,
добавленный в редакторе workflow, агент может создавать сразу. Там же лежит инструкция инициализации
проекта, если она была в шаблоне - именно её выполнение завершает запуск `/aiko-init`.

## Аутентификация

Демон требует токен доступа на своём MCP-эндпоинте, поэтому **каждая сгенерированная MCP-запись несёт
его** — без токена демон отвечает `401`, и агент просто не видит Aiko:

- клиенты, чья конфигурация умеет заголовки (Claude Code, Cursor, ZCode), получают
  `"headers": { "Authorization": "Bearer <token>" }` — в том же виде, в каком это пишет CLI самого
  клиента (Claude Code заодно получает свой тег `"type": "http"`);
- Codex не умеет хранить заголовок буквально, поэтому в его записи указано имя переменной окружения —
  ровно как пишет его собственный CLI: `bearer_token_env_var = "AIKO_TOKEN"`. Экспортируйте `AIKO_TOKEN`
  со значением из `aiko token show` в той оболочке, из которой запускаете Codex.

Токен лежит в этих файлах, поэтому в коммит они попадать не должны. Инициализация проекта добавляет их
в `.gitignore` (`/.mcp.json`, `/.cursor/mcp.json`, `/.zcode/config.json` вместе с записью `.aiko`,
которой управляет git-политика); для проекта, созданного раньше, эти строки добавляются руками.

`aiko doctor` сообщает о конфигурации, у которой адрес верный, а учётных данных нет, а
`aiko repair --fix` её перезаписывает.

## Паритет: скилл, tool, UI

Одну и ту же работу можно выполнить из агента, через MCP и - большую часть - с доски. Там, где пути в
UI пока нет, таблица говорит это прямо, а не делает вид, что наборы уже совпадают.

| Действие | Скилл агента | MCP tool | UI |
| :-- | :-- | :-- | :-- |
| Зарегистрировать проект по выбранному шаблону | `/aiko-init [templateId]` | `aiko_list_templates`, `aiko_init_project` | Дашборд - *Добавить проект* (обзор папок + выбор шаблона) |
| Список проектов | `/aiko-list-projects` | `aiko_list_projects` | Дашборд - список проектов |
| Открыть доску | `/aiko-ui` | `aiko_open_ui` | `aiko ui` или адрес в верхней панели |
| Создать карточку любого типа проекта | `/aiko-create <тип> <описание>`, `/aiko-create-<тип>` | `aiko_create_card`, `aiko_create_card_in_project`, `aiko_estimate_card` | Доска - *Создать карточку* |
| Создать подзадачу под карточкой | `/aiko-create-sub <parentCardId> <тип> <описание>` | `aiko_create_card`, `aiko_link_cards`, `aiko_estimate_card` | Доска - *Создать карточку*, связь на странице карточки |
| Прочитать доску | `/aiko-status` | `aiko_list_cards`, `aiko_get_card` | Доска, и карточка как отдельная страница |
| Изменить карточку | — | `aiko_update_card` | Страница карточки - *Сохранить* |
| Оценить размер и оценки карточки | `/aiko-estimate <cardId>` | `aiko_estimate_card` | Страница карточки - *Попросить оценить* |
| Перевести карточку по этапам | `/aiko-run <cardId> <stageId>` | `aiko_move_card` | Перетаскивание между колонками |
| Запустить этап | — | `aiko_start_stage` | — (доска покажет результат) |
| Отчитаться о прогрессе | `/aiko-run` | `aiko_report_progress` | Страница карточки - история исполнения |
| Запросить расширение scope | `/aiko-scope` | `aiko_request_scope_expansion` | Страница карточки - declared и actual файлы |
| Передать этап другому агенту | `/aiko-handoff` | `aiko_handoff_execution` | Страница карточки - история исполнения |
| Завершить этап | `/aiko-run` | `aiko_complete_stage` | Перетаскивание в следующую колонку |
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
