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

Open a card to edit the title, priority and declared scope, and to view relations,
executions, agent attempts, progress and artifacts.

## Workflows

A workflow is an ordered list of stages. Each stage has a title, an agent instruction, the card
kinds it accepts, a default agent, required artifacts and action policies. Edit workflows in the
UI (the **Workflow** button). Removing a stage that still contains cards is rejected.

## Settings

Two levels of settings exist:

- **Global** (`app-settings.json` next to the database) - defaults and daemon-level choices.
- **Project** (`.aiko/settings.json`) - the effective values for one project.

Per section, project settings override global settings, which override the safe defaults.
Currently configured: execution (workspace mode, max concurrent runs, scope-overlap and commit
policies) and priority weights.

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
