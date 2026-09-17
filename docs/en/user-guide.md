# Aiko - User Guide

## The board

The board shows the project's cards in three projections:

- **Stories** - story cards grouped by their workflow stages.
- **Tasks** - task cards grouped by their workflow stages.
- **Combined** - stories and tasks together.

Drag a card between columns to move it to another stage. The move is saved with optimistic
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

The card page is laid out in two columns: on the left the stage's scope with the declared and actual files
and the acceptance criteria, the last run's report, the block the commit diff will fill, the artifacts and
the run history; on the right the execution state with its assignees, the triage and score with the card's
rank on the board, the related cards (parents, children and plain relations - each links to its own page),
the progress of the stage's acceptance criteria, the stage's skills and the card's parameters (title,
priority, size, declared scope).

## Workflows

A workflow is an ordered list of stages. Each stage has a title, the card kinds it accepts, the
instruction the agent is given, the skills it invokes **before** and **after** that instruction, the
executors allowed to run it and its default agent, the artifacts it must produce (each with a policy for
when it is missing) and the commands that verify its outcome. Edit them in **Project settings**: the panel
lists the stages of the pipeline and lets you reorder them, and clicking a stage opens it in a drawer.
A project owns its own copy of the pipelines, so a change there never reaches another project - or a
project created later from the template. Removing a stage that still contains cards is rejected.

## Settings

Three levels exist, from widest to narrowest, and the narrower one wins:

- **Template** (`templates/default/template.json` next to the daemon's database) - what a new project is
  created from: the execution defaults, the scoring model (weights, criteria, size grid) and the pipelines
  with their stages. This is the level the **Global settings** screen edits, and editing it affects the
  projects created afterwards, never the ones that already exist.
- **Installation** (`app-settings.json` next to the database) - the daemon-level fallback for the sections a
  template leaves out. Aiko writes it at init; the screen does not edit it.
- **Project** (`.aiko/settings.json`) - the values this project actually runs with, copied in at creation.
  The **Project settings** screen edits this one, and only this one.

Currently configured: execution (workspace mode, max concurrent runs, scope-overlap and commit policies),
and the priority model - blending weights, the criteria with their ranges and agent instructions, and the
size grid.

## Project templates

A project is created from a **template**, and the template decides what it starts with: the stages of each
pipeline with their agent instructions and required artifacts, the board projections, the starting memory
and the default settings. Choose it in the dashboard's *Add project* form, with
`aiko init <path> --template <id>`, or through `aiko_init_project` after listing the options with
`aiko_list_templates` (the same list is available as `GET /api/v1/templates`).

The template is **copied** into the project, and the project owns its configuration from then on. Editing a
template - in the UI or by editing `templates/<id>/template.json` next to the daemon's database - affects
the projects created afterwards, never the ones that already exist. `.aiko/project.json` records
`templateId` and `templateVersion`, so a project can always say which template built it.

## Executions

Each card can have stage executions. An execution owns the workspace and keeps a history of
`AgentAttempt`s (one per agent run). Progress, scope changes, artifacts, commits and handoffs are
recorded. A rate-limited agent hands the execution to another agent without losing history.

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
