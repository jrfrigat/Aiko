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

## The tool set (27 tools)

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

`/aiko-init`, `/aiko-list-projects`, `/aiko-status`, `/aiko-doctor`, `/aiko-repair`, `/aiko-agents`,
`/aiko-token`, `/aiko-backup`, `/aiko-ui`.

The behavior contract: any work item starts with a card; read context before acting; warn before
changing files outside the declared scope; report progress, actual files and commits through Aiko.

## Parity: skill, tool, UI

The same work is reachable from an agent, over MCP and - for most of it - from the board. Where the UI
has no path yet, the table says so rather than pretending the sets are already equal.

| Action | Agent skill | MCP tool | UI |
| :-- | :-- | :-- | :-- |
| Register a project | `/aiko-init` | `aiko_init_project` | Dashboard - *Add project* (with the folder browser) |
| List projects | `/aiko-list-projects` | `aiko_list_projects` | Dashboard - project list |
| Open the board | `/aiko-ui` | `aiko_open_ui` | `aiko ui`, or the URL in the app bar |
| Create a story / task | `/aiko-story-create`, `/aiko-task-create` | `aiko_create_card`, `aiko_create_card_in_project` | Board - *Create card* |
| Read the board | `/aiko-status` | `aiko_list_cards`, `aiko_get_card` | Board and card drawer |
| Edit a card | — | `aiko_update_card` | Card drawer - *Save card* |
| Move a card between stages | `/aiko-next-stage` | `aiko_move_card` | Drag a card between columns |
| Start a stage | — | `aiko_start_stage` | — (the board shows the resulting state) |
| Report progress | `/aiko-analyze`, `/aiko-implement`, `/aiko-review` | `aiko_report_progress` | Card drawer - execution history |
| Request scope expansion | `/aiko-scope` | `aiko_request_scope_expansion` | Card drawer - declared vs actual files |
| Hand off to another agent | `/aiko-handoff` | `aiko_handoff_execution` | Card drawer - execution history |
| Complete a stage | `/aiko-complete` | `aiko_complete_stage` | Drag to the next column |
| Record and search memory | `/aiko-memory` | `aiko_store_memory`, `aiko_search_memory` | — (not in the UI yet) |
| Edit the pipeline | — | — | Workflow page |
| Read settings | — | `aiko_get_settings` | Settings page |
| Change settings | — (planned `/aiko-settings`) | — (planned `aiko_update_settings`) | Settings page |
| Rebuild projections | — | `aiko_reindex` | — (`aiko reindex`) |
| Diagnose the installation | `/aiko-doctor` | `aiko_doctor` | — (`aiko doctor`) |
| Repair the installation | `/aiko-repair` | — | — (`aiko repair --fix`) |
| Back up a project | `/aiko-backup` | `aiko_backup` | — |
| Show the access token | `/aiko-token` | `aiko_token` | — (`aiko token show`) |
| Manage agent integrations | `/aiko-agents` | — (`aiko agent list` / `install` / `uninstall`) | — |

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
