# Aiko: Requirements Clarification Journal

> [Русская версия ->](../ru/requirements-discussion.md) - [Technical specification](technical-specification.md) - [README](../../README.md)

---

The historical answers below were recorded while the working name of the product was StitchFlow.
The current name and the up-to-date contracts are fixed in
[technical-specification.md](technical-specification.md).

This document stores questions, answers, adopted decisions and open architectural points. It must
be updated after every requirements discussion so that the history of decisions is not lost.

## 2026-09-14: initial analysis of the terms of reference

### Recorded requirements and answers

1. **Backend scope.** One permanently running backend serves all projects.

2. **Cards and documents.** Every Story and Task is represented by a separate folder. The folder
   contains the card description and several Markdown documents: the original request, analysis,
   architecture and other artifacts, with the exact set depending on the pipeline stage.

3. **Metadata and performance.** Markdown is for meaningful documents. Relations, scores, state
   and other structured data must be stored separately. The system must work efficiently with a
   large number of cards and support graph queries.

4. **Card graph.** Stacking/linking cards does not destroy or merge them. Typed relations are
   created between cards: relates to, parent/child, blocks/blocked-by and others. For shared work,
   child TaskCards can be created that cover the needs of several parent cards.

5. **Priority.** Both StoryCards and TaskCards have their own priority. A TaskCard's priority also
   accounts for the priority of its parent StoryCards. When several linked/parent cards exist, the
   maximum parent value is used. The exact formula still had to be defined.

6. **Manual control.** The user may move a card at any moment, including cancelling the
   implementation. Automatic constraints must not take this right away.

7. **Stage artifacts.** Columns have no shared blocking validation. A column defines expected
   output artifacts: documents that should appear after the corresponding stage completes.

8. **Dynamic pipeline.** MCP has stable entry points. The user can create a new status, write an
   instruction and link it to the status. When a card enters the column, an internal dynamic skill
   built from that instruction starts. Example: the closing stage commits, updates the changelog
   and creates a PR.

9. **Custom skills.** Skills must remain available for direct launch inside AI agents. Preferably
   they are published/installed into the agents' native directories (`.claude`, `.codex` and
   similar). Skills from `SkillsExample` should become the base but must be decoupled from
   specific projects and the current `.claude/workflow` structure.

10. **Supported agents.** Claude Code, Codex and Cursor must work simultaneously with one project.
    Later, OpenCode and other popular agents are desirable.

11. **Native AOT.** The startup time and memory figures given in the terms of reference are
    guidelines, not hard acceptance criteria.

### Observations from the Flare backlog example

- `blockedBy` is a directed link; the reverse "blocks" link is computed.
- `groups/group` combine cards that are rational to complete in one shared effort but do not turn
  them into a single card.
- One issue folder or issue document can describe several atomic backlog items.
- For StitchFlow it makes sense to generalize these ad-hoc fields into a typed card graph.

### Preliminary architectural recommendations

#### Card storage

Use a hybrid model:

- `.flow/stories/<STORY-ID>/` and `.flow/tasks/<TASK-ID>/` contain Markdown artifacts and a small
  `card.json` with portable, Git-friendly card metadata;
- SQLite serves the fast index, full-text search, graph queries, locks, queues and runtime state;
- portable data stays readable and diffable in Git, and the SQLite projection can be rebuilt from
  `.flow`;
- all `card.json` changes are performed by the backend atomically and with a revision number so
  that concurrent updates are detected.

Keeping a single canonical SQLite database inside every project is undesirable: a binary file is
hard to inspect and merge in Git. Keeping the whole backlog as one large JSON file is also
undesirable because of write conflicts and full file rewrites. A separate `card.json` next to each
card gives local changes and preserves portability.

#### Processes and MCP

The preferred scheme for a single global backend:

- `stitchflow serve` runs the single daemon, API, scheduler and store;
- every MCP client runs a lightweight `stitchflow mcp` over stdio;
- the stdio process owns no state and does not open the main port; it proxies calls to the daemon
  over local IPC or a secured loopback API;
- the PWA uses the REST API and SSE for state and streaming logs;
- MCP Streamable HTTP can be added later as a separate transport if clients need it.

#### Memory

Use one memory service with two representations:

- canonical, human-curated durable records in `.flow/memory/*.md`;
- SQLite FTS and a graph index for fast search and links;
- add a vector/semantic index later as a swappable module, not as a second independent source of
  truth;
- `CLAUDE.md`, `AGENTS.md` and Cursor/OpenCode rules must point the agent to the StitchFlow MCP
  and the `.flow` memory without copying the memory itself across several directories.

The approach is based on the useful part of the Ruflo architecture (SQLite, index and graph) but
avoids the problem Ruflo itself noted of several overlapping memory systems.

#### Local API security

Recommended minimum:

- listen on loopback only unless the user explicitly enables remote access;
- no wildcard CORS; allow only configured origins;
- validate `Origin` and `Host`, protect against DNS rebinding;
- pair the PWA with the daemon initially using a one-time code and issue a limited session;
- separate permissions for reading, file changes and process launches;
- run dangerous commands and publication to external systems through a separate
  confirmation/policy;
- keep no permanent administrative secret in the project or the published PWA.

### Needs further resolution

1. Where does the global SQLite database physically live: in the user profile, or one index
   database per `.flow`?
2. Should the public GitHub Pages PWA be the main working UI, or can the daemon serve the same PWA
   locally for simpler and safer communication?
3. Is MCP Streamable HTTP needed in the first version, or are stdio bridges for agents plus
   REST/SSE for the PWA enough?
4. Which graph relation types are in the first version, and which of them have a computed reverse
   link?
5. How exactly is the final TaskCard priority computed from its own score and the parents'
   priorities?
6. Are output artifacts only an expected checklist, or should a missing artifact mark the stage
   incomplete and require an explicit user override?
7. How to coordinate several agents working simultaneously: shared checkout or separate Git
   worktrees; can two agents be assigned to one card; what to do on file overlap?
8. How strictly is `ScopeFiles` controlled: instruction and audit only, or a technical ban?
9. Which events and logs go into Git, and which are local runtime state?
10. Which dynamic-skill actions may run automatically, and which always require user confirmation?

## 2026-09-14: MCP transport, scope, data directory and installation

### New answers and decisions

1. **Automatic MCP usage.** After StitchFlow is connected to a project, agents must automatically
   account for its tools and context. Project initialization must install native configuration
   and instructions for every supported agent.

2. **ScopeFiles.** It is an instruction, not a technical sandbox. Before changing a file outside
   the original scope the agent must warn the user. The card stores separately:
   - the initially declared scope;
   - the actually changed files;
   - the computed deviation of the actual set from the declared one, visible in the UI.
   Automatic blocking or rollback is not required.

3. **Project directory.** The working name `.flow` should be replaced. The preferred option is
   `.stitchflow`: it is unambiguously tied to the product and has a smaller conflict risk than
   `.stitch`.

4. **Git.** By default the whole `.stitchflow/` directory is added to `.gitignore`. In the
   initialization wizard the user can choose a different storage policy. Even with full
   ignoring, Markdown/JSON remain the local readable source of truth, and SQLite serves as the
   index and runtime store.

5. **Agent assignment.** On the Kanban board the user picks the agent to execute a stage. An agent
   can also take a card with a command from its chat; the transition and the performer are
   recorded in history. Every status has a default agent, and a specific card can override the
   assignment. Before an automatic launch the UI shows the choice with a pre-filled value.

6. **Sequential execution.** Only one agent executes a card at a time. Different agents can be
   assigned in advance to different statuses of one card; they execute the stages sequentially.
   The stage history stores the agent type, the instance/client and the execution time.

7. **Priority.** The Task's own score and the maximum priority of the parent Stories are combined
   with configurable factors. All factors are available to the user in settings.

8. **Artifacts.** The reaction to a missing artifact is configurable. A shared default plus
   per-requirement overrides is possible.

9. **Action policy.** Potentially dangerous operations use a configurable policy. Proposed
   values: `allow`, `ask`, `deny` with per-action/status/project overrides.

10. **History.** Full logs and technical events are stored locally in SQLite. `.stitchflow` keeps
    significant events and final artifacts; by default the whole directory is ignored by Git.

11. **Initialization.** An interactive installation/initialization wizard is needed. It registers
    the project in the global daemon, creates `.stitchflow`, connects the MCP and instructions of
    supported agents, and offers a Git policy, agent assignments and action policies.

### Decision on MCP transports (recommendation)

Support both current transports through one set of handlers:

- **Streamable HTTP** - the primary transport of the global daemon: one process and one state for
  several simultaneously connected clients;
- **stdio** - a compatible adapter for clients or client versions without reliable Streamable HTTP
  support.

Every HTTP connection must be bound to a project. Proposed initialization flow:

1. `stitchflow init` runs in the project root and registers the absolute path with the daemon.
2. The daemon issues a stable `projectId` and separate connection credentials.
3. The initializer writes into the agent configuration a project MCP endpoint of the form
   `http://127.0.0.1:<port>/mcp/projects/<projectId>` or an equivalent token-based binding.
4. Every MCP call gets an unambiguous project context without relying on the HTTP client's `cwd`.

This fits a global daemon better than a separate heavy stdio process per client. The transport
implementation must remain a swappable infrastructure layer; the domain MCP handlers must not
depend on HTTP or stdio.

### Agents using StitchFlow automatically

A connected MCP alone does not guarantee that a model will call a tool. The initializer must
create an adaptation for every agent:

- the project MCP configuration;
- a native rule (`CLAUDE.md`, `AGENTS.md`, Cursor rules and the like) instructing to fetch the
  StitchFlow context first and use it for card operations;
- base skills in the agent's native directory;
- a hook/session-start integration where the specific agent supports it;
- a diagnostic check: MCP connected, project recognized, a read-only health/context tool called
  successfully.

This gives reliable behavior within each client's capabilities, but there is no universal
protocol-level mechanism that forces any model to call an MCP tool.

### Refined PWA recommendation

The GitHub Pages PWA can remain the primary UI; serving the PWA locally is not mandatory. A safe
scheme requires:

- a loopback-only API;
- an exact allowlist of the public origin;
- `Origin` and `Host` validation;
- an explicit browser Local Network Access permission;
- one-time pairing with the local daemon;
- a short-lived project-scoped session;
- `allow`/`ask`/`deny` policies for side-effecting operations;
- the ability to revoke all browser sessions from the local CLI.

The advantage: the PWA updates independently of the installed daemon version. For that the API
must be versioned and the UI/backend compatibility negotiated. An embedded local PWA should stay
as an optional fallback for closed networks or browsers that block loopback access.

### Open questions after round two

1. Confirm the primary transport: Streamable HTTP with a stdio fallback adapter.
2. Confirm the `.stitchflow` directory name.
3. Choose a Git checkout strategy for automatic agents: shared checkout or a separate worktree
   per run.
4. Define the parent Story factor formula in the Task priority.
5. Decide whether custom relation types are allowed immediately or only a predefined extensible
   catalog.
6. Define the out-of-scope warning behavior in fully automatic mode when the user is not in an
   interactive agent chat.
7. Decide whether the `stitchflow init` wizard should modify existing `CLAUDE.md`/`AGENTS.md` or
   create separate pluggable rules files where the client supports them.

## 2026-09-14: agent parallelism and launching the UI

### New requirements

1. **Parallel work.** In the target architecture different agents can simultaneously execute
   different cards of one project if the cards do not block each other. For the first version,
   serializing managed runs within a project is acceptable if safe change attribution and conflict
   resolution significantly complicate the MVP.

2. **External changes.** The user can always edit files directly in the IDE. StitchFlow must not
   try to forbid that; it must detect the working-copy state change and account for the fact that
   it may not belong to the active agent run.

3. **Launching the UI from an agent.** A base skill `stitchflow-ui` (a possible short alias
   `stitch-ui`) and a corresponding stable command/MCP tool are needed to open the current
   project's board or a specific card.

4. **UI choice.** Global or project settings define the UI mode:
   - `remote` - the GitHub Pages PWA;
   - `local` - the UI served by the local daemon;
   - `auto` - prefer remote and use local as a fallback.

### Concurrency recommendation

Design the data model and services for several runs from the start (`Run`, `CardLease`,
`DeclaredScope`, `Workspace`), but enable the capabilities in stages:

#### First version

- one managed automatic run per project in the shared checkout;
- one performer per card;
- user/IDE changes are not blocked;
- a working-copy baseline is saved before a run;
- an external change during a run flags the result as requiring attribution review;
- the agent reports actually changed files via MCP, and StitchFlow best-effort compares the report
  with the diff against the baseline.

#### Next version

- a separate Git worktree per managed run;
- parallel card execution when there are no graph blocks;
- an upfront `DeclaredScope` overlap check;
- a separate step integrating the result into the user's checkout;
- a conflict is not resolved automatically without a configured policy or confirmation.

The reason for staging: in a shared checkout Git shows the merged state of all processes and the
user. Reliably attributing a change to a specific card is impossible. A worktree gives exact
attribution and isolation but requires a separate change-integration process.

### A safe `stitchflow-ui`

The skill is an adapter over the stable `stitchflow_open_ui` operation and holds no permanent
secrets. Proposed flow for the remote UI:

1. The agent calls the MCP tool `stitchflow_open_ui` with the current `projectId` and an optional
   `cardId`.
2. The daemon creates a random one-time pairing nonce bound to the project, assignment and a
   short lifetime.
3. The GitHub Pages address opens with the nonce only in the URL fragment, for example:
   `https://<site>/#/connect?pair=<one-time-nonce>&project=<id>`.
4. The fragment is not sent to the GitHub Pages HTTP server. The PWA reads the nonce and exchanges
   it with the loopback API for a short-lived project session.
5. The nonce becomes invalid immediately after the first successful exchange or on timeout.
6. The PWA strips the pairing parameters from the address bar via the History API.

Passing a permanent bearer token in a query string or fragment is forbidden. If automatic browser
opening is unavailable/forbidden, the tool returns a safe link or a code for manual connection.

For the local UI no pairing is needed: the daemon issues a local session but still validates
loopback and protects side-effecting operations with the user's policies.

### Preliminary entry-point names

- CLI: `stitchflow ui [--project <id>] [--card <id>] [--mode auto|remote|local]`;
- MCP: `stitchflow_open_ui`;
- skill: `stitchflow-ui`;
- an optional short alias: `stitch-ui`.

The canonical name recommended is `stitchflow-ui` so that it is found unambiguously among skills
and does not clash with other products.

### Open questions after round three

1. Confirm the MVP restriction: one managed automatic run per project, with the model prepared for
   the later move to worktrees and parallelism.
2. Pick the default UI mode: `remote`, `local` or `auto`. The recommendation is `auto`, where
   remote is preferred and local is the fallback.
3. Decide whether `stitchflow-ui` should open the browser immediately or return a link by default
   and open the browser only after an explicit user action/setting.

## 2026-09-14: concurrency modes and Kanban projections

### Confirmed decisions

1. **MVP workspace.** If a full worktree mode significantly lengthens the first version, the MVP
   may start with a shared checkout. The architecture must not forbid adding worktrees later.

2. **UI in the MVP.** The first version uses the local UI served by the global daemon. The remote
   GitHub Pages UI remains a possible future mode but is not needed for the MVP.

3. **UI navigation.** The UI always contains a registered-project switcher. Opening the UI does
   not depend on the skill: the user can open the permanent local address directly, use the CLI,
   an MCP tool, a link or the `stitchflow-ui` skill.

4. **Opening a card.** The user can open the board and pick a card independently. A skill or a
   command with a `cardId` is only a convenience deep-link mechanism.

5. **Board projections.** Separate views are required for StoryCards, TaskCards and a combined
   view. Projections do not copy cards and work on top of the single data model/graph.

### Concurrent execution modes

Parallelism should be prepared in the first version through a swappable workspace strategy:

- `shared` - all agents work in the user's current checkout;
- `worktree` - every managed run gets a dedicated Git worktree;
- a future strategy for a project without Git is possible.

Proposed project configuration:

```json
{
  "execution": {
    "workspaceMode": "shared",
    "maxConcurrentRuns": 1,
    "scopeOverlapPolicy": "ask",
    "sharedCheckoutCommitPolicy": "deny"
  }
}
```

Even before worktrees are implemented, the user can explicitly raise `maxConcurrentRuns` and
enable parallel work in the shared checkout. The UI must show a permanent warning:

- the Git working tree and index are shared by all processes and the user;
- exact file attribution becomes best effort;
- overlapping changes can be overwritten;
- a commit can include another card's changes.

For `shared + maxConcurrentRuns > 1`, automatic `git add`, `git commit`, `git stash`, branch
switching and other operations on the shared Git state must be denied by default regardless of the
regular stage policy. The user can lift the ban only with an explicitly dangerous setting. A safe
automatic commit is allowed in `worktree`, where the run has its own index and working tree.

The pre-launch check considers:

1. graph blocks of cards;
2. the card lease (one performer per card);
3. overlap of the declared `ScopeFiles` of active runs;
4. the selected `scopeOverlapPolicy`: `deny`, `ask`, `allow`;
5. the `maxConcurrentRuns` limit.

User changes from the IDE are never blocked and may appear outside this model.

### Board projections

Recommended built-in projections:

1. **Tasks** - TaskCards distributed over workflow columns; the main working board.
2. **Stories** - StoryCards with aggregated progress and child-task state, or with their own life
   cycle (needs clarification).
3. **Combined** - StoryCards form swimlanes/groups with TaskCards inside; a separate section holds
   Tasks without a Story. Flat mixing of card kinds is not recommended.

A projection is a saved view configuration, not a separate backlog:

```json
{
  "id": "combined-default",
  "title": "Stories and tasks",
  "cardTypes": ["story", "task"],
  "groupBy": "parentStory",
  "columnSource": "workflowStatus",
  "filters": [],
  "sort": ["priority:desc"]
}
```

Custom projections can be added later: filters by agent, status, tag, priority, overdue state,
blockers and project.

### Open questions after round four

1. Does the StoryCard stay static as in the original terms of reference with its state computed
   from child TaskCards, or should the StoryCard have its own workflow and manual column
   transitions?
2. Is a shared cross-project board needed in the MVP, or is a project switcher plus a per-project
   board enough?
3. Can relations exist between cards of different projects?
4. Confirm the safe default: `shared`, `maxConcurrentRuns=1`, with the user able to explicitly
   enable parallelism with a warning; automatic Git operations are forbidden in parallel shared
   mode.

## 2026-09-14: UX warning for parallel shared mode

### Confirmed decision

Several agents working in parallel in a shared checkout is allowed as a user setting. The risk
that one agent's commit or change captures another agent's or the user's changes is not a reason
to forbid the mode outright. Instead the UI explicitly warns the user about the consequences of
the choice.

The warning is shown:

- when enabling `maxConcurrentRuns > 1` with `workspaceMode=shared`;
- in settings next to the parallelism parameter;
- on the board/run panel while several runs execute simultaneously;
- before permitting a Git operation in that mode.

Recommended wording in meaning:

> Agents share the working copy and the Git index. Changes of different cards and your local
> changes can end up in one commit, conflict, or be misattributed to another run.

The warning must be visually prominent but, once the mode is consciously enabled, must not block
regular file operations. The automatic Git-operations policy (`allow`/`ask`/`deny`) stays a
separate setting; the safe default for parallel shared mode is `deny`.

## 2026-09-14: handing an unfinished stage to another agent

### Requirement

If an agent cannot continue executing a card (for example, Claude ran out of quota), the user must
be able to continue the same stage in another agent, for example Codex. Reassignment does not
create a new card and does not lose the first run's history.

### Execution model

The workspace and state belong to the stage execution, not to a specific agent:

```text
Card
  -> StageExecution
       -> AgentAttempt #1: Claude, RateLimited
       -> AgentAttempt #2: Codex, Running
       -> AgentAttempt #3: Cursor, Completed
```

- `StageExecution` stores the card, workflow status, workspace, scope, baseline, expected
  artifacts and the overall stage progress;
- `AgentAttempt` stores the specific agent, client, timing, exit reason, log and actually
  performed actions;
- the lease belongs to the `StageExecution`, and the current performer inside it can change;
- at any moment the execution has one active agent, unless the user explicitly breaks this rule
  with an external manual launch.

### Agent attempt states

The minimal set:

- `Queued`;
- `Running`;
- `WaitingForUser`;
- `RateLimited`;
- `Paused`;
- `Failed`;
- `Cancelled`;
- `Superseded`;
- `Completed`.

`RateLimited` is detected via the agent adapter's structured result, a known exit/output pattern,
or manually by the user. An unknown process exit stays `Failed`/`NeedsAttention` instead of being
misclassified as a rate limit.

### The handoff package

Before the next agent starts, StitchFlow assembles the continuation context:

- the card and the current stage;
- the stage instruction/skill;
- the original `ScopeFiles`;
- the already changed and out-of-scope files;
- the artifacts created;
- the decisions and a brief summary of the previous attempt;
- the unfinished steps;
- the last useful output fragment/error;
- workspace details and the current diff;
- a reference to the previous `AgentAttempt`.

The new agent gets the command to continue the existing work, to verify the actual file state
first and not to blindly repeat already completed actions.

### Workspace behavior

- In `shared` mode the new agent continues in the same checkout.
- In `worktree` mode the worktree belongs to the `StageExecution`, so the new agent connects to
  that same worktree instead of creating another one.
- When the old agent is unavailable, its attempt is closed as `RateLimited`, `Paused` or `Failed`,
  and the lease moves to the new performer.

### UI and entry points

On an active/stopped card these actions are available:

- `Continue with another agent`;
- `Retry with the same agent`;
- `Pause`;
- `Cancel the stage execution`.

When picking another agent, the UI shows the previous performer, the stop reason, unfinished
steps, changed files and the new performer. The card history keeps both attempts.

Proposed MCP tools:

- `stitchflow_report_agent_state`;
- `stitchflow_pause_execution`;
- `stitchflow_handoff_execution`;
- `stitchflow_resume_execution`.

### Automatic fallback policy

For the MVP the manual `Continue with another agent` action is enough. Later a status can have an
ordered fallback list, for example `Claude -> Codex -> Cursor`, and a policy:

- `manual` - always wait for the user's choice;
- `ask` - suggest the next agent;
- `automatic` - hand off automatically on a recognized `RateLimited`.

The automatic fallback must not fire on an unknown error and must account for cost, credential
availability and the user's policies for the specific agent.

## 2026-09-14: ZCode and extensible agent integration

### Requirement

ZCode joins the list of supported first-version clients along with Claude Code, Codex and Cursor.
The installation and execution architecture must not be closed over a fixed list: in the future a
new agent must be added by a separate adapter without changing StitchFlow's domain logic.

### Architectural decision

Separate the notions:

- **Agent Type** - the kind of client (`claude-code`, `codex`, `cursor`, `zcode`, `opencode`, ...);
- **Agent Installation** - a discovered instance of the client on a specific machine;
- **Agent Adapter** - the integration module that knows the client's formats and capabilities;
- **Agent Assignment** - the choice of an agent type/installation for a status or a card;
- **Agent Attempt** - a concrete attempt of that agent to execute a stage.

The card, workflow, memory and priority domain works only with universal identifiers and
capabilities. It contains no `switch`/`if` over specific client names.

### The adapter contract

Conceptual interface:

```text
IAgentAdapter
  id / displayName / version
  detectInstallations()
  getCapabilities()
  planProjectInstall(projectContext)
  applyProjectInstall(plan)
  validateConnection(projectContext)
  launch(stageExecution)
  pause/cancel/resume(attempt)
  classifyExit(output, exitCode)
  buildHandoffContext(stageExecution)
  uninstallProjectIntegration(projectContext)
```

Capabilities describe rather than assume support:

- MCP `stdio`;
- MCP Streamable HTTP;
- workspace/user MCP configuration;
- authorization headers;
- project instructions/rules;
- `SKILL.md`;
- slash commands;
- hooks/session-start;
- headless/non-interactive launch;
- resuming an existing session;
- structured output;
- rate limit recognition;
- remote workspace.

The orchestrator picks behavior by capabilities. For example, no hooks means a best-effort rule
via instructions/skills, and no HTTP enables the stdio proxy.

### Extending without breaking Native AOT

Native AOT does not imply reliable dynamic loading of arbitrary .NET assemblies. Therefore:

- the official first-version adapters compile into StitchFlow;
- simple future adapters are described by a declarative manifest + configuration templates;
- a complex external adapter can ship as a separate executable talking to StitchFlow over a
  versioned JSON-RPC/stdio contract;
- the domain core loads no third-party DLLs at runtime.

Proposed layout for custom adapters:

```text
<StitchFlow user data>/adapters/<adapter-id>/
  adapter.json
  templates/
  skills/
  commands/
  optional-adapter-executable
```

### Safe and repeatable installation

Every adapter first builds an `InstallationPlan` that the UI shows to the user:

- the discovered client and version;
- files created and modified;
- the MCP transport and endpoint;
- installed skills/commands/rules/hooks;
- warnings and unsupported capabilities.

Installation must be idempotent, preserve foreign configuration and mark StitchFlow-managed
entries. `dry-run`, a post-install check, updates and surgical removal of only StitchFlow-owned
entries are required.

### Built-in ZCode integration

ZCode supports `stdio`, HTTP and SSE MCP servers. For StitchFlow the project-scoped Streamable
HTTP endpoint is preferred. The adapter accounts for these native paths:

- workspace MCP: `<project>/.zcode/config.json`, key `mcp.servers`;
- fallback compatibility: `<project>/.agents/mcp.json`, key `mcpServers`;
- workspace skills: `<project>/.zcode/skills/<skill-name>/SKILL.md`;
- commands: the ZCode workspace commands directory;
- plugin package: `.zcode-plugin/plugin.json`, `skills/`, `commands/`, `hooks/`, `.mcp.json`.

If the native `.zcode/config.json` contains MCP servers, ZCode does not merge them with
`.agents/mcp.json` of the same scope. So the adapter reads the existing configuration first and
updates the native file without relying on both at once.

The recommended MVP ZCode install plan:

1. add the StitchFlow MCP to `.zcode/config.json` with the project-scoped HTTP endpoint;
2. install the `stitchflow-*` skills into `.zcode/skills` or as a single local ZCode plugin;
3. add the `/stitchflow-ui` command;
4. install project instructions through a ZCode-supported/`AGENTS.md` mechanism;
5. run a health/context check from ZCode;
6. do not install project hooks directly if the current ZCode version does not execute them;
   distribute hooks via a trusted plugin or a user-level setting.

### The initial catalog of official adapters

- `claude-code`;
- `codex`;
- `cursor`;
- `zcode`;
- then `opencode` and other clients under the same contract.

### Open question

Decide the ZCode MVP delivery format: the installer directly modifying the workspace
configuration, or generating a local `stitchflow` ZCode plugin bundling MCP, skills and slash
commands. The recommendation is the plugin as the primary integration package, with direct
configuration writes kept as a fallback for automatic/minimal installs.

### Confirmation

ZCode means the Z.ai product with the native `.zcode` directory. ZCode is an officially supported
built-in MVP adapter. Further decisions about its installation must rely on the ZCode MCP, skills,
commands and plugin formats.

## 2026-09-14: formalizing the spec and starting development

- The normative specification was written into `docs/technical-specification.md`.
- On any discrepancy the normative spec defines the current decision, and this file keeps the
  history.
- A local Git repository was created for the project.
- The source materials and the spec were committed in the root commit on `main`.
- Development happens on a separate `feature/stitchflow-foundation` branch without merging into
  `main`.
- The foundation covers Domain, Application contracts, the agent adapter contract, the Native AOT
  server skeleton and runnable domain checks.

## 2026-09-14: the unified agent installation wizard

- StitchFlow has one installer/project initialization wizard.
- The wizard shows Claude Code, Codex, Cursor, ZCode and future discovered adapters in one list
  with checkboxes; the user selects all the needed integrations in one pass.
- An adapter-specific `InstallationPlan` is built independently for every checked agent.
- A shared confirmation screen groups files, capabilities and warnings by agent.
- Re-running is idempotent: one integration can be added or removed without reinstalling the
  others.
- A partial failure is shown per adapter and must not mask the successful installation of the
  rest.

### Product language and localization (decided)

- **UI text lives in resources.** The neutral `src/Aiko.Pwa/Resources/Loc.resx` is English and the
  `Loc.ru.resx` satellite is Russian. The language is chosen from the browser's preferences at startup
  (see `Program.cs`, `ApplyBrowserLanguageAsync`) rather than pinned at build time; an untranslated
  language falls through to the neutral (English) strings. Adding a language is a new
  `Loc.<culture>.resx` plus `SatelliteResourceLanguages`.
- Strings and documentation are guarded by tests: `Aiko.Pwa.Specs/LocalizationSpecs` requires every
  translated key to exist in the neutral resources and the neutral ones to contain no Cyrillic.
- **Documentation is kept in two versions** (`docs/en`, `docs/ru`), and so is the README (`README.md`,
  `README.ru.md`).
- **Data from the previous product name (`StitchFlow`) is not migrated automatically.** The project
  directory is `.aiko` and the database is `%LOCALAPPDATA%/Aiko/aiko.db`. Compatibility is implemented
  for agent configurations only: the adapters recognise and replace legacy entries from the old name.
  Users move project data themselves (rename the directory, or run `aiko init` again on the same path
  and let `aiko reindex` rebuild the projections).

