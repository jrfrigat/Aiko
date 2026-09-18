# Aiko: Technical Specification and Architecture

> [Русская версия ->](../ru/technical-specification.md) - [Requirements journal](requirements-discussion.md) - [README](../../README.md)

---

Version: 0.1
Status: approved MVP foundation
Date: 2026-09-14

## 1. Purpose

Aiko (AI kanban orchestrator) is a local development orchestrator and a guided Kanban
pipeline for collaboration between a user and several AI agents. The system ties requirements,
tasks, documents, performers, history and actual file changes into a single traceable process.

Aiko does not replace Claude Code, Codex, Cursor or ZCode. It provides them with shared context,
workflow, memory and a stable MCP contract.

Projects use the `.aiko` directory, and builds and MCP tools carry the Aiko name. A previously
saved port remains valid until explicitly changed in the installer; for a fresh installation the
preferred port is `24560`.

## 2. Goals

- One global daemon serves several local projects.
- StoryCards and TaskCards form a typed graph.
- Every card has a folder with Markdown artifacts and structured metadata.
- The user creates and changes workflows without recompiling the application.
- A workflow stage can run in a selected AI agent.
- Different stages of one card can be executed by different agents.
- An unfinished stage can be handed to another agent without losing context.
- Several agents can work in parallel with explicitly shown risks of the shared checkout.
- The PWA provides the Kanban board, history, settings and artifact viewing.
- The system stays local, portable and requires no external database.
- A new agent is integrated by an adapter, not by changing the domain core.

## 3. Out of MVP scope

- A cloud Aiko server.
- Multi-user collaboration over the network.
- Automatic Git branch merging and conflict resolution.
- A mandatory vector database and embeddings.
- Cross-project card relations.
- A single cross-project Kanban board.
- A hard sandbox over `ScopeFiles`.
- Guaranteed forcing of any model to call an MCP tool.
- A full worktree isolation implementation, if it delays the first working release.

## 4. Terms

- **Project** - a registered local project root.
- **Card** - the common entity of a StoryCard or TaskCard.
- **StoryCard** - a large need or user story.
- **TaskCard** - atomic or composite technical work.
- **Workflow** - a configurable set of statuses and transitions for a card kind.
- **Stage** - a workflow status/stage with its associated instruction.
- **StageExecution** - one resumable execution of a card stage.
- **AgentAttempt** - an attempt of a specific agent to complete a StageExecution.
- **Artifact** - a document or other file expected or produced at a stage.
- **DeclaredScope** - the initially declared list of files/globs.
- **ActualFiles** - the actually changed files.
- **Projection** - a saved view over the single card set.
- **Durable memory** - verified project knowledge useful across sessions.

## 5. System context

```text
+----------------------------------------------------------+
|                    Aiko daemon                     |
|  REST + SSE  |  MCP Streamable HTTP  |  stdio proxy API |
|  scheduler   |  workflow engine      |  memory service  |
|  SQLite      |  project filesystem   |  agent adapters  |
+------+--------------+---------------+--------------+------+
       |              |               |              |
 Local PWA       Claude Code        Codex       Cursor/ZCode
```

The daemon starts once per user and listens on loopback only by default.

## 6. Solution components

### 6.1 Domain

Contains cards, relations, workflows, priority computation, executions, attempts, scope and
policies. Depends on no transport, file system, SQLite or specific agent.

### 6.2 Application

Contains use cases and ports:

- project registration and discovery;
- card and relation CRUD;
- stage transitions;
- start, pause, handoff and completion;
- projection computation;
- memory management;
- adapter installation planning.

### 6.3 Infrastructure

Implements:

- the global SQLite database;
- reading and atomic writing of `.aiko`;
- FileSystemWatcher and reconciliation;
- agent process launching;
- Git inspection;
- the official adapter set;
- log storage and rotation.

### 6.4 Server

A single ASP.NET Core .NET 10 process:

- REST API `/api/v1`;
- event SSE `/api/v1/events`;
- MCP endpoint `/mcp/projects/{projectHandle}` - the project's readable handle;
- health/readiness endpoints;
- serving the local PWA;
- the background run scheduler.

### 6.5 Web

A Blazor WebAssembly PWA on Flare.Blazor:

- project selection;
- Kanban projections;
- cards, graph and artifacts;
- executions and live logs;
- workflow, agent, security and Git settings;
- the initialization wizard.

The interface language follows the browser: the UI culture is resolved from the browser's language
preferences at startup, and the neutral resources are English, so a language Aiko does not translate
falls back to English rather than to another translation.

## 7. Storage

### 7.1 Separation of responsibilities

`.aiko` is the local, human-readable source of project data. The global SQLite database is a fast
index and the source of runtime state.

By default the installer adds `/.aiko/` to `.gitignore`. The user can choose another policy.

### 7.2 Project layout

```text
.aiko/
  project.json
  workflows/
    story.json
    task.json
  projections/
    tasks.json
    stories.json
    combined.json
  stories/<STORY-ID>/
    card.json
    request.md
    analysis.md
    architecture.md
    artifacts/
  tasks/<TASK-ID>/
    card.json
    request.md
    analysis.md
    architecture.md
    implementation.md
    artifacts/
    handoffs/
  memory/
    index.md
    architecture.md
    conventions.md
    lessons.md
  runtime/                 # always local, never committed
```

The presence of a specific Markdown file is defined by stage requirements, not by a fixed set: `aiko init`
creates `workflows/`, `projections/`, `stories/`, `tasks/`, `memory/` and `runtime/`, while a card's
directory (`tasks/<TASK-ID>/` with `card.json` and its artifacts) appears with the first card, and
`handoffs/` with the first hand-off of a stage to another agent.

### 7.3 Global SQLite

Recommended Windows path: `%LOCALAPPDATA%/Aiko/aiko.db`. On Linux/macOS the standard platform
user-data directory is used.

Main tables of the implemented schema (`AikoDatabase`):

- `schema_migrations` - applied schema versions;
- `projects` - the catalog of registered projects;
- `cards` - the card projection;
- `relations` - the relation projection;
- `executions` - stage runs, with their agent attempts, as one document;
- `events` - the event journal behind SSE (watermark and replay);
- `memory_fts` - the FTS5 memory index.

Workflows, Kanban projections and card content live in the `.aiko` files; SQLite holds only the index and
the runtime state. There are no separate tables for `Workflows`/`Stages`, `AgentInstallations`,
`CardLeases`, `Workspaces`, `Logs`, `BrowserSessions`, `Permissions`, `MemoryEntries`/`MemoryEdges` or
`CardSearch`: leases and agent installations are not stored, and full-text search goes through
`memory_fts`. The per-section status is in §29.

SQLite uses WAL, transactions, foreign keys (projections cascade when a project is removed) and indexes
over Project, Card and timestamps. FTS is used for memory full-text search. The relation graph is walked
in memory over the `relations` projection, not with a recursive CTE.

### 7.4 Synchronization

- The daemon is the only regular writer of structured data.
- `card.json` carries a `revision`.
- Writes go through a temporary file and atomic replace.
- The SQLite projection is rebuilt from `.aiko` by `aiko reindex` (or
  `POST /api/v1/projects/{projectId}/reindex`).
- Reacting to external JSON edits immediately (FileSystemWatcher as a signal plus optimistic
  reconciliation) is not implemented: the projection is refreshed by `reindex`. That is post-MVP
  (see §29).

## 8. Card model

Common Card fields:

- `id`, unique within the project;
- `projectId`;
- `kind`: `story` or `task`;
- `title`, `summary`;
- `workflowId`, `stageId`;
- `revision`;
- `tags`;
- `scores` and the computed priority snapshot;
- `declaredScopeFiles`;
- `actualChangedFiles`;
- `artifacts`;
- timestamps;
- the current performer and per-stage performer settings;
- custom metadata extensions.

StoryCards and TaskCards have their own configurable workflows. This replaces the original
constraint where a Story was static.

## 9. Relation graph

A relation is stored once as a directed edge. The reverse view is computed.

Built-in types:

- `implements`: a Task implements a Story;
- `parent-child`: card decomposition;
- `blocks`: one card blocks another;
- `relates-to`: a symmetric semantic link.

Built-in types may affect the scheduler and priority. Custom types are allowed as an extensible
catalog but by default carry only visual/search semantics.

In the MVP both endpoints of a relation belong to one project. `blocks` cycles are forbidden.
`relates-to` cycles are allowed. Reverse links are not duplicated in storage.

## 10. Priority

Every card has its own scoring criteria. A criterion defines an id, title, range, weight and an
`aiInstruction`. Size has a configurable factor.

The base card score:

```text
OwnScore = Sum(Wcriterion x NormalizedCriterionValue) x SizeFactor
```

For a Task with parents:

```text
EffectivePriority =
  (Wtask x OwnScore + Wparent x Max(ParentEffectivePriority))
  / (Wtask + Wparent)
```

Defaults: `Wtask=0.7`, `Wparent=0.3`. Without parents `OwnScore` is used. `relates-to` does not
affect priority. The formula and factors are versioned; the saved snapshot contains the formula
version.

## 11. Workflows and internal skills

A Stage contains:

- a stable ASCII id and a display title;
- order;
- allowed card kinds;
- the execution instruction;
- the default agent;
- allowed agents;
- expected output artifacts;
- the reaction to each missing artifact;
- action policies;
- verification commands;
- auto-advance rules.

The user can create a Stage and write an instruction. Aiko turns it into an internal skill.
The MCP tool list stays stable; the dynamic instruction is returned through the stage context.

Base skills are built from `SkillsExample` but drop the binding to a specific database, project
and the old `.claude/workflow` structure.

A missing artifact is handled by the `allow`, `warn`, `retry`, `needs-attention` or
`block-auto-advance` policy. The user can always move or cancel a card manually; the override is
recorded in history.

## 12. Execution and handoff

A `StageExecution` belongs to a card and a stage. The workspace belongs to the StageExecution,
not to the agent. An `AgentAttempt` describes an individual performer.

Attempt states:

- `queued`, `running`, `waiting-for-user`, `rate-limited`, `paused`, `failed`, `cancelled`,
  `superseded`, `completed`.

Handing off to another agent creates a new Attempt inside the same StageExecution. A handoff
includes:

- the card and the stage skill;
- decisions and the progress summary;
- completed and remaining steps;
- declared/actual/out-of-scope files;
- artifacts;
- workspace and diff;
- the last useful error/output;
- a reference to the previous Attempt.

In the MVP the fallback is manual. Later: `manual`, `ask`, `automatic` and an ordered agent list.
Only confidently recognized `rate-limited` permits an automatic fallback.

## 13. Concurrency and workspaces

Strategies:

- `shared`: the user's checkout;
- `worktree`: a dedicated Git worktree per execution;
- a future strategy for projects without Git.

Safe MVP defaults:

```json
{
  "workspaceMode": "shared",
  "maxConcurrentRuns": 1,
  "scopeOverlapPolicy": "ask",
  "sharedCheckoutCommitPolicy": "deny"
}
```

The user can enable parallel shared mode. The UI keeps warning that the working tree and the Git
index are shared, changes may conflict, mix into a commit and be misattributed.

The scheduler checks leases, `blocks`, the run limit and scope overlap. External IDE changes are
not blocked. When detected, the result is flagged as requiring attribution review.

Worktree mode is added through an `IWorkspaceStrategy` without changing the domain. In it, the
new agent after a handoff connects to the existing worktree of the StageExecution.

## 14. ScopeFiles

`ScopeFiles` is an instruction. There is no hard ban.

The card stores:

- `declaredScopeFiles`;
- `actualChangedFiles`;
- the computed `outOfScopeFiles`.

Before intentionally changing something outside the scope, the agent must call the scope-expansion
request and warn the user. Policies: `ask`, `warn`, `allow`. In non-interactive `ask`, the
execution moves to `needs-attention`.

## 15. Agents and adapters

Built-in MVP adapters:

- Claude Code;
- Codex;
- Cursor;
- ZCode by Z.ai.

Next candidates: OpenCode, Windsurf, Gemini CLI, Zed and the VS Code agent.

`IAgentAdapter` is responsible for discovery, capabilities, the install plan, applying/removing
the integration, health checks, launching, pause/cancel/resume, exit classification and handoff.

Capabilities include MCP transports, config scopes, skills, commands, rules, hooks, headless
mode, resume, structured output, rate-limit detection and remote workspace.

Official adapters compile into the Native AOT application. External extensions use a declarative
manifest and templates, or a separate executable with a versioned JSON-RPC/stdio contract.
Third-party .NET DLLs are not loaded dynamically.

### 15.1 ZCode

The primary path is a local ZCode plugin with `.zcode-plugin/plugin.json`, `.mcp.json`, `skills/`,
`commands/` and hooks where needed. The fallback is a safe merge of `.zcode/config.json` and
installing `.zcode/skills`. The adapter accounts for the native config taking precedence over
`.agents/mcp.json`.

## 16. MCP

The primary transport is the Streamable HTTP of the global daemon. The endpoint is per project:

```text
POST http://127.0.0.1:<configuredPort>/mcp/projects/{projectHandle}
```

`{projectHandle}` is the project's readable slug, the same handle the UI's URLs carry; the daemon also
resolves the project's id, so a URL written before slugs existed keeps working.

`stdio` remains a compatible thin proxy and does not create a second store. The proxy runs as a
separate short-lived process:

```text
aiko-stdio --url http://127.0.0.1:<configuredPort>/mcp/projects/{projectHandle}
```

Passing the URL through `AIKO_MCP_URL` is allowed. The proxy works at the raw transport level,
exits when stdin closes, reserves stdout for MCP only and accepts exclusively a loopback HTTP(S)
project endpoint. The system HTTP proxy and redirects for the daemon connection are forcibly
disabled.

The initial stable tool set:

- `aiko_get_project_context`;
- `aiko_list_cards`;
- `aiko_get_card`;
- `aiko_create_card`;
- `aiko_update_card`;
- `aiko_link_cards`;
- `aiko_add_comment`;
- `aiko_list_comments`;
- `aiko_take_card`;
- `aiko_start_stage`;
- `aiko_report_progress`;
- `aiko_request_scope_expansion`;
- `aiko_complete_stage`;
- `aiko_pause_execution`;
- `aiko_handoff_execution`;
- `aiko_resume_execution`;
- `aiko_report_agent_state`;
- `aiko_search_memory`;
- `aiko_store_memory`;
- `aiko_open_ui`.

Tool descriptions and project rules require fetching the context first. Work belongs to a stage execution:
an agent starts the stage it works in (`aiko_start_stage`, which moves the card into that stage) before it
changes any file, produces the artifacts the stage requires and completes it. `aiko_move_card` advances a
card one stage at a time and refuses to leave a stage that has no execution behind it, so a card cannot be
declared finished by moving it. The board's own move endpoint is deliberately not held to that rule - the
board is how a person corrects their own board - and `aiko doctor` reports the cards pushed past a stage
anyway, as its `card-progress` finding. MCP does not guarantee that every model will call a tool
automatically; the adapter reinforces the behavior with skills,
rules and hooks.

## 17. REST, SSE and the local UI

The MVP UI is local and available at a permanent loopback URL. The home page contains a switcher
for all registered projects. The UI can be opened manually, from the CLI, a deep link, an MCP tool
or a skill.

```text
aiko ui [--project <id>] [--card <id>]
```

`aiko-ui` is the canonical skill; `stitch-ui` is a possible alias.

SSE streams execution events and logs. After a reconnect the client sends the last event id. The
full log is read page by page over REST so the browser does not hold it in memory.

## 18. Kanban projections

A projection is a saved view configuration.

Built-in views:

- `Tasks`: TaskCards by workflow;
- `Stories`: StoryCards by their own workflow with child-task aggregates;
- `Combined`: Stories as swimlanes/groups with Tasks inside; a separate group for Tasks without
  a Story.

Cards are not copied. Later the user creates custom filters by agent, status, tag, priority,
blockers, scope deviations and project. In the MVP the project switcher always exists; the
cross-project board is deferred.

## 19. Memory

A single `MemoryService`:

- durable Markdown in `.aiko/memory`;
- SQLite FTS and a graph index;
- provenance: source, card, execution, agent and timestamp;
- a vector backend is an optional module of a future version.

`CLAUDE.md`, `AGENTS.md`, ZCode/Cursor rules direct the agent to the Aiko MCP and memory but do
not duplicate it. When closing a card, the agent proposes durable conclusions; saving is governed
by policy and recorded in history.

## 20. Git

By default `.aiko` is fully ignored. The wizard offers:

- `local-only`;
- `track-project-knowledge`;
- `custom`.

Full logs, secrets, sessions, locks and SQLite are never offered for committing.

Operations have `allow`, `ask`, `deny` with a global -> project -> stage -> execution hierarchy. In
parallel shared mode Git mutations get a separate warning; the safe default is `deny`.

## 21. Security

- Bind to `127.0.0.1`/`::1` only by default.
- `Origin` and `Host` validation, DNS rebinding protection.
- Authentication for MCP HTTP and browser sessions.
- Secrets live outside the project in a protected user-data store.
- Permissions are split into read, project-write, process-execute and external-publish.
- Arbitrary commands get no implicit allow.
- Logs redact known secrets and have a bounded retention.
- Adapters and plugins show the install plan and trust boundaries.
- Remote access is off in the MVP.

## 22. Installation

The global installer:

- installs the executable;
- configures daemon autostart;
- offers a custom port or first uses the preferred `24560`; if it is busy, it automatically picks
  a free random port from `18000-18999` and saves the choice in the global `settings.json`;
- creates the SQLite database and local configuration;
- diagnoses the available agents.

`aiko init` in a project:

- registers the absolute root and issues a `projectId`;
- creates `.aiko`;
- offers a Git policy;
- shows the unified list of discovered and supported agents with independent checkboxes;
- installs all selected agent adapters in one pass;
- creates the project-scoped MCP endpoint/token;
- installs skills/rules/commands/hooks according to capabilities;
- offers workflows, default agents and action policies;
- shows the `InstallationPlan`, supports dry-run;
- runs a read-only health/context smoke test.

Configuration changes are idempotent. Removal touches only Aiko-marked entries.
The choice is not one-shot: the wizard can be run again to add or remove an individual
integration without reinstalling the others. The final combined `InstallationPlan` groups changes
and warnings by adapter; one adapter's failure does not hide the results of the others.

## 23. Reliability and audit

- Every mutation has an operation id and idempotency.
- Significant actions are recorded in an append-only SQLite event log.
- A card transition and a process launch are separate transactional steps with recovery.
- After a crash, running attempts become `needs-attention` unless the adapter proves the process
  is alive.
- Log rotation is configurable.
- Repair validates JSON schemas, references, `blocks` cycles, lost artifacts and the SQLite
  projection.
- File formats and APIs have versions and migrations.

## 24. Configuration

Levels: global -> project -> workflow/stage -> card/execution. The more specific value wins.
Every effective-config endpoint shows the value and its source.

Main groups:

- server and storage;
- UI;
- criteria, formula and size factors;
- workflows and artifacts;
- agents, assignments and fallback;
- concurrency and workspace;
- scope policies;
- action/Git policies;
- memory and retention;
- adapters.

## 25. MVP

### Stage 1. Foundation

- solution and domain model;
- versioned configuration contracts;
- the priority calculator;
- relation invariants;
- application ports;
- the global server health endpoint.

### Stage 2. Storage and projects

- global SQLite;
- `.aiko` layout and schemas;
- register/init/reindex;
- cards, relations and memory CRUD.

### Stage 3. MCP and agents

- Streamable HTTP;
- the stdio proxy;
- Claude Code, Codex, Cursor, ZCode installers;
- take/start/report/complete/handoff.

### Stage 4. Local PWA

- the Flare.Blazor shell;
- project switcher;
- Tasks/Stories/Combined;
- card editor, graph, artifacts, executions and SSE logs;
- settings and warnings.

### Stage 5. Execution loop

- process adapters;
- manual/active mode;
- policies, missing artifacts, scope warnings;
- limit detection and handoff.

### Stage 6. Hardening

- recovery/migrations;
- a security review;
- AOT publish;
- performance and concurrency tests;
- an optional worktree strategy.

## 26. MVP acceptance criteria

1. The daemon registers at least two projects and the UI switches between them.
2. Stories and Tasks are created as folders with `card.json` and Markdown.
3. Relations, blocking and effective priority are computed correctly.
4. The user changes a workflow and creates a new internal skill/status.
5. Claude Code, Codex, Cursor and ZCode pass the MCP health/context smoke test.
6. A card can be assigned to an agent, started, stopped and handed to another one.
7. History distinguishes StageExecutions and AgentAttempts.
8. Declared, actual and out-of-scope files are visible separately.
9. Parallel shared mode shows a permanent warning.
10. Tasks, Stories and Combined use the same data.
11. The full runtime log stays local; durable memory is available to the next agent.
12. After the SQLite index is deleted, it is rebuilt from `.aiko`.
13. The server listens on loopback and rejects invalid Origins.
14. Release publish passes Native AOT without unhandled AOT/trimming warnings or documents the
    temporarily accepted exceptions.

## 27. Accepted assumptions

- The canonical project directory name is `.aiko`.
- Stories and Tasks have their own workflows.
- Streamable HTTP is the primary MCP transport; stdio is a compatibility proxy.
- The local PWA is the MVP UI.
- Cross-project relations and a shared board are deferred.
- The shared checkout is the first workspace implementation; parallelism is enabled consciously.
- Custom relation types are allowed but carry no automatic semantics.
- The exact Native AOT figures are guidelines.

## 28. Requirements history

The discussion, alternatives and answers are kept in
[requirements-discussion.md](requirements-discussion.md). On any discrepancy this document is the
current normative specification, and the journal explains where the decision came from.

## 29. Implementation status

Sections 1-28 above stay normative: they are requirements, not a report. The table below says which of
them are verifiable in the code of the current version and which are still a plan, so the document is not
read as a description of a finished product where it is not one.

| § | Section | Status |
| :-- | :-- | :-- |
| 1-6 | Purpose, goals, scope, terms, context, components | Implemented |
| 7 | Storage | Implemented: `.aiko` files plus the SQLite projections (cards, relations, memory, stage transitions, daemon runs) and `reindex`. FileSystemWatcher and reconciliation are post-MVP |
| 8 | Card model | Implemented |
| 9 | Relation graph | Implemented inside a project; cross-project relations are out of scope (see §3) |
| 10 | Priority | Implemented, including recursion through parent stories |
| 11 | Workflows and internal skills | Workflow and its editor are implemented; the base `aiko-*` skills are written by the adapters; the wider skill catalogue is in progress |
| 12 | Execution and handoff | Implemented; workspace/diff and logs inside the handoff document are partial |
| 13 | Concurrency and workspaces | Implemented: run limit plus per-card and per-project locks. `WorkspaceMode.Worktree` is declared but not executed - only `Shared` runs |
| 14 | ScopeFiles | Implemented, including the out-of-scope report |
| 15 | Agents and adapters | Implemented: Claude Code, Codex, Cursor, ZCode |
| 16 | MCP | Implemented: project-scoped and daemon-level endpoints, REST parity on stage validation |
| 17 | REST, SSE and the local UI | Implemented; SSE with watermark, replay and project checking |
| 18 | Kanban projections | Implemented (Tasks / Stories / Combined) |
| 19 | Memory | Implemented on FTS5 |
| 20 | Git | Implemented: reading the repository (branch, changes, log, the diff of a card's files) by running the `git` client, plus the commit policies; push and branch handling are post-MVP |
| 21 | Security | Implemented: loopback binding, access token, browser pairing, `Host`/`Origin` checks |
| 22 | Installation | Implemented: `install.ps1`, user-scope skills and MCP configuration, binary update and removal. The interactive TUI installer and autostart are post-MVP |
| 23 | Reliability and audit | Partial: event journal, replay and health exist; log rotation does not |
| 24 | Configuration | Implemented: templates (workflow sets) as the source of defaults - copied at init, captured from a project, imported/exported and applied explicitly; project overrides and the effective source in the response |
| 25-27 | MVP, acceptance criteria, assumptions | Met, except for what is marked post-MVP above |
| 28 | Requirements history | - |

The comparison was made against the code and the tests of the current version: `dotnet test Aiko.slnx`
(the number of checks is in the README).

