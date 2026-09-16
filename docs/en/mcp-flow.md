# Local AI Orchestrator and AI-Driven Development Kanban Pipeline

> [Русская версия ->](../ru/mcp-flow.md) - [Technical specification](technical-specification.md) - [README](../../README.md)

(A project analogous to Ruflo; the working name was "StitchFlow". The final name was pending
approval. This is the original historical terms of reference kept for reference.)

---

## 1. Architecture and Network Contract

* System type: a client-server local development automation environment with closed execution
  loops and end-to-end artifact traceability.
* Frontend: a Progressive Web Application (PWA) on Blazor WebAssembly, built on top of your own
  Flare.Blazor library (FlareKanban components and built-in drag-and-drop).
* Hosting: GitHub Pages (a fully static web server, execution isolated inside the browser). [1]
* Backend: a cross-platform console service on .NET 10.0 (C#) with Native AOT support for an
  instant "cold" start (up to 50 ms) and minimal memory footprint (15-30 MB). It acts as a hybrid
  coordinator:
* Role 1 (MCP server): a stdio bridge for local execution of Claude Code commands and the Claude
  Desktop environment.
  * Role 2 (Local Web API): a local web server handling HTTP/SSE requests with a configured
    end-to-end CORS policy for integration with the browser PWA, Cursor, ChatGPT Desktop and
    Codex.
* Network contract and dynamic port:
* The port is chosen by the installer: the user can specify it manually or accept an automatically
  found free port. The choice is saved globally and reused by the daemon.
  * PWA configuration: the Flare.Blazor PWA settings window provides an interactive input for
    overriding the local server's base address (URL/port). The value is stored in the browser
    (LocalStorage) and substituted on the fly into all HttpClient instances. A port availability
    check button ("Ping") is provided.
* Data storage: a textual JSON database (backlog.json, archive.json, config.json) and description
  files in Markdown. The whole database is deployed locally into the `.flow/` directory inside the
  target project root, which enables versioning of the working environment through Git without
  deploying external databases.

---

## 2. Data Structure and the Many-to-Many Relational Model

The system abstracts the working backlog into three independent levels connected by relational
logic:

   1. StoryCard (user story):
   * A large functional requirement (epic). Described as a Markdown file in the `.flow/issues/`
     directory.
      * Contains a unique ASCII identifier, business goals in Russian, technical constraints and an
        array of custom value scores. A story is static and does not move across board columns.
   2. TaskCard (atomic task):
   * The minimal quantum of technical execution for AI agents. Carries a triage string (format:
     `triage: type=refactor status=open readiness=0 size=M`).
   3. The Many-to-Many relation model (shared tasks):
   * Merge criterion: one atomic TaskCard can be attached to two or more stories at the same time
     (a ParentStoryIds array). This prevents duplication during code generation.
      * Merging: in the FlareKanban interface, dropping one card onto another (or via the
        merge_tasks MCP tool) merges the tasks into one. The merged task's priority is recomputed
        automatically from the parent stories' weights using the selected strategy (by default,
        taking the maximum value score).

---

## 3. The Meta-System of Dynamic Columns, Skills and Artifacts

The development pipeline is a guided state machine where every column's configuration is fully
customized by the user through the PWA:

* Dynamic status (column): a configuration object defining the column title, the board order and
  the allowed-agent flag (AllowedTargetAgents: User, Claude, Codex).
* Linked AI skills: moving a card into a specific column automatically activates or makes
  available to AI clients the corresponding MCP tool (backlog analysis, architecture analysis and
  design, API documentation generation, commit composition and task closing processes adapted and
  moved to universal rails).
* Input/output artifacts (Definition of Done): every column contains an array of required files
  (for example, docs/architecture.md for the analysis stage or docs/api.md for the development
  stage). The backend reads global templates from the configuration, and the AI generates/modifies
  them, storing them locally in the isolated task directory
  `.flow/workflow/tasks/<TASK-ID>/`.

---

## 4. Integrated Work History and Artifact Audit

TaskCard and StoryCard entities embed dynamic blocks for transparency and AI context memory:

* ExecutionHistory (work journal): a timeline of events for the task. Every entry records: the
  exact timestamp, the performer's name (Claude-Code, Codex, User), the atomic action performed,
  the operation status (Success, Failed) and the Git commit hash (when present).
* Artifacts (linked artifacts): an interactive table of files produced or changed within the card
  (relative path, artifact type, creating agent). When an artifact is clicked in the PWA
  interface, the frontend requests the file body from the local web API backend and renders its
  text/Markdown content in the built-in viewer.

---

## 5. The Configuration Control Panel in the PWA

The Flare.Blazor frontend contains an isolated administrative screen for transparent manipulation
of the flow-config.json file:

1. **Priority formula constructor:** a set of graphical sliders for adjusting the backlog weight
   factors:

   $$ \text{Priority} = (W_{\text{user}} \cdot \text{UserValue} + W_{\text{flare}} \cdot \text{LibraryDebt} + W_{\text{readiness}} \cdot \frac{\text{Readiness}}{10}) \cdot \text{SizeFactor} $$

   *Size factors (default):* `XS` (1.15), `S` (1.08), `M` (1.00), `L` (0.90), `XL` (0.80).
2. **Custom scoring criteria editor:** an interface for dynamically extending the base scoring
   model. The user can add a new criterion, set its scale (for example, 0 to 5) and write a
   detailed instruction text for the AI (`aiInstruction`). This instruction is dynamically
   embedded into the system prompts of the decomposition and scoring MCP tools, obliging the AI to
   return the new fields in the JSON structure.
3. **Pipeline constructor (column management):** a tool for dragging, adding and renaming columns
   and binding custom instructions, executable commands and required artifacts to them.

---

## 6. Execution Modes (Execution Loop) and Validation

* Reactive mode (manual CLI): moving a card in the interface triggers an update of backlog.json.
  The developer talks to an AI agent in the project terminal. The AI reads the context through
  the MCP tool, sees the tasks in progress and operates only within the declared file array
  (ScopeFiles).
* Active mode (background daemon): a .NET background process (IHostedService) built into the
  backend uses FileSystemWatcher to continuously monitor task files. Finding a card in the
  execution status, the daemon covertly initializes the local AI client process (Process.Start).
  The current status of the AI's text log is continuously streamed to the PWA over Server-Sent
  Events (SSE) to show progress in real time.
* Readiness criteria (DoD) and validation: moving a card to the next column is blocked by the
  backend when validation rules are not met:
* File check: control over the physical presence of the pipeline's required Markdown artifacts.
  * System command: execution of the verification command specified in the column's config (for
    example, dotnet test). If the command fails, the backend returns the card to the AI for
    rework, writing the error log into ExecutionHistory.
  * The Archival Rule (Rule #1): the repository and the Git index remain untouched by the server
    (all commits happen only by agreement). Closed tasks move from backlog.json to archive.json,
    and conclusions (durable memory) are aggregated into the project's global long-term memory
    file.

---

If this specification fully matches your vision, let's move on to implementing the backend. We
need to agree on the first practical step. What do we build first:

* The C# structure of data models and configuration classes (FlowConfig.cs), capable of dynamically
  parsing custom criteria, weights and pipeline columns?
* The skeleton of the backend server's Program.cs initializing the Web API on the port from the
  installer's global setting, CORS for the static PWA and the background stdio listener thread for
  MCP clients?
