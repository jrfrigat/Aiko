# Aiko - User Guide

## The project

The project page (the first item of the rail's **Project** group) answers two questions: what this project is
and how its cards
are doing. It shows the project's path, its workflows and how many stages they have, the board projections
and the relations, plus the cards per stage of each pipeline. "Re-index project" rebuilds the daemon's index
from the `.aiko` files - the one write this screen performs, and one that is safe to repeat.

## The board

The board shows the project's cards in three projections:

- **Stories** - story cards grouped by their workflow stages.
- **Tasks** - task cards grouped by their workflow stages.
- **Combined** - stories and tasks together.

Drag a card between columns to move it to another stage: a card is picked up by its handle icon, while a
plain click opens the card's page. The move is saved with optimistic
concurrency; if the card changed elsewhere, the UI asks you to reload.

## Cards

Every card is a folder in `.aiko` with a `card.json` and Markdown artifacts. A card carries:

- `kind` - `story` or `task`;
- `title`, `workflowId`, `stageId`;
- `revision` - the optimistic revision;
- `ownPriority` and the computed effective priority (the task blends its own value with the
  maximum parent value using the project's priority weights);
- `declaredScopeFiles` (the intended files/globs) and `actualChangedFiles` (what was really
  changed). Files outside the declared scope are flagged as out-of-scope.
- `size` - the step of the project's size grid the card was given, for example `M`. The agent assigns
  it from the grid's descriptions, and the step's coefficient multiplies the card's score.
- `criterionValues` - the card's scores per criterion, when the project defines criteria.

A card's own score is the weighted average of its normalized criterion values, or its own priority when
the project defines no criteria; the size coefficient multiplies either. A task then blends that with the
highest parent value using the project's weights (ТЗ §10).

The card page is laid out in two columns: on the left the stage's scope - the editable stage instruction
(which belongs to the project's workflow), the editable declared scope, the declared and actual files and
the acceptance criteria - above one block of tabs: the completed outcomes, the code changes, the artifacts
and the runs; on the right the execution state with its assignees, the triage and score with the card's
rank on the board, the related cards (parents, children and plain relations - each links to its own page),
the progress of the stage's acceptance criteria, the stage's skills and the card's parameters (title,
priority, size and every scoring criterion of the project).

## Workflows

A workflow is an ordered list of stages. Each stage has a title, the card kinds it accepts, the
instruction the agent is given, the skills it invokes **before** and **after** that instruction, the
executors allowed to run it and its default agent, the artifacts it must produce (each with a policy for
when it is missing) and the commands that verify its outcome. Edit them in **Project settings**: the panel
lists the stages of the pipeline and lets you reorder them, and clicking a stage opens it in a drawer.
A project owns its own copy of the pipelines, so a change there never reaches another project - or a
project created later from the template. Removing a stage that still contains cards is rejected.

## Agent bridges

The **Daemon & MCP bridges** screen shows the daemon itself (version, address, PID, round-trip, MCP endpoint)
and one card per agent: whether it is installed on this machine and whether it is attached to Aiko. The button
on a card attaches the bridge - it writes Aiko's global skills and commands for that agent - and detaches it
again. The two facts are independent: an agent can be installed without being attached, or found only partly
configured, in which case the button completes it. An agent that is not on `PATH` shows the
`aiko agent install` command instead of the button. The MCP entry for one project is added separately:
`aiko agent install --project <id>`.

## Settings

Three levels exist, from widest to narrowest, and the narrower one wins:

- **Template** (`templates/default/template.json` next to the daemon's database) - what a new project is
  created from: the execution defaults, the scoring model (weights, criteria, size grid) and the pipelines
  with their stages. This is the level the **Workflow** screen edits, and editing it affects the
  projects created afterwards, never the ones that already exist.
- **Installation** (`app-settings.json` next to the database) - the daemon-level fallback for the sections a
  template leaves out. Aiko writes it at init; the screen does not edit it.
- **Project** (`.aiko/settings.json`) - the values this project actually runs with, copied in at creation.
  The **Project settings** screen edits this one, and only this one.

Currently configured: execution (workspace mode, max concurrent runs, scope-overlap and commit policies),
and the priority model - blending weights, the criteria with their ranges and agent instructions, and the
size grid.

## Workflow sets

A project is created from a **template**, and the template decides what it starts with: the stages of each
pipeline with their agent instructions and required artifacts, the board projections, the starting memory
and the default settings. Choose it in the dashboard's *Add project* form, with
`aiko init <path> --template <id>`, or through `aiko_init_project` after listing the options with
`aiko_list_templates` (the same list is available as `GET /api/v1/templates`).

The template is **copied** into the project, and the project owns its configuration from then on. Editing a
template - in the UI or by editing `templates/<id>/template.json` next to the daemon's database - affects
the projects created afterwards, never the ones that already exist. `.aiko/project.json` records
`templateId` and `templateVersion`, so a project can always say which template built it. The git policy is
part of the template: an init takes it from there unless the request names one.

The **Workflow** screen (`/templates`) lists them and owns everything else; a set's settings open by clicking
its name:

- **Capture from a project** - a new template out of an existing project: a snapshot of its settings, its
  pipelines, its projections, its memory and its git policy. It is how an installation pins a project it
  likes as the starting point of the next ones.
- **Duplicate** - a copy of a template, and the way to edit the shipped base: the base has no file of its
  own until it is copied.
- **Import / Export** - handing a template to another installation: it is written to a JSON file at the path
  you name and read back under its own identifier or one you give (which is how the same file is imported
  twice).
- **Delete** - removes a template from this installation's catalog; projects created from it are untouched.
- **Apply to a project** - the one action that reaches a project that already exists: the project's
  pipelines, projections and settings are replaced with the template's, its memory is filled in where the
  project has none, and it adopts the template's git policy - while its cards, runs and memory stay. The
  action is **refused** when a card would be left in a stage the template does not have, because work must
  not silently fall out of its pipeline.

Editing one template is the same defaults screen as before: `/templates/<id>` (and `/settings` for the base).

## Executions

Each card can have stage executions. An execution owns the workspace and keeps a history of
`AgentAttempt`s (one per agent run). Progress, scope changes, artifacts, commits and handoffs are
recorded. A rate-limited agent hands the execution to another agent without losing history.

## Git

Aiko does not reimplement git: it runs the `git` client on your machine as a command and reads what it says.
A missing client is reported as **"Git client unavailable"**; a directory that is not a repository says so
too. Both are answers, not errors - a screen has to be able to draw them.

What is read: the branch, the remote, the number of changed files, HEAD (short sha and subject), the recent
commits and the **diff of the card's files** - what the card declared in its scope or actually touched. The
diff comes from the working tree, and from the last commit that changed the files when they are committed
already.

Where it shows: the branch in the dashboard's project table, the diff in the card's **Code changes** tab
(the file, `+N`/`-N`, and the coloured patch). Aiko never writes to the repository - no commits, no branches:
commits stay under the policy below, and push is post-MVP.

## Discussion & commands

Every card has a feed: notes from people and reports from agents. A note is authored content, so it lives
with the card, in its own folder: `.aiko/<stories|tasks>/<cardId>/discussion.json`, next to `card.json`. The
**Discussion & commands** tab shows the feed (who, when, what) and a box for a new note; the `/benchmark` and
`/leak-check` chips add a command to the text. Nothing is launched from here: the daemon records work, you run
the agent - a command in the text is addressed to whoever opens the stage.

## Analytics

The project page carries two charts computed from what the daemon observed rather than from what you wrote:
**pipeline velocity** (cards that entered a stage, per week - every week of the window, quiet ones included)
and the **distribution** (by card kind and by size step). The daemon screen reports **uptime, runs, runs
without a clean stop, working set and managed heap**.

Those numbers live in SQLite on purpose: a stage transition is something the daemon observed, not authored
content. Losing the table costs a chart, not a card. Everything that matters - cards, settings, templates,
memory, discussion - is files, in the project and in Aiko's own directory.

## Commits

The project's commit policy controls who commits:

- `deny` (default) - you commit; the agent does not.
- `ask` - the agent reports a commit and waits for your approval (the execution becomes
  `waiting-for-user`; approve with `aiko_approve_commit` or in the UI).
- `allow` - the agent commits and reports the SHA.

## Memory

Durable project memory lives in `.aiko/memory` as Markdown and is full-text searchable. Use
`aiko_search_memory` / `aiko_store_memory` (or `/aiko-memory`) for decisions, conventions and
lessons.

## Cross-project cards

An agent working in one project can report a story or task into another project. The created card
records its origin (`originProjectId`); the target project simply sees where it came from.
