# Aiko - Agent Integration

Aiko connects Claude Code, Codex, Cursor, ZCode and Cline through a per-project MCP endpoint.

## MCP endpoints

Each project gets a Streamable HTTP MCP endpoint:

```text
http://127.0.0.1:<port>/mcp/projects/<handle>
```

`<handle>` is the project's readable slug - the same one the UI's URLs carry. The daemon resolves a project
by its handle or by its id, so an endpoint written before slugs existed keeps working.

A daemon-level endpoint (no project) exposes global operations:

```text
http://127.0.0.1:<port>/mcp
```

Clients without reliable Streamable HTTP use the stdio proxy:

```text
aiko-stdio --url http://127.0.0.1:<port>/mcp/projects/<handle>
```

## The tool set

- **Project context** - `aiko_get_project_context`, `aiko_open_ui`.
- **Reading the project's state** - `aiko_list_board`, `aiko_list_work_queue`, `aiko_get_settings`. These are
  how an agent finds out what the project says; opening a file under `.aiko` to learn it is a violation (a
  card whose subject *is* the `.aiko` format is the exception, and says so).
- **Cards** - `aiko_list_cards`, `aiko_get_card`, `aiko_create_card`, `aiko_update_card`,
  `aiko_estimate_card`, `aiko_move_card`, `aiko_take_card`, `aiko_link_cards`, `aiko_add_comment`,
  `aiko_list_comments`.
- **Card artifacts** - `aiko_get_card_artifact`, `aiko_save_card_artifact`: the Markdown beside a card
  (`issue.md`, `analysis.md`, `implementation.md`, ...). `aiko_get_card` already carries the list of paths and
  the text of `issue.md`, so the common read is one call.
- **Queue** - `aiko_list_commands`, `aiko_claim_command`, `aiko_finish_command`: the requests a screen placed
  for an agent, not to be confused with `aiko_list_work_queue`, which is the cards to work.
- **Execution** - `aiko_start_stage`, `aiko_report_progress`, `aiko_request_scope_expansion`,
  `aiko_complete_stage`, `aiko_pause_execution`, `aiko_handoff_execution`,
  `aiko_resume_execution`, `aiko_report_agent_state`, `aiko_report_commit`, `aiko_approve_commit`.
- **Memory** - `aiko_search_memory`, `aiko_store_memory`.
- **Daemon (global)** - `aiko_init_project`, `aiko_list_projects`, `aiko_list_templates`,
  `aiko_create_card_in_project`, `aiko_link_project`, `aiko_unlink_project`, `aiko_doctor`,
  `aiko_reindex`, `aiko_get_settings`, `aiko_token`, `aiko_backup`.

Tool descriptions instruct agents to fetch the project context first; the installed skills and
rules reinforce this per agent.

## Where work happens

A card starts in its type's `backlog` stage, and a card sitting there has no work in it yet: work belongs to
a stage execution, and a stage that ran is what leaves an execution, its artifacts and its history behind.
So the order is: create the card, start the stage you are working in with `aiko_start_stage` (the start moves
the card into that stage), do what that stage's instruction asks for, produce the artifacts it requires,
complete it with `aiko_complete_stage`, then move on. `aiko_move_card` advances a card one stage at a time
and refuses to leave a stage that was never run, so a card cannot be declared finished by moving it. A person
dragging a card on the board is deliberately not held to that rule: the board is how a person corrects their
own board, and `aiko doctor` reports the cards that were pushed past a stage anyway.

**One run is one stage.** A request in the chat is not a run: it creates a card in the backlog
(`aiko_create_card`, or `/aiko-create`) and stops there - the work begins when the user asks for it ("выполни",
`/aiko-run <cardId>`). An order is still a request: «поправь X» earns a card, not a run. That run performs
exactly one stage: `aiko_start_stage` for the stage the card is in,
the work, `aiko_complete_stage`, then stop. Running the card again while its stage is unfinished continues
that same run, so a half-finished stage is never stepped over, and starting the next stage is refused until
the current one is completed. `/aiko-run <cardId> --all` is the explicit exception: it walks the pipeline by
itself and descends into the card's children - creating the ones a container's stage calls for and working each
child to the end of its own pipeline before returning to its parent, with the parent's run parked so the whole
tree keeps one run slot - and even then it stops when a stage asks the user a question, when an agent fails or
hits its rate limit, when a stage's policy forbids an action, or when a required artifact cannot be produced.

A stage is completed only when its required artifacts are in place and the card was re-estimated during that
run: `aiko_complete_stage` refuses otherwise, because the readiness criterion is what says the work is done.
A project that defines no criteria has nothing to estimate and completes without the check.

A card can wait for another one: `blocks` is a directed relation - the blocking card is `sourceCardId`, the
waiting card `targetCardId` - and Aiko reads it rather than only storing it. `aiko_get_card` reports the
unfinished blockers in `blockedBy`, beside the card's own fields, and `aiko_start_stage` **refuses** a blocked
card, naming the blocking card and the stage it sits in. An agent that meets that refusal says so to the user
and offers the blocking card; it does not work around it. A blocker stops blocking when it reaches the last
stage of its own pipeline - the last stage is read from the workflow, so a type whose end is not called `done`
works too - and the relation is removed when the order no longer holds.

A project can be **linked** to other projects, each with a sentence saying what that project is for. The
registry lives in `.aiko/links.json`, `aiko_get_project_context` lists it under *Linked projects* for the agent
working here, and the project's *Linked projects* screen - the rail item beside *Project settings* - edits it.
Work that belongs to a linked project is filed there
with `aiko_create_card_in_project`, passing `originProjectId` and `originCardId` so the receiving card records
where it came from - read that project's context first, because its card types, its stages and its rules are
its own. `aiko_link_project` and `aiko_unlink_project` (/aiko-link) write and remove a link; a project nobody
registered, and the project itself, cannot be linked.

A run is in one of five states the card page and the board show: **pending** (no run yet), **running**,
**paused** (paused, failed or rate-limited), **waiting for a decision** and **completed**. "Running" means the
agent reported it, not that its process is alive: Aiko never starts agents.

`aiko_get_project_context` also states the project's **git policy** - whether Aiko's `.aiko` tree is tracked
(`LocalOnly` ignores it and keeps it on the machine, `TrackProjectKnowledge` commits the project knowledge,
`Custom` leaves `.gitignore` to the person) - and its **commit policy** for the shared checkout: `Allow`
(make the commit and record it with `aiko_report_commit`), `Ask` (propose it and wait for the answer) or `Deny`
(do not commit). Aiko never runs git itself and never creates a commit; it only records what an agent reports.

The card's feed is the notebook between stages: the project context asks the agent to read it with
`aiko_list_comments` before it works a stage and to post the outcome with `aiko_add_comment` before it completes
it - including what the next stage or agent will need - signed with its own adapter id, so what one stage
learned is not lost on the next.

## Authentication

The daemon authenticates its MCP endpoint with the local access token, so **every generated MCP entry
carries it** - without it the daemon answers `401` and the agent simply never sees Aiko:

- clients whose configuration holds headers (Claude Code, Cursor, ZCode, Cline) get
  `"headers": { "Authorization": "Bearer <token>" }`, written in the same shape the client's own CLI
  produces (Claude Code also gets its `"type": "http"` tag);
- Codex cannot hold a literal header, so its entry names the environment variable its own CLI writes:
  `bearer_token_env_var = "AIKO_TOKEN"`. Export `AIKO_TOKEN` with the value of `aiko token show` in the
  shell you start Codex from.

Because the token lives in those files, they must not be committed. Project initialization adds them to
`.gitignore` (`/.mcp.json`, `/.cursor/mcp.json`, `/.zcode/config.json`, plus the `.aiko` entry the git
policy already covers); for a project initialized earlier, add the lines by hand. The directories Aiko writes
agent files into (`/.cline/`, `/.clinerules/`, `/.codex/`, `/.agents/`) are added too, because every install
and repair rewrites them. `AGENTS.md` and `CLAUDE.md` are not added: those are shared instructions, and Aiko
only puts a marked block in them.

`aiko doctor` reports a configuration whose endpoint is right but which carries no credential, and
`aiko repair --fix` rewrites it.

## Skills, commands and rules

Two channels, because a contract and a procedure are not the same thing. A **skill** is a procedure a model
loads when it judges the description relevant; a **rule** is read on every run, which is what the working
contract needs.

Project-scoped procedures (installed with `aiko agent install --project <id>`) are written as skills, and as
slash commands for the clients that have commands - Claude Code and ZCode:

`aiko-create`, `aiko-create-sub`, `aiko-estimate`, `aiko-run`, `aiko-run-all`, `aiko-commands`, `aiko-scope`,
`aiko-handoff`, `aiko-memory`, `aiko-status`, `aiko-ui`.

`/aiko-create <type> <description>` takes the card type as its first argument (`/aiko-create bug The
dropdown is empty`) and reads the project context to resolve it, so it covers every type without being
regenerated. The card is named by Aiko - a person never invents an id - and it is always created in that
type's backlog stage, because a card nobody has worked out has no business starting anywhere else. In the
same pass the agent estimates the card: it judges the size step and every criterion from the description
and calls `aiko_estimate_card`, so a card created from a command is never left unranked. The card also carries
the user's own wording in `request` - the raw request, not the reworked task - and the agent's description of
the work in `requirements`: the first is fixed once the card leaves the backlog, the second keeps changing and
every change is explained in the card's discussion. Alongside
`/aiko-create` the daemon installs one `/aiko-create-<type>` per card type the project defines -
`/aiko-create-story`, `/aiko-create-task`, and one for every type you add in the workflow editor. Those
per-type commands are a projection of the project's workflows: they are re-written when a type is created
or removed, and only for the agents already connected to that project.

`/aiko-create-sub <parentCardId> <type> <description>` is the same creation under an existing card: the
agent creates the sub-card, links it with `aiko_link_cards` passing the parent as `sourceCardId`, the new
card as `targetCardId` and `parent-child` as the relation type, and estimates it. The edge points from the
parent to the child, which is the direction the board reads as "this card belongs under that one".

`/aiko-estimate <cardId>` re-estimates a card on its own, which is what the card page's *Ask to estimate*
action hands to an agent. The agent reads the card and the project context (the size grid's descriptions
and each criterion's range) and calls `aiko_estimate_card` with the size step and the scores.

`/aiko-run <cardId> [stageId]` runs a card: it starts the stage with `aiko_start_stage`, does what that
stage's instruction asks for, produces its artifacts, reports progress and completes the stage. A stage id
names the stage to start, and the start moves the card into it - which is why there is no separate move
command. Nothing about it names a stage of a particular pipeline, so it works in any of them - the
per-stage commands it replaces (`/aiko-analyze`, `/aiko-implement`, `/aiko-review`, `/aiko-complete`) and
`/aiko-next-stage` all named stages of the default template, the same hardcoding card types no longer have.

`/aiko-commands` empties the project's command queue: the requests a person placed on a card while no agent
was running. Aiko does not start agent processes, so the button on the card page writes a command into
`.aiko/commands.json` and nothing else; this procedure is what carries it out. The agent reads the queue
with `aiko_list_commands`, takes one command with `aiko_claim_command` (a refusal means another agent has
it, or the person placed it for a different agent), does what its action asks - `Start` calls
`aiko_start_stage` and then works the stage exactly as `/aiko-run` does, `Pause` and `Resume` call
`aiko_pause_execution` and `aiko_resume_execution`, `Answer` writes the text into the card's feed and wakes
the run - and closes it with `aiko_finish_command`, `completed` or `failed` with a message. An agent must
never leave a taken command open: the queue is what the person reads to see whether their request happened.

`/aiko-run-all` works the whole board: every card whose pipeline is unfinished, in board order, each driven
to the end of its own workflow. The order comes from `aiko_list_board`, which returns the same snapshot the
interface draws - the board's priority is computed from the card and its parent, so sorting the cards by
their own score would pick a different first card than the one at the top of the screen. A card that is
already finished is skipped, and so is one that waits for another card, which is read from the refusal of
`aiko_start_stage` rather than by walking the blocking graph in the procedure. A question for the person
does not stop the pass: the question goes into that card's feed, its run goes into `waiting-for-user`, and
the pass takes the next card. The pass stops when it cannot go on at all - a failure or rate limit, a
forbidden action, an artifact that cannot be produced - and a second run resumes where the first stopped
without any bookkeeping, because the board itself says what is left: finished cards are not picked again, a
card waiting for an answer is refused a restart by the same gate, and a card the pass stopped on continues
its own run. This is also what the board's *Work the board* button asks for: the button places a `RunBoard`
command the way the card page places a command for a card, and any agent that runs may take it.

Global skills/commands (installed with `aiko agent install --scope user`):

`/aiko-init [templateId]`, `/aiko-list-projects`, `/aiko-status`, `/aiko-doctor`, `/aiko-repair`, `/aiko-agents`,
`/aiko-settings`, `/aiko-token`, `/aiko-backup`, `/aiko-ui`.

User scope is the machine-wide connection: it is what the dashboard's *Agents* card reports as
**connected**, and its Connect / Disconnect buttons write and remove exactly these files. The
project-scoped MCP entry is separate and per project, so an agent can be connected globally and still
need `aiko agent install --project <id>` for a particular project.

The behavior contract: any work item starts with a card; read context before acting; warn before
changing files outside the declared scope; report progress, actual files and commits through Aiko.
It is installed in the rule channel, not as a skill, because it has to hold whether or not a model decides
that a skill is relevant. The project context lists every card type the project defines with the stages of
its pipeline, so a type
added in the workflow editor is one an agent can create immediately. It also carries the project's
initialization instruction when its template came with one, which is what an `/aiko-init` run finishes by
carrying out.

## Parity: skill, tool, UI

The same work is reachable from an agent, over MCP and - for most of it - from the board. Where the UI
has no path yet, the table says so rather than pretending the sets are already equal.

`aiko_open_ui` returns a page address directly: `/p/<handle>/board` for the project and
`/p/<handle>/cards/<cardId>` for a card, always with the project's readable handle. A link an agent hands
over is therefore the same address the UI links to itself, not a second form of it.

| Action | Agent skill | MCP tool | UI |
| :-- | :-- | :-- | :-- |
| Register a project from a chosen template | `/aiko-init [templateId]` | `aiko_list_templates`, `aiko_init_project` | Dashboard - *Add project* (folder browser + template select) |
| List projects | `/aiko-list-projects` | `aiko_list_projects` | Dashboard - project list |
| Open the board | `/aiko-ui` | `aiko_open_ui` | `aiko ui`, or the URL in the app bar |
| Create a card of any type the project defines | `/aiko-create <type> <description>`, `/aiko-create-<type>` | `aiko_create_card`, `aiko_create_card_in_project`, `aiko_estimate_card` | Board - *Create card* |
| Create a sub-card under a card | `/aiko-create-sub <parentCardId> <type> <description>` | `aiko_create_card`, `aiko_link_cards`, `aiko_estimate_card` | Board - *Create card*, linked on the card page |
| Read the board | `/aiko-status` | `aiko_list_cards`, `aiko_get_card` | Board, and the card as its own page |
| Edit a card | — | `aiko_update_card` | Card page - *Save card* |
| Estimate a card's size and scores | `/aiko-estimate <cardId>` | `aiko_estimate_card` | Card page - *Ask to estimate* |
| Move a card between stages | `/aiko-run <cardId> <stageId>` | `aiko_move_card` | Drag a card between columns |
| Start a stage | — | `aiko_start_stage` | — (the board shows the resulting state) |
| Report progress | `/aiko-run` | `aiko_report_progress` | Card page - execution history |
| Request scope expansion | `/aiko-scope` | `aiko_request_scope_expansion` | Card page - declared vs actual files |
| Hand off to another agent | `/aiko-handoff` | `aiko_handoff_execution` | Card page - execution history |
| Complete a stage | `/aiko-run` | `aiko_complete_stage` | Drag to the next column |
| Post the stage's outcome into the card's discussion | `/aiko-run` | `aiko_add_comment`, `aiko_list_comments` | Card page - discussion tab |
| Record and search memory | `/aiko-memory` | `aiko_store_memory`, `aiko_search_memory` | — (not in the UI yet) |
| Edit the pipeline | — | — | Workflow page |
| Read settings | — | `aiko_get_settings` | Settings page |
| Change settings | `/aiko-settings` | `aiko_update_settings` | Settings page |
| Rebuild projections | — | `aiko_reindex` | — (`aiko reindex`) |
| Diagnose the installation | `/aiko-doctor` | `aiko_doctor` | — (`aiko doctor`) |
| Repair the installation | `/aiko-repair` | — | — (`aiko repair --fix`) |
| Back up a project | `/aiko-backup` | `aiko_backup` | — |
| Link a project and say what it is for | `/aiko-link <slug> <description>` | `aiko_link_project`, `aiko_unlink_project` | Rail - *Linked projects* |
| File a card into a linked project | `/aiko-link` for the link, then the project's own contract | `aiko_create_card_in_project` (`userConfirmed` on an *Ask* project) | — (only an agent files the card; it lands on the target project's board) |
| Decide whether a linked project may be written to | — | — (the `crossProject` section of the sending project's settings) | Project settings - *Cross-project writing* |
| See where a cross-project card came from | — | — (the card carries its own `origin`) | Card page - *Origin*: the source project, the source card when one was named, the adapter and how long ago |
| Manage a run - start, pause, resume, hand off, complete, cancel | `/aiko-run`, `/aiko-handoff` | `aiko_start_stage`, `aiko_pause_execution`, `aiko_resume_execution`, `aiko_handoff_execution`, `aiko_complete_stage`, `aiko_report_agent_state` | — (the card page reads the run history; the controls, and the REST lifecycle behind them, are not built) |
| See every active run of the project | — | — (the board snapshot carries the latest run of each stage) | — (runs are visible inside a card only; no project-wide section) |
| Read the project's event journal | — | — | — (`GET /api/v1/projects/{id}/events/history` exists; nothing reads it) |
| Move a card with explicit stage actions (next, return, cancel) | — | `aiko_move_card` | — (drag only; the explicit actions are not built) |
| Ask an agent to do something from the board | `/aiko-commands` | `aiko_list_commands`, `aiko_claim_command`, `aiko_finish_command` | Card page - *Command for an agent*: place one, and see whether it is waiting or taken |
| Work every unfinished card of the board | `/aiko-run-all` | `aiko_list_board`, then the run tools per card | Board - *Work the board*: places the request and shows what became of it |
| Show the access token | `/aiko-token` | `aiko_token` | — (`aiko token show`) |
| Manage agent integrations | `/aiko-agents` | — (`aiko agent list` / `install` / `uninstall`) | Dashboard - the *Agents* card: detection and Connect / Disconnect |

An agent is detected by every signal it leaves behind, not only by an executable: a CLI is found on `PATH`,
while a desktop app or an IDE extension - which has nothing on `PATH` at all - is found by its own data
directory (`~/.claude`, `~/.codex`, `~/.cursor`, `~/.zcode`, `~/.cline`). Detection decides what the dashboard
offers and what a repair touches; it never runs the agent, so no version is reported.

## What the installer writes

Per project (`aiko agent install --project <id>`). The contract and the procedures travel separately:

| Agent | Rule - the contract, read every run | Skills and commands - the procedures |
| :-- | :-- | :-- |
| Claude Code | `CLAUDE.md` block | `.claude/skills/aiko-*/SKILL.md`, `.claude/commands/aiko-*.md` |
| Codex | `AGENTS.md` block | `.agents/skills/aiko-*/SKILL.md` (no commands) |
| Cursor | `.cursor/rules/aiko.mdc` | — (neither skills nor commands) |
| ZCode | `AGENTS.md` block | `.zcode/skills/aiko-*/SKILL.md`, `.zcode/commands/aiko-*.md` |
| Cline | `.clinerules/aiko.md` | `.cline/skills/aiko-*/SKILL.md` (no commands) |

One create procedure per card type the project defines is written alongside the rest, and both are a
projection of the workflows: they are re-written when a type is created or removed, and only for the agents
already connected to that project.

Codex and ZCode share the `AGENTS.md` block: it is the cross-client mechanism ZCode reads project
instructions from, and the content is identical, so two adapters merge into one block instead of fighting
over the file.

Globally, for every user (`aiko agent install --scope user`, which is what the installer runs). One
orientation skill explains the flow; the actions are commands:

| Agent | Files |
| :-- | :-- |
| Claude Code | `~/.claude/skills/aiko/SKILL.md`, `~/.claude/commands/aiko-*.md` |
| Codex | `~/.codex/skills/aiko/SKILL.md`, `~/.agents/skills/aiko/SKILL.md` |
| Cursor | `~/.cursor/rules/aiko.mdc` |
| ZCode | `~/.zcode/skills/aiko/SKILL.md`, `~/.zcode/commands/aiko-*.md` |
| Cline | `~/.cline/skills/aiko/SKILL.md`, `~/.agents/skills/aiko-*/SKILL.md` |

Cline gets its procedures globally as well as in the workspace, and the duplication is deliberate: a Cline
build that does not surface workspace skills - the desktop app reads the global root and leaves a project's
`.cline/skills` alone - would otherwise find no procedure at all. The generic ones are project-agnostic by
design, because each reads the project context at run time, so one global copy serves every project. A
procedure per card type stays in the workspace: a type is one project's data, and the type-agnostic
`aiko-create` covers it.

A user-scope procedure therefore starts by working out which project it is in, because it is visible in every
folder while the project is whichever folder the user has open. It runs
`aiko project find "<working directory>"`, which answers with the project - a folder inside one resolves to
it - or with one of the two ways there is none: a folder that carries `.aiko` but is not registered with the
daemon, and one that is not an Aiko project at all. In both cases the procedure reports the matching
`aiko init` command and stops, rather than acting on some other project. A workspace copy needs none of this:
it sits in the project, and the client that reads it is configured for that project.

Creating a project is the other half: the init command is generated per adapter and names that adapter, so
`aiko init <path> --agent <id>` registers the project and connects the agent in one step. The step is
idempotent - it writes only what is missing, and an agent already connected to the project is reported as such
instead of failing, which is what a second agent running init on the same project needs. A project created
from the dashboard's form takes the other road: the form offers the agents that are already connected for the
user, and the ones ticked there are connected to the new project in the same gesture.

Codex loads user-scope skills from its own root, `~/.codex/skills` (its built-ins live in
`~/.codex/skills/.system`), so the global skill is written there; the portable `~/.agents/skills` copy
is kept for layouts that read that tree. Set `AIKO_USER_HOME` to install into a different home
directory.

Cline keeps MCP servers **globally only** - a workspace carries skills and rules, not a server - so a
project install adds one entry named after the project's own handle (`aiko-<handle>`) to both files Cline
reads: `~/.cline/data/settings/cline_mcp_settings.json` (desktop app and IDE extension) and
`~/.cline/mcp.json` (CLI). Several projects can be connected at once; Cline enables and disables
servers per session. Its entry spells out `"type": "streamableHttp"`, because Cline falls back to the
legacy SSE transport when the type is missing. Cline receives the project procedures as skills - it has no
commands - and they are named after the procedures (`aiko-create`, `aiko-run`, ...) rather than `aiko`,
which is the name the global skill holds and Cline resolves first. That precedence is why the workspace
skill used to be called `aiko-project`. The contract itself sits in `.clinerules/aiko.md`, which Cline
reads on every run.

Installation is idempotent and preserves your own settings; uninstall removes only Aiko-managed
content (MCP entries, managed blocks, and files carrying the Aiko ownership marker).

## Rate limits and handoffs

When an agent reports `rate-limited`, hand the execution to another agent with
`aiko_handoff_execution`. The full attempt history is preserved.
