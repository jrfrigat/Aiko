# Aiko - Local AI Development Orchestrator

<p align="center"><img src="assets/logo.svg" alt="Aiko" width="96" /></p>

<p align="center">🌐 <b>English</b> - <a href="README.ru.md">Русский</a></p>

[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![Status](https://img.shields.io/badge/status-MVP%20foundation-orange)](docs/en/technical-specification.md)
[![Tests](https://img.shields.io/badge/tests-54%20xUnit-green)](#tests)

Aiko (AI kanban orchestrator) is a **local-first orchestrator for AI-assisted development**: one
loopback daemon that gives Claude Code, Codex, Cursor and ZCode a shared project context, a
configurable Kanban pipeline, durable memory and a stable MCP contract - while you keep every file
on disk, in Git-friendly Markdown and JSON.

Aiko does not replace your agents. It connects them: cards move through user-defined workflow
stages, each stage can run in a different agent, and an unfinished stage (for example, after a
rate limit) hands off to another agent without losing history.

**File-first storage - one typed card graph - effective priorities - agent handoffs with full attempt history - durable project memory - loopback-only by design**

---

## Features

- **Local daemon, one per user** - a single ASP.NET Core process serves every registered project:
  REST API, project-scoped MCP over Streamable HTTP, health endpoints and the PWA
- **Kanban PWA** on Blazor WebAssembly ([Flare.Blazor](https://github.com/jrfrigat/Flare)) with
  board projections (Tasks / Stories / Combined), card editor, artifacts and executions
- **Cards as folders** - every Story/Task is a directory with a `card.json` (optimistic revisions)
  and Markdown artifacts; the whole `.aiko` tree is readable, diffable and Git-friendly
- **Typed card graph** - `implements`, `parent-child`, `blocks` (cycle-checked) and symmetric
  `relates-to` relations; blocks drive scheduling
- **Effective priorities** - each card has its own score; task priorities blend in the maximum
  parent value with configurable weights (versioned formula)
- **Execution loop** - `StageExecution` owns the workspace; `AgentAttempt` records each agent's
  run, so handoff, resume, pause and rate-limit states never lose history
- **Durable memory** - decisions, conventions and lessons live in `.aiko/memory` as Markdown and
  are searchable through an SQLite FTS5 index
- **Unified agent installer** - discovers Claude Code, Codex, Cursor and ZCode installations and
  applies idempotent, user-config-preserving project configuration (MCP entries, managed blocks,
  owned skills/commands) with per-adapter plans and surgical uninstall
- **SQLite projections, rebuildable** - the global SQLite database is an index and runtime state;
  deleting it is safe, `reindex` rebuilds everything from `.aiko` files
- **Security by default** - loopback bind only, `Host`/`Origin` validation against DNS rebinding
  and remote-browser origins, no wildcard CORS

---

## Quick Start

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```sh
git clone <repository-url>
cd Aiko

# build everything (server, PWA, stdio proxy, tests)
dotnet build Aiko.slnx

# run the daemon (serves the PWA and the MCP endpoints)
dotnet run --project src/Aiko.Server
```

The daemon prefers port **24560**; if it is busy it picks a free port from `18000-18999` and
remembers the choice in `settings.json` next to the database. Open the UI at
`http://127.0.0.1:24560` and register a project through the UI (or the API below).

Register a project and get its MCP endpoint:

```sh
curl -X POST http://127.0.0.1:24560/api/v1/projects/initialize \
     -H "Content-Type: application/json" \
     -d "{ \"rootPath\": \"C:/path/to/your/project\" }"
# => { "id": "<projectId>", ... }   MCP: http://127.0.0.1:24560/mcp/projects/<projectId>
```

### Configuration

| Environment variable | Purpose |
| :-- | :-- |
| `AIKO_PORT` | Explicit port for this launch; validated and persisted |
| `AIKO_URL` | Explicit loopback origin (overrides port selection) |
| `AIKO_DATABASE` | Path to the SQLite database (default `%LocalAppData%/Aiko/aiko.db`) |

### Tests

```sh
dotnet test Aiko.slnx
```

54 xUnit facts across three suites: domain rules, infrastructure/file/SQLite behavior, and MCP
integration. The MCP suite boots its own daemon on a random port with an isolated database -
no manual orchestration needed.

---

## How agents connect

Each project gets a dedicated Streamable HTTP MCP endpoint:

```text
http://127.0.0.1:<port>/mcp/projects/<projectId>
```

Clients without reliable Streamable HTTP use the thin stdio proxy (no second store, raw transport
forwarding, loopback-only):

```text
aiko-stdio --url http://127.0.0.1:<port>/mcp/projects/<projectId>
```

The stable tool set (29 tools): project context, card CRUD and linking, taking a card, the full
stage-execution life cycle (start / report progress / request scope expansion / complete / pause /
handoff / resume / report agent state), memory search and store, and opening the UI. Tool
descriptions instruct agents to fetch the project context first; installed skills, rules and
`AGENTS.md` blocks reinforce it per agent.

---

## Repository layout

| Project | Responsibility |
| :-- | :-- |
| `src/Aiko.Domain` | Cards, relations, workflows, priorities, executions - no I/O, no agents |
| `src/Aiko.Application` | Use cases and ports (stores, coordinator, installer contracts) |
| `src/Aiko.Infrastructure` | File stores, SQLite projections, FTS5 memory, agent adapters |
| `src/Aiko.Server` | ASP.NET Core daemon: REST API, MCP endpoints, PWA hosting |
| `src/Aiko.Pwa` | Blazor WebAssembly PWA on Flare.Blazor |
| `src/Aiko.StdioProxy` | Short-lived stdio <-> Streamable HTTP MCP proxy |
| `tests/*` | xUnit suites: Domain.Specs, Infrastructure.Specs, Mcp.Specs (self-hosted) |

---

## Documentation

- [Installation](docs/en/installation.md) - install, run, configure, update and uninstall
- [Getting Started](docs/en/getting-started.md) - first project in a few minutes
- [User Guide](docs/en/user-guide.md) - board, cards, workflows, settings, executions, memory
- [Agent Integration](docs/en/agent-integration.md) - MCP endpoints, tools, skills, installer output
- [Troubleshooting](docs/en/troubleshooting.md) - common problems and fixes
- [Technical Specification](docs/en/technical-specification.md) - the normative MVP specification
- [Requirements Discussion](docs/en/requirements-discussion.md) - the journal of decisions
- [Original Terms of Reference](docs/en/mcp-flow.md) - the historical source document

---

## Status

MVP foundation under active development (see the [MVP stages](docs/en/technical-specification.md#25-mvp)
in the spec). The daemon, storage, MCP layer, installer and the self-contained test suites are in
place; the PWA board and the automatic execution loop are being built out next.
