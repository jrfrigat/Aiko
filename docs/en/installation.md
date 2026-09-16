# Aiko - Installation

Aiko is a local-first orchestrator for AI-assisted development: one loopback daemon, a Kanban
PWA, durable project memory and a stable MCP contract for Claude Code, Codex, Cursor and ZCode.

This guide covers installing and running Aiko on Windows.

## Requirements

- Windows 10/11 x64 (the release ships a self-contained win-x64 build).
- Nothing else for a release install - no .NET SDK and no .NET runtime.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) only when you build and run from
  source. A source checkout publishes framework-dependent, so it needs the .NET 10 runtime at run
  time.

## Install

One line in PowerShell:

```powershell
irm https://raw.githubusercontent.com/jrfrigat/Aiko/main/scripts/install.ps1 | iex
```

The installer:

1. resolves the newest GitHub release (or the tag you pass),
2. downloads `aiko-<version>-win-x64.zip` from it,
3. unpacks it into `%LOCALAPPDATA%\Aiko\bin`,
4. adds that directory to the user `PATH`.

Nothing is installed machine-wide and no administrator rights are needed. Open a new terminal so the
`aiko` command is picked up.

The installed layout:

```text
%LOCALAPPDATA%\Aiko\bin\aiko.exe                 CLI
%LOCALAPPDATA%\Aiko\bin\aiko-stdio.exe           stdio <-> Streamable HTTP MCP proxy
%LOCALAPPDATA%\Aiko\bin\server\Aiko.Server.exe   daemon, with the PWA in server\wwwroot
```

### Installer options

| Option | Effect |
| :-- | :-- |
| `-Version <tag>` | Install a specific release, for example `v0.1.0`. Defaults to the latest release. |
| `-InstallDir <path>` | Unpack somewhere else. Defaults to `%LOCALAPPDATA%\Aiko\bin`. |
| `-NoPathUpdate` | Leave the user `PATH` untouched. |

Options need the scriptblock form, because `irm ... | iex` cannot take parameters:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/jrfrigat/Aiko/main/scripts/install.ps1))) `
    -Version v0.1.0 -InstallDir D:\Tools\Aiko
```

### Install from a source checkout

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

This publishes the working tree framework-dependent (`aiko` CLI, daemon, stdio proxy) into the same
`%LOCALAPPDATA%\Aiko\bin` directory, and the .NET 10 runtime is required at run time.

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

- Update: re-run the installer (release or source) - it replaces the binaries and keeps project data
  and settings. Pin a release with `-Version` when you do not want the newest one.
- Uninstall: delete `%LOCALAPPDATA%\Aiko\bin` and remove it from the user `PATH`. Project `.aiko`
  directories and the database are never deleted automatically.

## Verify

- `aiko status` - daemon health, port, data directory.
- `GET http://127.0.0.1:<port>/health` - `healthy`.
- `aiko ui` - opens the board.

If `irm` is blocked by the execution policy, run the installer explicitly:

```powershell
powershell -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/jrfrigat/Aiko/main/scripts/install.ps1 | iex"
```
