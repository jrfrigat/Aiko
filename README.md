# Aiko - Local AI Development Orchestrator

<p align="center"><img src="assets/banner.svg" alt="Aiko - AI kanban orchestrator" width="640" /></p>

<p align="center">🌐 <b>English</b> - <a href="README.ru.md">Русский</a></p>

[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![Release](https://img.shields.io/github/v/release/jrfrigat/Aiko?sort=semver)](https://github.com/jrfrigat/Aiko/releases/latest)
[![CI](https://github.com/jrfrigat/Aiko/actions/workflows/ci.yml/badge.svg)](https://github.com/jrfrigat/Aiko/actions/workflows/ci.yml)
[![CodeQL](https://github.com/jrfrigat/Aiko/actions/workflows/codeql.yml/badge.svg)](https://github.com/jrfrigat/Aiko/actions/workflows/codeql.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![Status](https://img.shields.io/badge/status-MVP%20foundation-orange)](docs/en/technical-specification.md)

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
  board projections (Tasks / Stories / Combined), card editor, artifacts and executions; the
  interface language follows the browser, English and Russian ship, and any other language falls
  back to English
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

## Install

Windows 10/11, x64. The release is self-contained, so neither the .NET SDK nor the .NET runtime is
required:

```powershell
irm https://raw.githubusercontent.com/jrfrigat/Aiko/main/scripts/install.ps1 | iex
```

The installer downloads the newest [`aiko-<version>-win-x64.zip`](https://github.com/jrfrigat/Aiko/releases/latest),
unpacks it into `%LOCALAPPDATA%\Aiko\bin` (CLI `aiko`, stdio proxy `aiko-stdio`, the daemon in
`server\`) and adds that directory to the user `PATH`. Nothing is installed machine-wide and no
administrator rights are needed.

At the end it asks which agents to connect (Claude Code, Codex, Cursor, ZCode) and writes their
global MCP entry, `/aiko-*` skills and shared memory; answering with Enter skips the question. Answer
it non-interactively with `-Agents claude-code,codex`, or skip it with `-NoAgentSetup`.

```powershell
aiko serve     # start the daemon (loopback only; prefers port 24560)
aiko ui        # pair the browser with the daemon and open the board
aiko doctor    # check the installation; `aiko repair --fix` applies the fixes it names
```

Pin a specific release or choose another directory by fetching the script into a scriptblock first:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/jrfrigat/Aiko/main/scripts/install.ps1))) `
    -Version v0.3.0 -InstallDir D:\Tools\Aiko
```

Re-running the installer is the update path: binaries are replaced, project data and settings are
kept. To remove Aiko, delete `%LOCALAPPDATA%\Aiko\bin` and drop it from the user `PATH`; `.aiko`
directories and the database are never deleted automatically.

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

84 xUnit facts across four suites: domain rules, infrastructure/file/SQLite behavior, daemon
integration (MCP tools plus the REST API, its status codes and the loopback/Host/Origin guard) and the
client JSON contract. The integration suite boots its own daemon on a random port with an isolated
database - no manual orchestration needed.

---

## Run from source

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```sh
git clone https://github.com/jrfrigat/Aiko
cd Aiko

# build everything (server, PWA, CLI, stdio proxy, tests)
dotnet build Aiko.slnx

# run the daemon (serves the PWA and the MCP endpoints)
dotnet run --project src/Aiko.Server
```

The daemon prefers port **24560**; if it is busy it picks a free port from `18000-18999` and
remembers the choice in `settings.json` next to the database. Open the UI at
`http://127.0.0.1:24560` and register a project through the interface, or from the terminal:

```sh
curl -X POST http://127.0.0.1:24560/api/v1/projects/initialize \
     -H "Content-Type: application/json" \
     -d "{ \"rootPath\": \"C:/path/to/your/project\" }"
# => { "id": "<projectId>", ... }   MCP: http://127.0.0.1:24560/mcp/projects/<projectId>
```

`.\install.ps1` in the repository root publishes this checkout into `%LOCALAPPDATA%\Aiko\bin`, so
`aiko serve` and `aiko status` behave exactly as in an installed release.

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

The stable tool set (27 tools): project context, card CRUD and linking, taking a card, the full
stage-execution life cycle (start / report progress / request scope expansion / complete / pause /
handoff / resume / report agent state), memory search and store, project registration and listing,
diagnostics and reindex, and opening the UI. Tool
descriptions instruct agents to fetch the project context first; installed skills, rules and
`AGENTS.md` blocks reinforce it per agent.

### Skills

Written by `aiko agent install`. Project scope (`--project <id>`):

| Skill | Does |
| :-- | :-- |
| `/aiko-story-create`, `/aiko-task-create` | Create a story or a task card |
| `/aiko-next-stage` | Move the current card to its next stage |
| `/aiko-analyze`, `/aiko-implement`, `/aiko-review` | Run a pipeline stage and report progress |
| `/aiko-scope` | Ask for a scope expansion |
| `/aiko-handoff` | Hand the stage over to another agent |
| `/aiko-complete` | Complete the stage with its files and artifacts |
| `/aiko-memory` | Store and search project memory |
| `/aiko-status` | Summarize what is in progress |
| `/aiko-ui` | Open the board for this project |

User scope (`--scope user`), available without a project open:

| Skill | Does |
| :-- | :-- |
| `/aiko-init` | Register the current directory as a project |
| `/aiko-list-projects` | List the registered projects |
| `/aiko-status` | Daemon health, data directory, port |
| `/aiko-doctor` | Diagnose the installation (changes nothing) |
| `/aiko-repair` | Apply the fixes the diagnosis named |
| `/aiko-agents` | List, install and uninstall the agents' integrations |
| `/aiko-token` | Show the local access token |
| `/aiko-backup` | Back up a project's `.aiko` tree |
| `/aiko-ui` | Open the UI |

[Agent Integration](docs/en/agent-integration.md) has the full skill → MCP tool → UI table, including
what the UI cannot do yet.

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
| `tests/*` | xUnit suites: Domain.Specs, Infrastructure.Specs, Mcp.Specs (self-hosted), Pwa.Specs (JSON contract) |
| `scripts/install.ps1` | Release installer behind the one-line install command |
| `.github/workflows/` | `ci.yml` (build, test, lint the installers) and `release.yml` (win-x64 assets) |

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
- [Contributing](CONTRIBUTING.md) - build, test and pull request expectations
- [License](LICENSE) - MIT

---

## Status

MVP foundation under active development (see the [MVP stages](docs/en/technical-specification.md#25-mvp)
in the spec). The daemon, storage, MCP layer, installer and the self-contained test suites are in
place; the PWA board and the automatic execution loop are being built out next.
