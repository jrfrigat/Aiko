# Aiko - Installation

Aiko is a local-first orchestrator for AI-assisted development: one loopback daemon, a Kanban
PWA, durable project memory and a stable MCP contract for Claude Code, Codex, Cursor and ZCode.

This guide covers installing and running Aiko on Windows.

## Requirements

- Windows 10/11.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build and run from source
  (the published layout is framework-dependent, so the .NET 10 runtime is required at run time).

## Install

From the repository root, run the installer in PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

The installer publishes Aiko (`aiko` CLI, the daemon, the stdio proxy) to
`%LOCALAPPDATA%\Aiko\bin` and adds that directory to the user `PATH`. Open a new terminal so the
`aiko` command is picked up.

Install the global agent skills (optional but recommended) so `/aiko-init` works everywhere:

```powershell
aiko agent install --scope user
```

## Run

Start the daemon (it listens on loopback only):

```powershell
aiko serve
```

Open the UI - this pairs the browser with the daemon using a one-time code and opens the board:

```powershell
aiko ui
```

`aiko status` shows the data directory, database path, port and daemon health.

## Configuration

| Environment variable | Purpose |
| :-- | :-- |
| `AIKO_PORT` | Explicit port for the next start; validated and persisted |
| `AIKO_URL` | Explicit loopback origin (overrides port selection) |
| `AIKO_DATABASE` | Path to the SQLite database (default `%LOCALAPPDATA%\Aiko\aiko.db`) |
| `AIKO_TOKEN` | Fixed access token (otherwise generated and persisted in `access-token`) |
| `AIKO_PAIR_CODE` | Fixed pairing code (used by tests/scripts) |
| `AIKO_INSECURE` | Set to `1` to disable authentication (local debugging only) |

The daemon prefers port `24560`; if it is busy it asks for another port (or picks a free one from
`18000-18999` in non-interactive mode) and remembers the choice in `settings.json` next to the
database.

## Register a project

Either in the UI (the "Add project" button on an empty board) or from the terminal:

```powershell
aiko init C:\path\to\your\project --name "My Project" --git-policy local-only
```

This creates the `.aiko` directory (workflows, projections, memory) and registers the project.
The default git policy is `local-only` (the whole `.aiko` directory is added to `.gitignore`);
`track-project-knowledge` keeps workflows and memory under version control.

## Connect an agent

```powershell
aiko agent install --project <projectId>
```

This writes the project-scoped MCP configuration and the `/aiko-*` skills for the detected agents.
`aiko agent list` shows which agents were found. Restart the agent so it loads the new MCP server.

## Update and uninstall

- Update: re-run `install.ps1` (it overwrites the binaries; project data and settings are kept).
- Uninstall: delete `%LOCALAPPDATA%\Aiko\bin` and remove it from the user `PATH`. Project `.aiko`
  directories and the database are never deleted automatically.

## Verify

- `aiko status` - daemon health, port, data directory.
- `GET http://127.0.0.1:<port>/health` - `healthy`.
- `aiko ui` - opens the board.
