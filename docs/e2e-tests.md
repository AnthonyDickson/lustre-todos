# E2E Tests

Playwright tests in `tests/e2e/`, run against the full stack in Docker Compose
with host networking (Authelia, server, Vite, Playwright):

```bash
just e2e-test
```

E2E is the only layer that exercises the client and server together.

The server container restores packages into the bind-mounted `server/`, with
the container's package paths, so host builds and `just lint` fail with
"package was not found" afterwards. Run `just server-restore` to fix them.

## Isolation

Every test is independent of the order tests run in and of what runs in
parallel. Specs import `test` and `expect` from `support/fixtures.ts`, not from
`@playwright/test`, to get two layers of isolation:

- **A user per worker.** The server scopes all data by user, so workers logged
  in as different users never see each other's data in the shared database. Each
  worker logs in once through the real OIDC flow and reuses the session.
- **A reset per test.** Before each test, a development-only endpoint
  (`DELETE /api/test/user-data`) deletes the current user's rows from every
  table with a `UserId` column. Tables are discovered from the schema, so new
  tables are covered without changes. Tests in the same worker, and retries,
  start from no data.

Each test also gets a fresh browser context, so localStorage starts empty. The
server database is recreated on each run.

The E2E users (`e2e-0` to `e2e-3`, password `dev-password`) are defined in
`authelia/users.yml`. The worker count in `playwright.config.ts` must not exceed
the number of users; to raise it, add users to both the Authelia config and the
fixtures.

A test that would end the provider's session (e.g. following the logout
redirect to Authelia) must stub that page, since the other tests in its worker
log in again through that session.

## Flakiness

Tests run fully in parallel. A failing test is retried once to capture a trace,
and the run fails if any test passes only on retry, so flakiness is reported
rather than hidden. To check for order or parallelism dependencies, run the
suite repeatedly without retries:

```bash
docker compose -f docker-compose.e2e.yml run --rm e2e \
  sh -c "npm install && npx playwright test --retries=0 --repeat-each=4"
docker compose -f docker-compose.e2e.yml down
```

## Conventions

- Tests create the data they need; they never depend on data from other tests.
- Selectors use `data-testid` attributes. Add them to page views when
  introducing interactive elements.

## Configuration

`playwright.config.ts`: `ignoreHTTPSErrors: true` (self-signed certs),
`screenshot: 'on'`, `trace: 'on-first-retry'`.
