# Server Tests

Expecto tests in `server/tests/__PROJECT_NAME__.Server.Tests/`. Also see
[E2E Tests](e2e-tests.md) for end-to-end Playwright tests.

## Architecture

Uses a `TestApp` module with `HostBuilder` + `TestServer` — the .NET 10
replacement for the deprecated `WebHostBuilder`.

The host contains only what endpoints need:

- SQLite (temp file, auto-cleaned)
- Routing + Oxpecker middleware
- `Api.endpoints` directly — **not** the full domain module endpoints

### Why `Api.endpoints` not the domain module endpoints

The domain module's top-level `endpoints` wraps every endpoint with `requireAuth`,
which needs the full OIDC/JWT setup. Tests call `Api.endpoints` directly, bypassing
auth entirely. Production calls the domain module's `endpoints` and gets the auth filter.

### Fixture lifecycle

`TestApp.create()` produces a record (`{ Client, CleanDatabase, Dispose }`) that
creates a temp SQLite file, applies migrations once, and cleans up on
`Dispose`. A `Lazy<TestApp>` at the module level shares one instance across
all tests. Each test calls `app.CleanDatabase()` before acting.

## Running Tests

```bash
just server-test
# or
dotnet test server/__PROJECT_NAME__.slnx
```

This is the only way to run them, locally and in CI, and it is the same mechanism IDE test explorers use.
`YoloDev.Expecto.TestSdk` bridges Expecto to VSTest; it discovers tests by reflecting over `[<Tests>]`-attributed values
in the built assembly, so every test list must carry `[<Tests>]`. There is no explicit test program:
`Microsoft.NET.Test.Sdk` sets `OutputType` to `Exe` and adds its own generated entry point, which must not be disabled
(see below).

Useful options:

```bash
dotnet test server/__PROJECT_NAME__.slnx --list-tests
dotnet test server/__PROJECT_NAME__.slnx --filter "FullyQualifiedName~Todo"
dotnet test server/__PROJECT_NAME__.slnx -- Expecto.parallel=false   # Expecto settings, `Expecto.`-prefixed
```

### Don't set `GenerateProgramFile` to `false`

`Microsoft.NET.Test.Sdk` sets `OutputType=Exe` and, unless the project declares its own `Program.fs`, adds a generated
entry point. Suppressing that entry point leaves the F# assembly without one, and Expecto's discovery then reads every
`[<Tests>]` value as `null`, failing the run with:

```
Test is null. Assembly may not be initialized. Consider adding an [<EntryPoint>] or making it a library/classlib.
```

## Test Cases

The example domain (todos) covers the following CRUD test patterns:

| Method | Endpoint               | Asserts                     |
| ------ | ---------------------- | --------------------------- |
| GET    | `/api/todos`           | 200, `[]`                   |
| GET    | `/api/todos` (seeded)  | 200, seeded item present    |
| GET    | `/api/todos/{id}`      | 200, matches seeded item    |
| GET    | `/api/todos/{id}`      | 404                         |
| POST   | `/api/todos`           | 201, matches input          |
| PATCH  | `/api/todos/{id}`      | 200, reflects changes       |
| DELETE | `/api/todos/{id}`      | 204, 404 on re-GET          |
| DELETE | `/api/todos/completed` | 204, incomplete-only remain |
