# Server Tests

Expecto tests in `server/tests/LustreTodos.Tests/`, one folder per slice. What to
test and at which layer is decided by [testing-strategy](testing-strategy.md). Also
see [E2E Tests](e2e-tests.md) for end-to-end Playwright tests.

## Test host

Endpoint tests run against a real server host with an in-memory test server,
built per test from only what the feature under test needs:

- **In-memory SQLite** with every migration applied, so tests run the real
  schema and constraints, are hermetic, and need no files or network.
- **The slice's production endpoint list** (e.g. `TodoEndpoints.all`). The tests
  route the same endpoint values the production host uses, so a route added to
  a slice is reachable from tests without editing the host.
- **No auth middleware.** Production wraps endpoints in the auth filter, which
  needs the full OIDC setup. The test host skips it and injects a fake user with
  a `sub` claim instead. A request can name a different user against the same
  database with the `X-Test-User` header (`TestApp.ClientForUser`), which the
  cross-user scoping tests use.

Each test creates its own app, so it starts from an empty database and tests can
run in parallel. Tests build URLs from each operation's `Path` literal, and
`TestHttp` holds the JSON request and response helpers.

On a `5xx` response the host prints the request's server-side log to stderr, so
a failing test shows the real error rather than an opaque body. `dotnet test`
shows it with `--logger "console;verbosity=detailed"`.

## Running

```bash
just server-test
# or
dotnet test server/LustreTodos.slnx
```

`dotnet test` through the Expecto test adapter is the only runner, locally and
in CI, and it is the same mechanism IDE test explorers use. The adapter
discovers tests by reflecting over `[<Tests>]` values, so every test list must
carry the attribute. Expecto settings pass through with an `Expecto.` prefix
after `--`; `--filter` and `--list-tests` work as usual:

```bash
dotnet test server/LustreTodos.slnx --list-tests
dotnet test server/LustreTodos.slnx --filter "FullyQualifiedName~Todo"
dotnet test server/LustreTodos.slnx -- Expecto.parallel=false
```

The test project has no entry point of its own: the test SDK generates one. Do
not disable it (`GenerateProgramFile`); without an entry point the F# assembly
is not initialised and every test is discovered as `null`:

```
Test is null. Assembly may not be initialized. Consider adding an [<EntryPoint>] or making it a library/classlib.
```
