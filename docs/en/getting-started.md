# Aiko - Getting Started

Follow this path to go from a fresh install to a completed task in a few minutes.

## 1. Start the daemon and open the UI

```powershell
aiko serve     # in one terminal
aiko ui        # in another (opens the browser, pairs it with the daemon)
```

## 2. Register your project

In the UI, click **Add project**, enter the path to your project folder and a name, and choose a
git policy. Aiko creates the `.aiko` directory and registers the project.

You can also do it from the terminal:

```powershell
aiko init C:\path\to\your\project --name "My Project"
```

## 3. Connect an agent

Install the global agent integration once (it adds the Aiko MCP entry, skills and memory to every
detected agent), then connect the agent to this project:

```powershell
aiko agent install --project <projectId>
```

Then restart the agent so it loads the project's MCP server.

## 4. Create a story and a task

In the agent (inside the project):

```text
/aiko-init                # only if the project is not registered yet
/aiko-story-create        # create a story
/aiko-task-create         # create a task
```

Or use plain language: *"Create a task to add a settings page."* The same cards can be created in
the UI with the **Card** button on the board.

## 5. Work through the stages

The agent reads the project context first, then works stage by stage:

```text
/aiko-next-stage   # move the card to the next stage
/aiko-analyze      # analysis
/aiko-implement    # implementation
/aiko-review       # review
/aiko-complete     # done
```

Drag cards between columns on the board for the same effect. When work needs files outside the
card's scope, the agent calls `aiko_request_scope_expansion` and waits for your approval.

## 6. Finish and store knowledge

```text
/aiko-complete     # record the actual changed files and artifacts
/aiko-memory       # store durable decisions and lessons
```

Commits follow the project's commit policy (`deny` by default - you commit; `ask` - the agent asks
first; `allow` - the agent commits). The UI shows cards, artifacts, workflow and settings, all
updated live over SSE.

## What next

- [Installation](installation.md)
- [User guide](user-guide.md)
- [Agent integration](agent-integration.md)
- [Troubleshooting](troubleshooting.md)
