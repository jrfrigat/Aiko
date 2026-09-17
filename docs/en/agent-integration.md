# Aiko - Agent Integration

Aiko connects Claude Code, Codex, Cursor and ZCode through a per-project MCP endpoint.

## MCP endpoints

Each project gets a Streamable HTTP MCP endpoint:

```text
http://127.0.0.1:<port>/mcp/projects/<projectId>
```

A daemon-level endpoint (no project) exposes global operations:

```text
http://127.0.0.1:<port>/mcp
```

Clients without reliable Streamable HTTP use the stdio proxy:

```text
aiko-stdio --url http://127.0.0.1:<port>/mcp/projects/<projectId>
```

## The tool set (29 tools)

- **Project context** - `aiko_get_project_context`, `aiko_open_ui`.
- **Cards** - `aiko_list_cards`, `aiko_get_card`, `aiko_create_card`, `aiko_update_card`,
  `aiko_move_card`, `aiko_take_card`, `aiko_link_cards`.
- **Execution** - `aiko_start_stage`, `aiko_report_progress`, `aiko_request_scope_expansion`,
  `aiko_complete_stage`, `aiko_pause_execution`, `aiko_handoff_execution`,
  `aiko_resume_execution`, `aiko_report_agent_state`, `aiko_report_commit`, `aiko_approve_commit`.
- **Memory** - `aiko_search_memory`, `aiko_store_memory`.
- **Daemon (global)** - `aiko_init_project`, `aiko_list_projects`, `aiko_create_card_in_project`,
  `aiko_doctor`, `aiko_reindex`, `aiko_get_settings`, `aiko_token`, `aiko_backup`.

Tool descriptions instruct agents to fetch the project context first; the installed skills and
rules reinforce this per agent.

## Skills and commands

Project-scoped skills/commands (installed with `aiko agent install --project <id>`):

`/aiko-story-create`, `/aiko-task-create`, `/aiko-next-stage`, `/aiko-analyze`,
`/aiko-implement`, `/aiko-review`, `/aiko-complete`, `/aiko-scope`, `/aiko-handoff`,
`/aiko-memory`, `/aiko-status`, `/aiko-ui`.

Global skills/commands (installed with `aiko agent install --scope user`):

`/aiko-init`, `/aiko-list-projects`, `/aiko-status`, `/aiko-ui`.

The behavior contract: any work item starts with a card; read context before acting; warn before
changing files outside the declared scope; report progress, actual files and commits through Aiko.

## What the installer writes

Per project (`aiko agent install --project <id>`):

| Agent | Files |
| :-- | :-- |
| Claude Code | `.mcp.json`, `.claude/skills/aiko/SKILL.md`, `.claude/commands/aiko-*.md` |
| Codex | `.codex/config.toml` (`mcp_servers.aiko`), `.agents/skills/aiko/SKILL.md`, `AGENTS.md` block |
| Cursor | `.cursor/mcp.json`, `.cursor/rules/aiko.mdc` |
| ZCode | `.zcode/config.json` (native `mcp.servers`), `.zcode/skills/aiko/SKILL.md`, `.zcode/commands/aiko-*.md` |

Globally, for every user (`aiko agent install --scope user`, which is what the installer runs):

| Agent | Files |
| :-- | :-- |
| Claude Code | `~/.claude/skills/aiko/SKILL.md`, `~/.claude/commands/aiko-*.md` |
| Codex | `~/.codex/skills/aiko/SKILL.md`, `~/.agents/skills/aiko/SKILL.md` |
| Cursor | `~/.cursor/rules/aiko.mdc` |
| ZCode | `~/.zcode/skills/aiko/SKILL.md`, `~/.zcode/commands/aiko-*.md` |

Codex loads user-scope skills from its own root, `~/.codex/skills` (its built-ins live in
`~/.codex/skills/.system`), so the global skill is written there; the portable `~/.agents/skills` copy
is kept for layouts that read that tree. Set `AIKO_USER_HOME` to install into a different home
directory.

Installation is idempotent and preserves your own settings; uninstall removes only Aiko-managed
content (MCP entries, managed blocks, and files carrying the Aiko ownership marker).

## Rate limits and handoffs

When an agent reports `rate-limited`, hand the execution to another agent with
`aiko_handoff_execution`. The full attempt history is preserved.
