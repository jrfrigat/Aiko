# Aiko - Getting Started

Follow this path to go from a fresh install to a completed task in a few minutes.

## 1. Start the daemon and open the UI

```powershell
aiko serve     # in one terminal
aiko ui        # in another (opens the browser, pairs it with the daemon)
```

## 2. Register your project

In the UI, click **Add project**, enter the path to your project folder, and choose a git policy. The
project's name and its id are filled in from the folder - `C:\work\Aiko` becomes **Aiko**, id **aiko** -
so the project's address reads `/p/aiko/board`. Both fields stay editable before you press *Create*; an
id that another project already uses is refused. Aiko creates the `.aiko` directory and registers the
project.

You can also do it from the terminal:

```powershell
aiko init C:\path\to\your\project --name "My Project" --id my-project
```

`--name` and `--id` are optional: the name defaults to the folder name, and the id to a slug derived
from that name (Cyrillic is transliterated: *Мой проект* becomes `moj-proekt`). A derived id is made
unique automatically, a typed one is refused when it is taken.

## 3. Connect an agent

Install the global agent integration once (it adds the Aiko MCP entry, skills and memory to every
detected agent), then connect the agent to this project:

```powershell
aiko agent install --project <projectId>
```

Then restart the agent so it loads the project's MCP server.

## 4. Create a card

In the agent (inside the project):

```text
/aiko-init <templateId>   # only if the project is not registered yet; the template decides what it starts as
/aiko-create task Add a settings page      # name the type, then what you want
/aiko-create bug The dropdown list is empty
```

The types are the project's own: every workflow in **Project settings - Workflow** is a card type, so a
type you add there (an *Epic*, a *Bug*) is creatable at once - `/aiko-create` reads the project context
instead of naming types, and `/aiko-create-<type>` is generated for each of them.

Or use plain language: *"Create a task to add a settings page."* The same cards can be created in
the UI with the **Card** button on the board.

## 5. Work through the stages

The agent reads the project context first, then runs the card:

```text
/aiko-run TASK-1              # do what the card's current stage asks for
/aiko-run TASK-1 review       # move the card to "review" first, then run it
```

There is no per-stage command: `/aiko-run` reads the stage from the card and its instruction from the
project, so it works in a pipeline you edited or invented yourself. Drag cards between columns on the
board for the same effect. When work needs files outside the card's scope, the agent calls
`aiko_request_scope_expansion` and waits for your approval.

## 6. Finish and store knowledge

`/aiko-run` completes the stage it started, recording the actual changed files and the artifacts, so the
knowledge step is the one left:

```text
/aiko-memory       # store durable decisions and lessons
```

Commits follow the project's commit policy (`deny` by default - you commit; `ask` - the agent asks
first; `allow` - the agent commits). The UI shows the board, the project page (statistics, velocity,
distribution), a card (scope, acceptance criteria, the diff from git, discussion, runs) and the
workflow sets - all updated live over SSE.

## What next

- [Installation](installation.md)
- [User guide](user-guide.md)
- [Agent integration](agent-integration.md)
- [Troubleshooting](troubleshooting.md)
