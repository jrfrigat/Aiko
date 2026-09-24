# Aiko - Git and Security

Aiko runs on your machine and is built to stay there. This document collects, in one place, what the
daemon protects, how secrets are handled, and how Aiko treats Git. It is the user-facing companion to the
repository's [`SECURITY.md`](../../SECURITY.md), which stays the normative policy.

## The security model

- The daemon binds to **loopback only**. A request whose `Host` header is not loopback is refused with
  `400`, so a proxy or a tunnel in front of the daemon fails loudly instead of quietly.
- Browser origins are checked: only the daemon's own page - its scheme and port, on `127.0.0.1` or
  `localhost` - gets through. Any other origin, another program's page on another local port included, gets
  `403`, and so does a request the browser marks as `Sec-Fetch-Site: same-site` or `cross-site`. No page may
  frame the board (`Content-Security-Policy: frame-ancestors 'none'`).
- The browser's session cookie opens the board's REST API only: the MCP endpoint takes the token in
  `Authorization: Bearer` and nothing else, and a write the cookie authenticates must carry the
  `X-Aiko-Request` header, which the board sends and a form on another page cannot.
- A repository is data, not a trusted input: identifiers read from `.aiko` (card kinds, workflow and stage
  ids) must be plain file names, and a document that breaks the rule is refused with its file named. Every
  path Aiko builds from them - card directories, memory, workflows, agent files inside the project - has to
  stay inside the project and may not cross a junction or symbolic link below its root.
- Clients authenticate with an **access token**. A browser is paired once with a one-time code.
- `AIKO_INSECURE=1` disables authentication. It exists for local debugging only and should never be left
  on.
- Project data lives in `.aiko` inside your project; the daemon's own state lives in
  `%LOCALAPPDATA%\Aiko`. Nothing is uploaded anywhere, and unregistering a project never deletes files on
  disk.

What is **not** covered: the daemon has no user accounts and no way to tell two local OS users apart, so it
assumes a single-user machine. Do not expose the port through a reverse proxy or a tunnel - the loopback
check exists precisely to make that fail loudly.

## The access token

The token is created once and reused across daemon restarts:

```text
%LOCALAPPDATA%\Aiko\access-token
```

Every REST call and the MCP endpoint require it. `aiko token show` prints it, and `AIKO_TOKEN` can fix the
token to a value you provide (otherwise it is generated and persisted).

Rotate the token if it ever leaked - for example if an agent configuration file holding it was committed:
replace the contents of `access-token` with a new value and restart the daemon. Anything that cached the
old token (paired browsers, agent MCP configurations) stops working until it is paired or reinstalled.

If you see `401 Unauthorized`, the client is not holding the current token: re-pair the browser with
`aiko ui`, or rewrite the agent's MCP configuration with `aiko agent install`.

## Pairing a browser

The UI needs a token, so the browser is paired with a **one-time code**:

- `aiko ui` starts the daemon if none is answering, pairs the browser and opens the board;
- another browser on this machine is paired the same way: run `aiko ui` again (each run issues a fresh
  code), or open the `Aiko pairing URL` the daemon prints when it starts. Aiko listens on the loopback
  interface only, so a browser on another machine cannot reach it.

The code is single-use: consuming it removes it from the set of valid codes. `AIKO_PAIR_CODE` fixes the
pairing code to a known value and is meant for tests and scripts.

## Secrets in agent configuration

The MCP endpoint requires the same token, so `aiko agent install` writes it into the agent's MCP
configuration:

- Claude Code, Cursor and ZCode get an `Authorization: Bearer ...` header;
- Codex gets the name of the `AIKO_TOKEN` environment variable it reads instead.

**Those configuration files are secrets.** Project initialization adds them to `.gitignore`, so they are
not committed by accident. Never commit a file that contains the token; if one slipped in, rotate the token
as described above.

## Git policies

### Git policy at project creation

A project records a **git policy** that decides how `.aiko` is treated by version control:

- `local-only` (the built-in template's) - the whole `.aiko` directory is added to `.gitignore`, so project knowledge
  stays on this machine;
- `track-project-knowledge` - `.aiko` workflows, settings and memory stay under version control, so the
  project's rules travel with the code.

The policy is part of the template: `aiko init` takes it from the template unless the request names one.

### Commit policy

The project's **commit policy** answers who commits during a stage execution:

- `deny` (the default) - you commit; the agent does not;
- `ask` - the agent reports a commit and waits for your approval (the execution becomes
  `waiting-for-user`; you approve it on the board - the agent cannot approve its own commit);
- `allow` - the agent makes the commit and reports the SHA.

Aiko itself never creates a commit: it only records what the agent reports. It does run `git`, read-only -
`status`, `log`, `diff` and `show` for the branch, the changes and a card's diff. The policy
is therefore a rule the agent reads, not something the daemon can enforce for it.

### Push policy

The **push policy** answers the same question for pushing from the shared checkout, with the same three
values. It is a rule the agent reads rather than one Aiko enforces: Aiko has no push of its own, so nothing
can pause an execution until you approve. The context the agent reads first states the policy, and the
`/aiko-run` procedure repeats it. Push and branch handling beyond this are post-MVP.

## Reporting a vulnerability

Do not report security vulnerabilities through public GitHub issues. Use GitHub's private reporting on the
repository's **Security** tab (**Report a vulnerability**), as described in
[`SECURITY.md`](../../SECURITY.md).
