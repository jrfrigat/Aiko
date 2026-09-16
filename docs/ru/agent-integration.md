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
  `aiko_create_card_in_project`, `aiko_doctor`, `aiko_reindex`, `aiko_get_settings`.

Описания инструментов требуют сначала читать контекст проекта; установленные скиллы и правила
подкрепляют это для каждого агента.

## Скиллы и команды

Project-scoped скиллы/команды (устанавливаются `aiko agent install --project <id>`):

`/aiko-story-create`, `/aiko-task-create`, `/aiko-next-stage`, `/aiko-analyze`,
`/aiko-implement`, `/aiko-review`, `/aiko-complete`, `/aiko-scope`, `/aiko-handoff`,
`/aiko-memory`, `/aiko-status`, `/aiko-ui`.

Глобальные скиллы/команды (устанавливаются `aiko agent install --scope user`):

`/aiko-init`, `/aiko-list-projects`, `/aiko-status`, `/aiko-ui`.

Контракт поведения: любая работа начинается с карточки; читайте контекст перед действиями;
предупреждайте об изменениях вне declared scope; сообщайте прогресс, фактические файлы и коммиты
через Aiko.

## Что пишет установщик

| Агент | Файлы |
| :-- | :-- |
| Claude Code | `.mcp.json`, `.claude/skills/aiko/SKILL.md`, `.claude/commands/aiko-*.md` |
| Codex | `.codex/config.toml` (`mcp_servers.aiko`), `.agents/skills/aiko/SKILL.md`, блок `AGENTS.md` |
| Cursor | `.cursor/mcp.json`, `.cursor/rules/aiko.mdc` |
| ZCode | `.zcode/config.json` (нативный `mcp.servers`), `.zcode/skills/aiko/SKILL.md`, `.zcode/commands/aiko-*.md` |

Установка идемпотентна и сохраняет ваши настройки; удаление убирает только Aiko-управляемый
контент (MCP-записи, managed-блоки и файлы с маркером владения Aiko).

## Rate limit и handoff

Когда агент сообщает `rate-limited`, передайте execution другому агенту через
`aiko_handoff_execution`. Полная история попыток сохраняется.
