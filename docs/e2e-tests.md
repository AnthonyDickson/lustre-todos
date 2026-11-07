# E2E Tests

Playwright tests running the full stack in Docker Compose with host networking
(Authelia → .NET server → Vite client → Playwright).

```bash
just e2e-test
```

The server container restores packages into the bind-mounted `server/`, with
the container's package paths, so host builds and `just lint` fail with
"package was not found" afterwards. Run `just server-restore` to fix them.

## Setup

### Global Auth (`global-setup.ts`)

Logs in once via Authelia before the test suite, saves `auth.json`:

1. Navigate to app → SPA 401 → OIDC redirect → Authelia login
2. Fill MUI form (`dev` / `dev-password`) using click + pressSequentially
3. Handle consent screen (first login only)
4. Wait for redirect back to app, save storage state

### Database

Server uses `/tmp/todos_e2e.db` (ephemeral, cleared on each startup via
`rm -f` before `dotnet watch run`). Fresh DB every run.

## Test Selectors

All selectors use `data-testid` attributes from `todo_page.gleam`:

`new-todo-input`, `edit-todo-input`, `todo-item`, `todo-title`,
`todo-checkbox`, `delete-todo`, `clear-completed`, `todo-count`,
`filter-all`, `filter-active`, `filter-completed`, `logout`

## Configuration

`playwright.config.ts`: `ignoreHTTPSErrors: true` (self-signed certs),
`screenshot: 'on'`, `trace: 'on-first-retry'`, auth via `storageState`.
