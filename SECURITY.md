# Security Policy

## Supported versions

Aiko is pre-1.0; only the latest released version receives security fixes.

| Version | Supported |
| ------- | --------- |
| latest  | yes       |
| older   | no        |

## Security model

Aiko runs on your machine and is built to stay there. What that means in practice:

- The daemon binds to **loopback only**. Any request whose `Host` header is not loopback is rejected
  with `400`.
- Browser origins are checked: only the daemon's own page (its scheme and port on loopback) gets through;
  any other origin, including another program's page on another local port, gets `403`, as does a request
  the browser marks `same-site` or `cross-site`. No page may frame the board.
- The browser's session cookie opens the board's REST API only: MCP takes the token in
  `Authorization: Bearer` alone, and a write the cookie authenticates must carry `X-Aiko-Request`.
- Clients authenticate with an **access token**; a browser is paired once with a one-time code
  (`aiko ui`). `AIKO_INSECURE=1` disables authentication and is for local debugging only.
- The MCP endpoint requires the same token, so `aiko agent install` writes it into the agent's MCP
  configuration: Claude Code, Cursor and ZCode get an `Authorization: Bearer …` header, and Codex gets the
  name of the `AIKO_TOKEN` environment variable it reads instead. **Those files are secrets.** Project
  initialization adds them to `.gitignore`; never commit them, and rotate the token
  (`%LOCALAPPDATA%\Aiko\access-token`) if one was committed.
- Project data lives in `.aiko` inside your project; the daemon's own state lives in
  `%LOCALAPPDATA%\Aiko`. Nothing is uploaded anywhere, and unregistering a project never deletes files
  on disk.
- The directory listing used by the project picker returns **directories only** - never file names or
  contents - and is available over the same authenticated loopback API.

What is **not** covered: the daemon has no user accounts and no way to tell two local OS users apart, so
it assumes a single-user machine. Do not expose the port through a reverse proxy or a tunnel: the
loopback check exists precisely to make that fail loudly rather than quietly.

## Reporting a vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Instead, use GitHub's private reporting:

1. Go to the repository's **Security** tab.
2. Click **Report a vulnerability** (Privately report a vulnerability).
3. Describe the issue, affected version(s), and steps to reproduce.

We aim to acknowledge a report within a few days and will keep you updated on the fix and disclosure
timeline. Please give us reasonable time to address the issue before any public disclosure.

Thank you for helping keep Aiko and its users safe.
