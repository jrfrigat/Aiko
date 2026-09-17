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

When the install finishes, the installer asks which agents to connect and then writes the global MCP
entry, the `/aiko-*` skills and the shared memory into them (`aiko agent install --scope user` under
the hood). Press Enter to skip; nothing is written into an agent that you did not name.

### Installer options

| Option | Effect |
| :-- | :-- |
| `-Version <tag>` | Install a specific release, for example `v0.1.0`. Defaults to the latest release. |
| `-InstallDir <path>` | Unpack somewhere else. Defaults to `%LOCALAPPDATA%\Aiko\bin`. |
| `-Agents <ids>` | Connect these agents globally without asking, for example `claude-code,codex`. |
| `-NoAgentSetup` | Never ask about agents (same as `aiko agent install --scope user` later). |
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

Stop it again from any terminal:

```powershell
aiko serve stop
```

It asks the daemon to stop - the port it saved when it started, or one named with `--port <p>`, which is
how a daemon started by hand is reached. The stop is refused without the local access token, like every
other API call, and the command waits until the port really stops answering before it reports success.

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

Either in the UI (the "Add project" button on the dashboard) or from the terminal:

```powershell
aiko init C:\path\to\your\project --name "My Project" --id my-project --git-policy local-only
```

`--name` and `--id` are optional. The name defaults to the folder name; the id is the short handle the
UI's URLs use (`/p/my-project/board`) and defaults to a slug derived from the name, transliterated to
latin when the name is Cyrillic. An id you type is used as-is or refused when another project already
holds it; a derived one is made unique with a numeric suffix instead.

In the UI the form also has a **Browse** button: it opens the daemon's directory listing, so the path is
picked instead of typed. The listing marks folders that already contain `.aiko`, and the field stays
editable for anyone who prefers to paste a path. Choosing or typing a path fills the name and the id in
at the same time, and both remain editable.

This creates the `.aiko` directory (workflows, projections, memory) and registers the project.
The default git policy is `local-only` (the whole `.aiko` directory is added to `.gitignore`);
`track-project-knowledge` keeps workflows and memory under version control.

## Unregister a project

A registration can be dropped without touching anything on disk - a mistyped path, a project that
moved, or a throwaway used for a test:

```powershell
aiko project remove <projectId> --yes
```

Only the registration and its SQLite projections go. The `.aiko` directory, the workflows and the
memory stay where they are, and `aiko init` on the same path registers the project again. In the UI
this is the bin button on a project row on the dashboard; over REST it is
`DELETE /api/v1/projects/<projectId>` (204 on success, 404 when nothing was registered).

Deleting the `.aiko` directory as well is deliberately **not** implemented: unregistering exists to undo
a mistake, and a command that also removes files cannot be the default. Remove the directory yourself
when that is what you want.

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
- `aiko doctor` - installation report: database, token, port, projects and agent configurations that
  point at an old endpoint. It changes nothing; `aiko repair --fix` applies the fixes it names.
- `GET http://127.0.0.1:<port>/health` - `healthy`.
- `aiko ui` - opens the board.

If `irm` is blocked by the execution policy, run the installer explicitly:

```powershell
powershell -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/jrfrigat/Aiko/main/scripts/install.ps1 | iex"
```
