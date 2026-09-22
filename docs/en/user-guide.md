# Aiko - User Guide

## The project

The project page (the first item of the rail's **Project** group) answers two questions: what this project is
and how its cards
are doing. It shows the project's path, its workflows and how many stages they have, the board projections
and the relations, plus the cards per stage of each pipeline. "Re-index project" rebuilds the daemon's index
from the `.aiko` files - the one write this screen performs, and one that is safe to repeat.

A project has two identifiers. The readable **handle** - `aiko`, as in `/p/aiko/board` - is derived from the
folder name and can be set while the project is created; it is what every link the interface builds uses. An
address that names the project by the GUID instead is rewritten to the handle as soon as the page opens, so
the address bar never keeps a form the interface itself would not produce. The internal project id is a
generated GUID: it is written into `card.json`, into the relations and into the MCP endpoint of every
connected agent, so it never changes. Every route still accepts both, so a link saved before the handle
existed keeps working.

## The board

The board shows the project's cards as one section per card type the project declares. A **card types**
control in its toolbar narrows the page: **Combined** means no filter at all, and one or more types may be
picked at once, so two pipelines can be read side by side. The choice is a filter on this page and not a
route of its own, so the address stays `/p/<project>/board` while it changes. The list is built from the
project's workflows, so a type you added yourself appears here immediately and without a code change. Drag a
card between columns to move it to another stage: a card is
picked up by its handle icon, while a plain click opens the card's page. The move is saved with optimistic
concurrency; if the card changed elsewhere, the UI asks you to reload.

### The archive

The board is work; the **archive** is what the work left behind, and the view group in the toolbar switches
between them. A finished card can be put away from its own page - the button beside its runs, offered once the
card has reached the end of its pipeline - and from then on it is out of the columns and out of every count,
while its page, feed, runs, artifacts, scores and relations stay exactly as they were. The archive view lists
what is in there with each card's type, the stage it was put away in and when, and returns one to the board with
a single button. Putting a card away never deletes anything, and the card list an agent reads leaves the archive
out as well - unless the agent asks for it with `includeArchived`.

## The backlog

The **backlog** is not a column: it is every card that has not been taken into work yet, gathered from the
reserved `backlog` stage of each workflow and grouped by card type. "Take into work" moves a card to the
next stage of its own pipeline. The `backlog` column cannot be removed - it is where a card enters its
pipeline, and without it a new card would have nowhere to go.

## Cards

Every card is a folder in `.aiko` with a `card.json` and Markdown artifacts. A card carries:

- `kind` - the card type id (`Story`, `Task`, or any type the project declares). The type is defined by the
  project's workflow, so a project can add its own type without a code change;
- `title`, `workflowId`, `stageId`. The id and the starting stage are Aiko's, not the caller's: a card is
  named by the daemon (the type id plus the lowest free number, for example `TASK-3`) and is always created
  in that type's `backlog` stage, because a card nobody has worked out should not start anywhere else;
- `requirements` - what the card is asked to do, in the words of whoever wrote it, kept in the card's
  metadata;
- `request` - what the user asked for, in their own words, kept in the same metadata. It records what was
  asked rather than what the work became, so a card that has left the backlog refuses to change it (a typo
  may still be corrected while the card sits in the backlog), and a later change to `requirements` is written
  into the card's discussion with the reason it came from. A card written before this key existed simply has
  no request;
- `revision` - the optimistic revision;
- `ownPriority` and the computed effective priority (the task blends its own value with the
  maximum parent value using the project's priority weights);
- `declaredScopeFiles` (the intended files/globs) and `actualChangedFiles` (what was really
  changed). Files outside the declared scope are flagged as out-of-scope.
- `size` - the step of the project's size grid the card was given, for example `M`. The agent assigns
  it from the grid's descriptions, and the step's coefficient multiplies the card's score.
- `criterionValues` - the card's scores per criterion, when the project defines criteria. A card created
  without a size or scores is one the agent is expected to estimate: the card page's *Ask to estimate*
  action records which agent was asked, and that agent runs `/aiko-estimate <cardId>`.

A card's own score is the weighted average of its normalized criterion values, or its own priority when
the project defines no criteria; the size coefficient multiplies either. A task then blends that with the
highest parent value using the project's weights (ТЗ §10).

A project created from the default template starts with three criteria: `app-point` (how much the product
itself needs the work - correctness, architecture, the cost of not doing it; weight 0.35), `user-point` (how
much the people using the product do; 0.35) and `complete` (how ready the card is; 0.30). Each is scored on
a 0..10 range and carries its own instruction for the agent. `complete` is the one that moves: every stage
of the default pipelines, and `/aiko-run`, require the agent to re-score it after each change, so the board
always says what the card actually does now. A stage is not completed with a stale score either:
`aiko_complete_stage` refuses until the card was re-estimated during that run. A project that starts from a
template without criteria keeps the plain priority a person types and has nothing to estimate, so completing
a stage there needs no score.

The card page is laid out in two columns: on the left what the card itself says - its editable requirements
and declared scope, the declared and actual files and the acceptance criteria of the stage it sits in - above
one block of tabs: the completed outcomes, the code changes, the artifacts and the runs; on the right the
execution state with its assignees, the triage and score with the card's rank on the board (and the *Ask to
estimate* action below the criteria), the related cards (parents, children and plain relations - each links to
its own page), the progress of the stage's acceptance criteria, the stage's skills and the card's parameters
(title, priority, size and every scoring criterion of the project). What the stage asks the agent to do belongs
to the workflow, not to the card, so it is edited on the **Workflow** page rather than here.

A card can wait for another one: a `blocks` relation (the blocking card is the source) keeps the blocked card
from being started. `aiko_start_stage` refuses it and names the blocking card and its stage, and the agent
reports that to you instead of working a card whose turn has not come. The block lifts when the blocking card
reaches the last stage of its own pipeline - read from that card's workflow, so pipelines may differ - or when
the relation is removed.

## Workflows

A workflow is the card type: an ordered list of stages that defines both the pipeline a card runs through
and what that card is. Each stage has a title, the card types it accepts, the instruction the agent is
given, the skills it invokes **before** and **after** that instruction, the executors allowed to run it and
its default agent, the artifacts it must produce (each with a policy for when it is missing) and the commands
that verify its outcome. The workflow itself has a name, a description, an icon and a colour - that is how
the type is drawn in the pickers and on the board.

A card type is project data, not something Aiko knows about. The default template ships three workflows -
`epic`, `story` and `task` - but the engine names no type itself: every behaviour that depends on a type is
declared by that type. The one such behaviour today is **Blends with its parents' score**: a type that sets
it (the shipped `task` does) mixes its own score with the best of its parents', and a type that leaves it off
keeps its own score. Add a **Bug** type and it behaves exactly as you set it, with no code change.

Edit them in **Project settings**: the card types are the tabs at the top of the panel, and everything below
belongs to the tab that is open. The type's own fields - its id, its name, what it is for, and its icon and
colour - are written by **Save type**; clicking a stage opens it in a drawer where its column icon and colour
are chosen too. The icon comes from a
set of ten and the colour from Flare's palette; "Default" means there is no icon, and the column title keeps
the theme's own colour. The two are connected - the colour tints the icon and the title - so it is visible
together with a chosen icon. **+** opens a draft type as a tab of its own, which the same fields then fill
in: give it an id (for example `bug`), a name and a description ("a
card type for defects found in the field") and pick an icon and a colour - the type appears at once in
the create-card picker and as a board section, and its pipeline starts with `backlog`, "In progress" and
"Done". The id may be changed while the type has no cards; once cards exist it is fixed, because their folder
and their board section are named after it.

A project owns its own copy of the pipelines, so a change there never reaches another project - or a project
created later from the template. Removing a stage that still contains cards is rejected, removing a card type
that still contains cards is rejected too, and the `backlog` column is protected twice over: it is not removed
and it stays first, because a card enters its pipeline there - no other status can be placed before it.

## Agent bridges

The **Daemon & MCP bridges** screen shows the daemon itself (version, address, PID, round-trip, MCP endpoint)
and one card per agent: whether it is installed on this machine and whether it is attached to Aiko. The button
on a card attaches the bridge - it writes Aiko's global skills and commands for that agent - and detaches it
again. The two facts are independent: an agent can be installed without being attached, or found only partly
configured, in which case the button completes it. An agent that is not on `PATH` shows the
`aiko agent install` command instead of the button. The MCP entry for one project is added separately:
`aiko agent install --project <id>`.

## Settings

Two levels exist, and the narrower one wins:

- **Template** (`templates/default/template.json` next to the daemon's database) - what a new project is
  created from: the execution defaults, the scoring model (weights, criteria, size grid) and the pipelines
  with their stages. This is the level the **Workflow** screen edits, and editing it affects the
  projects created afterwards, never the ones that already exist.
- **Project** (`.aiko/settings.json`) - the values this project actually runs with, copied in at creation.
  The **Project settings** screen edits this one, and only this one.

There is no installation-wide level: what a project does not state it does not inherit from anything, so the
Project settings screen has no "reset to app defaults" to offer. What it offers instead is **Change
workflow** - point the project at another workflow set - and **Create template** - capture the project as a
set other projects can start from.

Currently configured: execution (workspace mode, max concurrent runs, and the policies for scope overlap,
scope widening, commits and pushes), and the priority model - blending weights, the criteria with their
ranges and agent instructions, and the size grid.

There is a third level that is not configuration at all: **Appearance** (`/appearance`, in the rail's
overview group) holds how the interface looks and reads for the person in front of it: the language, the
theme mode and the palette. Nothing there is written to a file: the choice goes to the
browser's local storage, so it follows the browser rather than the project, and neither a template nor a
project can set it.

**Language** is chosen from the ones the interface is translated into. On a first visit, with nothing
chosen, it follows the browser's ordered language list and falls back to English; a choice made on the
screen wins over both, and a stored language the interface no longer ships is ignored rather than honoured.
Switching it reloads the page: the language has to be settled before the first frame is drawn, which is what
keeps the interface from flashing in the wrong one.

English and Russian ship today, and English is the neutral resource set - the fallback every untranslated
language reads. Adding another is a `Loc.<culture>.resx` beside it, the culture in `UiLanguages.Supported`,
and the culture in the PWA project's `SatelliteResourceLanguages`, so that the satellite is actually built;
a spec fails when the three disagree.

**Mode** has three states: **Auto** (the default), Light and Dark. Auto follows the system's own light/dark
setting, and follows it as it changes - switching the system theme while the app is open repaints the
interface, because the preference is subscribed to rather than read once at startup. Light and Dark hold one
scheme whatever the system says. Like the language, the choice is kept in the browser and survives a reload;
unlike the language, switching the mode takes effect on the spot - nothing is reloaded.

**Palette** is the third axis: three colour schemes of the one theme, chosen from the list the theme ships.
**Kinetic Orchestration** is the one a first visit starts on - indigo and cyan on a navy shell; **Cyber Amber
Parchment** is the warm one, brown-amber accents on parchment in the light scheme and on warm charcoal in the
dark one; **Cyber Amber Operator** is the cool one, the same amber on grey-blue surfaces. Each palette carries
both schemes, so any of them can be read in Light or Dark. Like the mode, the choice is kept in the browser,
survives a reload and takes effect on the spot, and a saved palette is applied before the first frame - the
interface never flashes another one.

## Settings and pipelines from the terminal

The same two documents are reachable without opening the board:

```powershell
aiko settings get --project aiko      # the effective values, each with the source it came from
aiko settings get --template default  # the defaults a new project is created from
aiko settings set --project aiko --file settings.json
aiko workflow get --project aiko                       # the pipelines, with their revisions
aiko workflow get --project aiko --workflow task > task.json
aiko workflow set --project aiko --file task.json
```

A command names exactly one target, `--project` or `--template`, because there is no installation-level
settings document: a project is created from a template and owns its copy from then on, so editing a
template cannot reach a project that already exists. Writing goes through the running daemon, which
reprojects the board afterwards and refuses a pipeline that would leave a card in a stage it removed;
`workflow get` reads the project's own file and needs no daemon.

## Backups

`aiko backup` writes the same archive the `aiko_backup` tool writes: a zip of the project's `.aiko`
contents, by default under the data directory's `backups` folder with the project id and a timestamp in the
name. It reads the archive back and reports how many entries and bytes it holds, so "done" is something you
can see rather than something you are told.

`aiko restore` puts an archive back over the project. It overwrites files with the same names, so it asks
first (pass `--yes` in a script, where there is nobody to ask), and it rebuilds the board's projections
afterwards, because the restored files and the board are two different things until it does. An archive
holding an entry that would be written outside the project is refused before anything is unpacked.

```powershell
aiko backup --project aiko
aiko restore --archive "$env:LOCALAPPDATA\Aiko\backups\aiko-20260921T090000.zip" --project aiko
```

## Logs

`aiko logs` prints the tail of the daemon's own log - what it said on its way out, which is what "it stopped by
itself" is answered with - and `--project <id>` adds the tail of that project's event journal: what happened to
its cards and its stage runs.

```powershell
aiko logs                             # the daemon's log, last 50 lines
aiko logs --lines 200                 # more of it
aiko logs --project aiko              # and that project's journal
aiko logs --project aiko --after 2700 # from a known event onwards
```

The daemon log needs no daemon to read: it is a file, written when the daemon runs in the background. The
journal is read from the running daemon, and it is read oldest-first, so the command walks forward and keeps
the last entries; if the journal is longer than the walk covers, it says so and points at `--after`. An empty
journal, or a daemon that has never run in the background, is reported in words rather than as silence.

## Workflow sets

A project is created from a **template**, and the template decides what it starts with: the stages of each
pipeline with their agent instructions and required artifacts, the board projections, the starting memory
and the default settings. Choose it in the dashboard's *Add project* form, with
`aiko init <path> --template <id> --id <slug>`, or through `aiko_init_project` after listing the options with
`aiko_list_templates` (the same list is available as `GET /api/v1/templates`).

A template can also carry an **initialization instruction**: what an agent does right after a project has
been created from it, beyond Aiko copying the files - the structure the project should have, for example.
The field is on the Workflow screen, in the **Project initialization** panel at the top of the right
column. The instruction is copied into the new
project as `.aiko/initialization.md`, and `aiko_get_project_context` hands it to the agent, so
`/aiko-init <templateId>` ends with the project actually laid out rather than only registered. A project
created from a template without one simply has no such document.

The template is **copied** into the project, and the project owns its configuration from then on. Editing a
template - in the UI or by editing `templates/<id>/template.json` next to the daemon's database - affects
the projects created afterwards, never the ones that already exist. `.aiko/project.json` records
`templateId` and `templateVersion`, so a project can always say which template built it. The git policy is
part of the template: an init takes it from there unless the request names one.

The **Workflow** screen (`/templates`) lists them and owns everything else; a set's settings open by clicking
its name:

- **Capture from a project** - a new template out of an existing project: a snapshot of its settings, its
  pipelines, its projections, its memory and its git policy. It is how an installation pins a project it
  likes as the starting point of the next ones. The same action is on the **Project settings** screen as
  *Create template*.
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
  not silently fall out of its pipeline. The same action is on the **Project settings** screen as
  *Change workflow*.

Editing one template is the same defaults screen as before: `/templates/<id>` (and `/settings` for the base).

## Stage states and runs

Each card can have stage executions. An execution owns the workspace and keeps a history of
`AgentAttempt`s (one per agent run). Progress, scope changes, artifacts, commits and handoffs are
recorded. A rate-limited agent hands the execution to another agent without losing history.

A card moves through its pipeline one stage at a time, and a stage's state is derived from its runs - Aiko
stores runs, not a stage status, so there is one thing that can say where the work got to. The card page and
the board both show it:

- **pending** - the stage has no run yet: the card is sitting here and nothing was started;
- **running** - an agent reported that it is working here;
- **paused** - the run stopped: the agent paused it, failed or hit its rate limit;
- **waiting for a decision** - the agent asked the user something (`aiko_request_scope_expansion`) and is
  waiting for the answer;
- **completed** - the agent finished the stage, its artifacts are in place and the card was re-estimated.

A stage that is not completed holds the card: `aiko_move_card` refuses to advance it, and starting another
stage (`aiko_start_stage`) is refused as well. Running the card again continues the unfinished stage instead
of starting a new one - that is what "one run is one stage" means, and it is why `/aiko-run <cardId>` performs
exactly one stage and then stops. The board itself stays free: a person may drag a card to any column, and
`aiko doctor` reports the cards that were pushed past a stage that was not finished.

"Running" means the agent **said** it is running, not that its process is alive: Aiko does not start agents,
it only records what an agent reports. A stage is completed only when its required artifacts are in place and
the card was re-estimated; `/aiko-run <cardId> --all` is the one mode that walks the pipeline by itself -
descending into the children of a container, creating the ones a stage calls for and working each of them before
returning to the parent, with the parent's run parked so the tree keeps one run slot - and even it stops for a
question to the user, a failure or a rate limit, a forbidden policy or a missing required artifact.

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
with the card, in its own folder: `.aiko/workflows/<stories|tasks>/<cardId>/discussion.json`, next to `card.json`. The
**Discussion & commands** tab shows the feed (who, when, what) and a box for a new note; the `/benchmark` and
`/leak-check` chips add a command to the text. Nothing is launched from here: the daemon records work, you run
the agent - a command in the text is addressed to whoever opens the stage.

The feed is the notebook between stages, not a chat: an agent reads it before it works a stage and writes its
outcome there before completing it - including what the next stage or agent will need - and signs the note with
its own adapter id, so the feed says which agent wrote what.

## Analytics

The project page opens with the project's own **activity**: the same contribution calendar the dashboard
draws, over the days this project's runs and events happened on - work in another project never shows up in
it. Below it sit the two charts, side by side, computed from what the daemon observed rather than from what
you wrote: **pipeline velocity** (cards that entered a stage, per week - every week of the window, quiet
ones included) and the **distribution** (by card kind and by size step). FlareChart draws them, the category
labels sit beside the bars, and a value of zero draws no bar at all. The third block counts the project's
**cards per stage**, workflow by workflow. The daemon screen reports **uptime, runs, runs without a clean
stop, working set and managed heap**.

Those numbers live in SQLite on purpose: a stage transition is something the daemon observed, not authored
content. Losing the table costs a chart, not a card. Everything that matters - cards, settings, templates,
memory, discussion - is files, in the project and in Aiko's own directory.

## Commits

The project's commit policy controls who commits:

- `deny` (default) - you commit; the agent does not.
- `ask` - the agent reports a commit and waits for your approval (the execution becomes
  `waiting-for-user`; approve with `aiko_approve_commit` or in the UI).
- `allow` - the agent commits and reports the SHA.

The project's push policy answers the same question for pushing from the shared checkout, with the same three
answers. It is a rule the agent reads rather than one Aiko enforces: Aiko has no push of its own, so nothing
can pause an execution until you approve - the context the agent reads first states it, and the `/aiko-run`
procedure repeats it.

## Memory

Durable project memory lives in `.aiko/memory` as Markdown and is full-text searchable. Use
`aiko_search_memory` / `aiko_store_memory` (or `/aiko-memory`) for decisions, conventions and
lessons.

## Cross-project cards

An agent working in one project can report a card into another project. The created card
records its origin (`originProjectId`), and the card page shows it in the *Origin* block: the
source project, the card it was reported from when one was named, the adapter that filed it and
how long ago. The target project configures nothing here - it is told, not asked.

Which project that is comes from the **linked projects** registry, edited on the project's *Linked projects*
screen - the rail item beside *Project settings*: pick one of the projects registered with this installation,
write what it is for ("the
desktop client - UI work is filed here") and, when you can, where its reference lives and when work does and does
not belong there. Those last three are optional and they are the useful half: an agent holding a path to a
neighbour's documentation reads it, while an agent holding only a sentence goes looking in the packages this
project happens to have installed. The registry is `.aiko/links.json`; `aiko_list_links` returns it on its own
and the same entry is shown to an agent in `aiko_get_project_context`, and removing a link only stops future
routing - the cards already filed in the other project stay where they are.

A hand-over is this project's own decision, and the two settings that carry it sit on its settings page:

- **Writing to other projects** — whether an agent working here may file a card in a neighbouring project at
  all: *Deny* (the default) refuses and tells the agent to file it by hand, *Ask* confirms every hand-over with
  you (the agent asks, then repeats the call with `userConfirmed=true`, so each attempt is confirmed rather than
  one), and *Allow* creates the card without asking.
- **Projects this one may write to** — which of them may be written to: an empty list allows any registered
  project, and a non-empty one refuses a target that is not named there.

The project being written to configures nothing about it: it sees the mark of where the card came from, and what
to do with the card itself is its business.
