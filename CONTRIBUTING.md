# Contributing to Aiko

Thanks for taking the time to work on Aiko. This file covers the loop from a fresh clone to a pull
request.

## Prerequisites

- Windows 10/11 x64 (the shipped product targets Windows).
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). `global.json` pins the `10.0.4xx`
  feature band, and the solution is a `.slnx` file, so an older SDK will not build it.

## Build, run, test

```powershell
dotnet build Aiko.slnx -c Release
dotnet test  Aiko.slnx -c Release --no-build
dotnet run --project src/Aiko.Server            # the daemon, serving the PWA and the MCP endpoints
```

Four xUnit suites run together: `Aiko.Domain.Specs` (pure rules), `Aiko.Infrastructure.Specs`
(files, SQLite, agent adapters), `Aiko.Mcp.Specs` - which boots its own daemon on a random loopback
port with a throwaway database - and `Aiko.Pwa.Specs`, which pins the client's JSON contract. No
manual setup is needed before `dotnet test`.

To exercise the installed experience without touching your machine-wide setup:

```powershell
.\install.ps1                     # publishes the CLI, daemon and stdio proxy into %LOCALAPPDATA%\Aiko\bin
aiko status
```

Project data lives in `.aiko` directories and in `%LOCALAPPDATA%\Aiko\aiko.db`. The `.aiko` files are the
source of truth and must not be deleted. The database's search indexes are rebuildable from them
(`aiko reindex <projectId>`), but the project registry, the run history and the event journal live only in
the database.

## Code conventions

- `Directory.Build.props` sets `TreatWarningsAsErrors`, `Nullable` and documentation generation for
  every project: a warning is a build failure, so keep the tree clean.
- Keep the layering: `Aiko.Domain` has no I/O and no agent knowledge, `Aiko.Application` defines use
  cases and ports, `Aiko.Infrastructure` implements them, `Aiko.Server` hosts them.
- Comments explain *why* a decision was made, not what a line does. File-level and type-level
  summaries are expected; `CS1591` is suppressed for positional record members.
- The PWA talks JSON through the source-generated `PwaJsonContext` only: the published client is
  trimmed, where reflection-based serialization fails at runtime. Register every new type the UI
  exchanges, pass `PwaJson.Options` to every `HttpClient` JSON call, and keep client DTOs top-level
  and `internal` (a type nested in a component cannot be reached by the generator).
  `Aiko.Pwa.Specs` fails when a type is missing from the context.
- Documentation is maintained in both languages (`README.md` + `README.ru.md`,
  `docs/en/*` + `docs/ru/*`). A behavior change updates both.

## Installer scripts

`scripts/install.ps1` is fetched over the network and piped straight into a shell, so CI parses it first
(and the contributor `install.ps1` with it) and then runs PSScriptAnalyzer on both at Error, Warning and
ParseError severity. The parse step is not redundant: a syntax error is reported as `ParseError`, which is
a severity of its own, so a filter of `Error, Warning` alone lets a script that cannot run once pass the
gate. Keep both scripts free of analyzer findings and keep the `owner/repo` constant, the release asset name
(`aiko-<version>-win-x64.zip`) and the layout contract in sync with
`.github/workflows/release.yml`:

```text
aiko.exe                the CLI
aiko-stdio.exe          the stdio <-> Streamable HTTP MCP proxy
server/Aiko.Server.exe  the daemon, with the PWA in server/wwwroot
templates/default/template.json
                        the base project template the installer seeds the data directory with
```

## What to check before opening a pull request

1. `dotnet build Aiko.slnx -c Release` and `dotnet test Aiko.slnx -c Release --no-build` are green.
2. New behavior has a spec in the matching suite; the MCP tools, the REST endpoints and the UI stay
   consistent for the same operation.
3. Numbers quoted in documentation (test count, tool count, port, directory names) match the code.
4. Both language variants of any documentation you touched are updated.

## Repository setup (one-time, web UI)

GitHub settings that live outside the repository and cannot be set from a file:

- Description: *Local-first AI development orchestrator - Kanban board, MCP endpoints and durable
  memory for your coding agents.*
- Topics: `ai-agents`, `mcp`, `kanban`, `blazor`, `dotnet`, `local-first`, `claude-code`, `codex`.
- Social preview: upload `assets/social-preview.png`, regenerated from its SVG source with
  `assets/render-social-preview.ps1` when the design changes.
- Releases must stay public: `scripts/install.ps1` downloads the assets without a token.

## Releases

Releases are cut from tags. Pushing `v0.1.0` runs the `Release` workflow, which builds the
self-contained win-x64 archive, attaches it to a draft GitHub Release and publishes the release only
once the archive is on it. `latest release` must always be installable, which is why the workflow
never publishes a release without its assets. The same workflow can be started manually from the
Actions tab with a tag as input.
