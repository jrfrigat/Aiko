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
  board projections (one per card type, plus Combined), a backlog screen, the card editor, artifacts
  and executions; the interface language follows the browser, English and Russian ship, and any other
  language falls back to English
- **Card types you define yourself** - a card type *is* a workflow: add an `Epic` with its own
  description, icon and colour right in the workflow editor, and it appears in the pickers, as a board
  section and in the backlog; every status column carries its own icon and colour too. The `backlog`
  column is standard and cannot be removed, and a dedicated **Backlog** screen gathers every card that
  has not been taken into work yet
- **A default template that is already worked out** - a new project starts from story and task pipelines in
  which every status carries an icon, a colour and an instruction saying what the agent does there, the
  card types come with their descriptions, and cards are scored by three criteria - `app-point`,
  `user-point` and `complete` - whose readiness score the agent re-calculates after every change
- **Cards as folders** - every card is a directory with a `card.json` (optimistic revisions)
  and Markdown artifacts; the whole `.aiko` tree is readable, diffable and Git-friendly
- **Readable project addresses** - a project gets a short id derived from its folder name (Cyrillic is
  transliterated), editable while it is created and used in every URL (`/p/aiko/board`); the generated
  GUID stays as the immutable key inside card files and agent endpoints, so cross-project links never break
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
- **Git, read through your own client** - the branch, the changes, the log and the diff of a card's files,
  read by running the `git` executable; a machine without it says "Git client unavailable" instead of
  failing, and Aiko never writes to the repository
- **Workflow sets you can author** - the pipelines and defaults a project starts from, plus what an agent
  must do right after creating it (the structure the project should have, for instance): captured from a
  project, exported and imported between machines, and applied to an existing project by an explicit action
- **Screens that answer questions** - a project page (cards per stage, weekly velocity, triage
  distribution, re-index), a card page (scope, acceptance criteria, live diff, discussion, runs) and a
  daemon page (uptime, runs and crashes, memory, agent bridges)

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
aiko serve stop                    # stop the daemon again, from any terminal
aiko ui        # pair the browser with the daemon and open the board
aiko doctor    # check the installation; `aiko repair --fix` applies the fixes it names
```

Pin a specific release or choose another directory by fetching the script into a scriptblock first:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/jrfrigat/Aiko/main/scripts/install.ps1))) `
    -Version v0.3.1 -InstallDir D:\Tools\Aiko
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

113 xUnit facts across four suites: domain rules, infrastructure/file/SQLite behavior, daemon
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

The stable tool set (31 tools): project context, card CRUD, linking, estimating and taking a card, the
full stage-execution life cycle (start / report progress / request scope expansion / complete / pause /
handoff / resume / report agent state), memory search and store, project registration and listing,
templates, diagnostics and reindex, settings, backup and opening the UI. Tool
descriptions instruct agents to fetch the project context first; installed skills, rules and
`AGENTS.md` blocks reinforce it per agent.

### Skills

Written by `aiko agent install`. Project scope (`--project <id>`):

| Skill | Does |
| :-- | :-- |
| `/aiko-create <type> <description>`, `/aiko-create-<type>` | Create a card of any type this project defines: Aiko names it, lands it in backlog and the agent estimates its size and scores |
| `/aiko-create-sub <parentCardId> <type> <description>` | Create a sub-card under a card: the same creation, plus the parent-child link |
| `/aiko-estimate <cardId>` | Estimate a card: the agent judges the size step and the criterion scores |
| `/aiko-run <cardId> [stageId]` | Run a card: do what its current stage asks for, report progress and complete it; a stage id moves it there first |
| `/aiko-scope` | Ask for a scope expansion |
| `/aiko-handoff` | Hand the stage over to another agent |
| `/aiko-memory` | Store and search project memory |
| `/aiko-status` | Summarize what is in progress |
| `/aiko-ui` | Open the board for this project |

User scope (`--scope user`), available without a project open:

| Skill | Does |
| :-- | :-- |
| `/aiko-init [name] [id]` | Register the current directory as a project; the id is the readable handle its URLs use |
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
- [User Guide](docs/en/user-guide.md) - board, cards, workflows, git, discussion, analytics, executions, memory
- [Agent Integration](docs/en/agent-integration.md) - MCP endpoints, tools, skills, installer output
- [Troubleshooting](docs/en/troubleshooting.md) - common problems and fixes
- [Technical Specification](docs/en/technical-specification.md) - the normative MVP specification
- [Requirements Discussion](docs/en/requirements-discussion.md) - the journal of decisions
- [Original Terms of Reference](docs/en/mcp-flow.md) - the historical source document
- [Contributing](CONTRIBUTING.md) - build, test and pull request expectations
- [License](LICENSE) - MIT

---

## Status

Implemented and covered by the test suites: the daemon (REST, project-scoped and daemon-level MCP, SSE),
file-first storage with rebuildable SQLite projections, the typed card graph with effective priorities, the
execution loop (attempts, handoffs, pause/resume, commit policies), durable memory, the unified agent
installer, and the PWA - board, project page, card page (scope, acceptance criteria, live diff, discussion,
runs), workflow sets, agent bridges and analytics.

Post-MVP, as tracked in §29 of the [specification](docs/en/technical-specification.md#29-implementation-status):
git push and branch handling, `WorkspaceMode.Worktree`, filesystem watching and reconciliation, agent
autolaunch, the interactive installer, and the memory and journal screens in the UI.
