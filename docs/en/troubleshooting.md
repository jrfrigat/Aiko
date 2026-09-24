# Aiko - Troubleshooting

## The installer fails

`irm https://raw.githubusercontent.com/jrfrigat/Aiko/main/scripts/install.ps1 | iex` needs:

- 64-bit Windows - the release ships win-x64 only;
- outbound HTTPS to `github.com` and `objects.githubusercontent.com`;
- a published release to download. If no release exists yet, the script says so; a specific tag can be
  pinned with `-Version v0.1.0`, and a wrong tag or asset is reported with the exact failing URL.

If the execution policy blocks `irm | iex`, run it explicitly:

```powershell
powershell -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/jrfrigat/Aiko/main/scripts/install.ps1 | iex"
```

`aiko` is not found after installing: open a new terminal (or check that
`%LOCALAPPDATA%\Aiko\bin` is on the user `PATH`).

## Upgrading from an earlier Aiko

The agent layout changed: the working contract now travels in the rule channel (`CLAUDE.md`, `AGENTS.md`,
`.clinerules/aiko.md`, `.cursor/rules/aiko.mdc`), and the skills carry procedures only - one per workflow
step. An earlier Aiko put the contract itself in a skill, so a project connected before this release still
has `.claude/skills/aiko/SKILL.md`, `.cline/skills/aiko-project/SKILL.md` and their siblings.

Re-run the connection once and the stale files are swept:

```powershell
aiko repair --fix
```

`repair --fix` reindexes and re-applies the whole project configuration for every agent the project is
already connected to, so the old skills are removed, the new ones are written and `CLAUDE.md` appears. It
refreshes a user-scope connection only where one exists, and it never connects an agent that is merely
installed: `aiko agent install --project <id>` does that for one project. A file without Aiko's ownership marker is never touched.

## The port is busy

If a daemon you left running holds it, stop that one first instead of hunting for the process:

```powershell
aiko serve stop
```

On the first start Aiko takes port `24560`, or a free one from `18000-18999` when that is busy, and saves
the choice in `settings.json`. From then on it keeps the saved port and refuses to start when something
else holds it. To move the daemon to another port, start it once with that port asked for explicitly - the
start saves it - and then point the agents at it:

```powershell
$env:AIKO_PORT = 18123   # any free port
aiko serve -d
Remove-Item Env:AIKO_PORT
aiko repair --fix         # rewrites the agents' MCP configurations for the new port
```

## The daemon stopped by itself

Aiko never stops its own daemon. There are exactly two ways out: `aiko serve stop` asks it to stop through
its own token-guarded endpoint, and the process being killed from outside. Which one happened is in its log:

```powershell
aiko status      # prints how to start one, plus the tail of the last background log
```

- A log that ends with `Application is shutting down...` means the daemon was *asked* to stop - by
  `aiko serve stop`, or by Ctrl+C in the terminal that started it.
- A log that ends mid-request, with no such line, means it was *killed*: Task Manager, `taskkill`, a logoff,
  or the console it shared being closed.

That second case is why backgrounds exist: a foreground daemon shares the terminal that started it, so
whatever ends that terminal - a closed window, a Ctrl+C pressed to copy text - ends the daemon too. Started
with `aiko serve -d`, it gets its own console and a log file (`daemon.log` next to the database, or the path
in `AIKO_LOG_FILE`) and outlives the shell.

The log does not grow without limit and does not shrink without saying so: past `maxFileBytes` it is rotated
to `daemon.log.1`, and `maxFiles` bounds how many files are kept - the file replacing a dropped one names it.
The event journal obeys `journalMaxAgeDays` the same way: rows past that age are removed once per daemon
start, leaving a `journal-trimmed` marker that states what went. All three bounds are the installation's own
settings in `settings.json` beside the database, and one left out falls back to the shipped value.

## 401 Unauthorized

The daemon requires the access token (or the pairing session cookie) for `/api/v1/*` and `/mcp/*`.
If you hit 401:

- Open the UI with `aiko ui` (it pairs the browser).
- For scripts/agents, send `Authorization: Bearer <token>`; get the token with `aiko token show`.

A daemon started with `AIKO_TOKEN` uses that value and does not write a token file; `aiko doctor` reports the
token as set by the variable rather than missing.

`AIKO_INSECURE=1` disables authentication for local debugging only.

## 403 / 400 on browser requests

The daemon rejects non-loopback hosts and remote browser origins. Keep everything on
`http://127.0.0.1:<port>`.

## The agent does not see Aiko

1. Run `aiko agent list` to confirm the agent is detected.
2. Run `aiko agent install --project <projectId>` (and `aiko agent install --scope user`).
3. Restart the agent so it loads the new MCP server.
4. Check the endpoint: `GET http://127.0.0.1:<port>/health` should return `healthy`.
5. If the agent still gets `401`, run `aiko doctor` and then `aiko repair --fix`: a configuration written
   before the access token existed carries no credential, so the daemon refuses it.

## The agent stopped seeing Aiko after the port changed

An agent's MCP configuration records the endpoint it connects to, so when the daemon moves to another
port those files keep pointing at the old one - the JSON stays valid and nothing else notices:

```powershell
aiko doctor          # reports which files are stale, and the fix for each finding
aiko repair --fix    # reindexes the projects and rewrites the stale configurations
```

`aiko doctor` changes nothing; `aiko repair` without `--fix` only reports. The same report is available
to an agent through the `aiko_doctor` MCP tool, and the drift it names is surfaced in two more places
without a second check behind them: `aiko status` prints the stale agent configurations after the
daemon's own state, and the *Daemon and MCP bridges* screen of the UI shows the same findings beside
the bridges.

## 409 Conflict

Cards and workflows use optimistic revisions. If two clients edit the same item, one gets a
`409` with the expected and actual revisions - reload and retry.

## Stale board or projections

The board reads from SQLite projections built from `.aiko`. The daemon rebuilds them when it starts, and a
save refused with `409` after the card's file changed (a git pull, a checkout) brings that card up to date,
so retrying the save works. If you edited files by hand while the daemon was running, restart it or rebuild:

```powershell
aiko reindex <projectId>
```

Deleting the database is safe - `aiko init` (or `POST /api/v1/projects/initialize`) and
`aiko reindex` rebuild everything from `.aiko`.

## A project is registered with the wrong path

Unregister it - the files on disk are kept:

```powershell
aiko project remove <projectId> --yes
```

The same button (a bin) is on the project's row on the dashboard. Nothing needs to be edited in SQLite,
and re-registering the right path is just `aiko init`.

## The agent skipped aiko_get_project_context

Aiko cannot force an agent to call a tool. The installed skills, `AGENTS.md` block and Cursor rule
reinforce the "read context first" contract; mention it explicitly in the prompt if needed.

## A card moved without its stage

`aiko doctor` reports it as a warning: *"N card(s) left the backlog without a single run"*, with the card
ids. It means the card sits past the backlog while nothing was ever worked through a stage - no execution, no
artifacts, no history - which is what an agent leaves behind when it edits files while the card is still in
its `backlog` stage, or when someone drags a card forward by hand.

What to do: open the card, start the stage it should have been worked in (`aiko_start_stage`, or run the card
with `/aiko-run`), and do the work under that execution; the history of a stage that was really worked is
what makes the card readable afterwards. The rule that prevents a repeat is the one agents read before they
touch files: work belongs to a stage execution, and `aiko_move_card` refuses to advance a card whose stage
was never run.

`aiko doctor` reports a second, quieter version of the same problem as *"N card(s) moved on from a stage
that was not finished"*. Here the card has runs, so it looks worked, but the stage it came from was only
started - or never started - and the card moved past it anyway. Each entry names the card, the stage it left
and how that stage stopped: `still running`, `paused`, `waiting for a user decision`, `stopped and needs
attention`, `cancelled` or `not started`. What to do: go back to that stage, continue it with
`aiko_start_stage` or finish it with `aiko_complete_stage`; a card moves on because its stage is completed,
not because the card was moved on.
