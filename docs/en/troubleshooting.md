# Aiko - Troubleshooting

## The port is busy

Aiko prefers port `24560`. If it is busy, `aiko serve` asks for another port (or picks a free one
from `18000-18999` in non-interactive mode) and saves it. If the port changed, the agent MCP
configs may be stale - reinstall them:

```powershell
aiko agent install --project <projectId>
```

## 401 Unauthorized

The daemon requires the access token (or the pairing session cookie) for `/api/v1/*` and `/mcp/*`.
If you hit 401:

- Open the UI with `aiko ui` (it pairs the browser).
- For scripts/agents, send `Authorization: Bearer <token>`; get the token with `aiko token show`.

`AIKO_INSECURE=1` disables authentication for local debugging only.

## 403 / 400 on browser requests

The daemon rejects non-loopback hosts and remote browser origins. Keep everything on
`http://127.0.0.1:<port>`.

## The agent does not see Aiko

1. Run `aiko agent list` to confirm the agent is detected.
2. Run `aiko agent install --project <projectId>` (and `aiko agent install --scope user`).
3. Restart the agent so it loads the new MCP server.
4. Check the endpoint: `GET http://127.0.0.1:<port>/health` should return `healthy`.

## 409 Conflict

Cards and workflows use optimistic revisions. If two clients edit the same item, one gets a
`409` with the expected and actual revisions - reload and retry.

## Stale board or projections

The board reads from SQLite projections built from `.aiko`. If you edited files by hand, rebuild:

```powershell
aiko reindex <projectId>
```

Deleting the database is safe - `aiko init` (or `POST /api/v1/projects/initialize`) and
`aiko reindex` rebuild everything from `.aiko`.

## The agent skipped aiko_get_project_context

Aiko cannot force an agent to call a tool. The installed skills, `AGENTS.md` block and Cursor rule
reinforce the "read context first" contract; mention it explicitly in the prompt if needed.
