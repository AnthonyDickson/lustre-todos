# AGENTS.md

## Project Overview

A full-stack web app with an Oxpecker F# .NET 10 backend (SQLite + OIDC auth + OpenAPI) and a Gleam/Lustre SPA frontend styled with Tailwind CSS v4 and bundled with Vite. OpenAPI schema conventions (annotating types and fields) live in [docs/openapi.md](docs/openapi.md).

This repo is a template — the example domain is a todo app, but the architecture patterns are domain-agnostic. Run `scripts/rename.sh` to adopt it for your own project.

## Essential Commands

```bash
just server-build    # Build the server
just server-restore  # Restore server packages (needed after the E2E stack runs)
just server-watch    # Run the server (auto-applies DB migrations)
just server-test     # Server Expecto tests (via the test adapter)
just client-install-deps  # npm install
just client-watch    # Start the client dev server (Vite + Gleam watch)
just client-build    # Production client bundle
just client-test     # Client gleeunit tests
just e2e-test        # Playwright E2E tests in Docker
just copy-client-dist # Copy client dist into server wwwroot/
just publish         # Single-file publish (builds client, copies assets, publishes server)
just format          # Format F# with fantomas + gleam format
just lint            # Lint F# with fsharplint
```

### Publishing

```bash
just publish                    # linux-x64 (default)
just publish RUNTIME=osx-arm64  # macOS Apple Silicon
```

This builds the client, copies it into the server's `wwwroot/`, then publishes the server as a self-contained single-file binary with trimming. The output is at `server/src/__PROJECT_NAME__.Server/bin/Release/publish/`.

### Database Commands

```bash
just db-migration name=add_foo  # Scaffold a new migration file
just db-migrate                 # Apply pending migrations (standalone script)
just db-generate                # Regenerate Db.fs types from live DB (SqlHydra)
just db-update                  # db-migrate + db-generate (full schema update)
just db-reset                   # Delete DB, re-apply all migrations, regenerate
```

Dev environment via Nix: `nix develop` (or `direnv allow`). Before client commands, run `just client-install-deps` to install npm packages.

## File Structure & Compilation Order

F# compiles server files in the order listed in `.fsproj`. **New files must be inserted before files that depend on them.** The Gleam client has no ordering constraints.

```
server/
  __PROJECT_NAME__.slnx                # XML-based solution format
  Directory.Build.props               # Enables Central Package Management
  Directory.Packages.props            # All NuGet package versions
  global.json                         # .NET SDK version
  dotnet-tools.json                   # Local tool manifest
  fsharplint.json                     # FSharpLint configuration
  sqlhydra-sqlite.toml                # SqlHydra CLI configuration
  .editorconfig                       # Code style (fantomas)
  .fantomasignore                     # Files excluded from formatting
  scripts/
    migrate.fsx                       # Standalone DbUp migration runner
  src/
    __PROJECT_NAME__.Server/
      __PROJECT_NAME__.Server.fsproj  # Project file
      packages.lock.json              # Locked dependency graph
      appsettings.Development.json    # Dev environment config
      Shared/                         # Cross-cutting modules: errors, auth, config, JSON, OpenAPI, logging
      Data/                           # Generated Db.fs, constraint checks, mappings shared by slices
      Feature/                        # Vertical slices, one folder per feature
        Auth/                         # Login and logout endpoints
        Status/                       # Public status endpoint
        Todo/                         # Example slice: one file per operation + TodoEndpoints
      Program.fs                      # Entry point, DbUp, OpenAPI/Scalar config
      migrations/                     # Numbered .sql files applied by DbUp at startup
client/
  src/
    app.gleam                         # Entry point + app shell + routing
    app.css                           # Tailwind CSS v4 entry
    main.js                           # Vite entry point
    lustre_todos/                     # Example feature module (rename for your domain)
      todo_page.gleam                 # Full MVC (model, update, view)
      effect.gleam                    # Effect type + interpreter + HTTP constructors
      http_effect.gleam               # HTTP transport: HttpMethod, HttpError, send/send_with
      effect_ffi.mjs                  # Browser FFI (localStorage, redirect, navigation)
      response.gleam                  # Decode helpers + JSON error formatting
      todo_item.gleam                 # Domain types + JSON codecs
      api_error.gleam                 # ApiError type + JSON codec
      guard.gleam                     # use-compatible early-return for Option/Result
  test/
    lustre_todos/
      todo_page_test.gleam            # Pure unit tests for update logic
tests/
  e2e/
    global-setup.ts                   # OIDC login before test suite
    todos.spec.ts                     # Playwright test specs
    playwright.config.ts              # Playwright configuration
```

## Architecture

### Auth (`Shared/Auth.fs`, `Feature/Auth/`)

Uses `Microsoft.AspNetCore.Authentication.OpenIdConnect` with two parallel schemes:

- **Cookie** (`CookieAuthenticationDefaults`) — SPA session, authorization code flow.
- **JWT Bearer** (`"bearer"`) — for Scalar API docs, PKCE flow.

Both satisfy the `"authenticated"` authorization policy. The `requireAuth` middleware gates protected endpoints.

Cookie defaults in production: `SecurePolicy=Always`, `SameSite=Lax`, `HttpOnly=true`. In dev, `RequireHttpsMetadata=false` and self-signed certs are accepted.

Environment config via `Oidc:Authority`, `Oidc:ClientId`, `Oidc:ClientSecret`, `Oidc:CallbackPath`. The `Login:ReturnUrl` and `OAuth2:*` settings are read in `Program.fs`.

### Vertical Slice Architecture (example: `Feature/Todo/`)

Each feature is a folder under `Feature/`, with one file per HTTP operation (e.g. `CreateTodo.fs`). The template uses a todo domain as an example — replace it with your own slices:

- **Operation modules** — each holds its request type, private queries, `handler`, and an `endpoint` that adds OpenAPI metadata. The `Path` literal is public so tests can build URLs from it.
- **`<Feature>Endpoints.all`** — the slice's endpoints grouped by HTTP method. `Program.fs` wraps them in the auth filter; the test host routes the same list without it.
- **Shared modules** — `Shared/` (cross-cutting infrastructure) and `Data/` (generated `Db.fs` and code several slices need). Slices depend on them, never the reverse, and slices never import each other.

Dependencies are passed as function arguments: the `QueryContextFactory` (from `Data/Db.fs`) is created once in `Program.fs` and passed to each endpoint. All queries use SqlHydra computation expressions (`selectTask`, `insertTask`, `updateTask`, `deleteTask`).

Each `endpoint` function wraps the handler with OpenAPI metadata via `addOpenApi`. Routes use `/api/<resource>` prefix for collection endpoints and `/api/<resource>/{id}` for item endpoints.

#### OpenAPI schema annotations

Types and fields are annotated for the published document in `Shared/OpenApi.fs`. Prefer native `System.ComponentModel.DataAnnotations` attributes, which ASP.NET Core maps onto JSON Schema keywords; fall back to the `SchemaHint` attributes only where they cannot express the constraint (string enums derived from F# unions, minimum-only or `multipleOf` numeric bounds). The `SchemaHintTransformer` and `DecimalSchemaTransformer` are registered in `addOpenApiToBuilder` (`Program.fs`). See [docs/openapi.md](docs/openapi.md) for the full reference and wiring order.

### Database (`Data/Db.fs` + `migrations/`)

- **`Db.fs`** — Auto-generated by `dotnet sqlhydra sqlite`. Contains record types, table declarations, and `QueryContextFactory`. Committed to source control — compiles immediately after clone, no code-gen needed. Do not modify directly, use the just targets.
- **`migrations/`** — Numbered `.sql` files embedded as resources. DbUp runs them in order at startup, tracking applied scripts in a `SchemaVersions` table. Use SqlHydra-compatible type hints (`GUID`, `BOOLEAN`, `DATETIME`) in column definitions — these aren't real SQLite types but influence codegen. See [SqlHydra's SqliteDataTypes.fs](https://github.com/JordanMarr/SqlHydra/blob/main/src/SqlHydra.Cli/Sqlite/SqliteDataTypes.fs) for the full list.
- **`scripts/migrate.fsx`** — Standalone DbUp migration runner that reads SQL files directly from disk. Used by `just db-migrate` to apply migrations without building the server — avoids the chicken-and-egg problem where a schema change breaks the build before `Db.fs` is regenerated.
- **Error handling**: handlers and queries do not catch exceptions. `Endpoint.handler` converts what a handler throws into a `DomainError` (SQLite constraint violation → `409`, other SQLite errors → `500`, oversized body → `400`, anything else → `500`). Handlers focus on domain logic (`Option` → `404`, validation → `400`). A global middleware in `Program.fs` catches exceptions outside a handler.

#### Schema Workflows

**After cloning:**

```bash
cd server && dotnet restore
just server-build    # Db.fs is committed — compiles immediately
just server-watch   # DbUp creates the database + applies migrations at startup
```

**Changing the schema:**

```bash
just db-migration name=add_priority   # scaffolds a new .sql migration
# … write the SQL in the new file …
just db-update                        # apply migrations + regenerate types
# … fix compile errors in the domain module's mapping functions …
just server-build
```

**Starting fresh:**

```bash
just db-reset                         # delete DB, re-apply all migrations, regenerate
```

**Key constraints:**

- Migration files are applied once, in order — never modify an already-run migration. Add a new file for changes.
- `Db.fs` is auto-generated — do not hand-edit. The mapping layer in the domain module is the control point for DB ↔ API type conversions.
- Connection string is `Data Source=<name>.db` (relative, resolves to project root). Override with an absolute path (e.g. `/data/app.db`) in production via `ConnectionStrings__Default` env var.

### Logging Architecture

The server uses a dual-layer logging system:

1. **Request-scoped buffered logging** (`RequestLogging.fs`): Collects structured log entries during request processing, then emits them as a single JSON array in the response log. This keeps related log entries together rather than interleaved.
2. **Global Serilog pipeline** (`Serilog.AspNetCore`): Handles startup logs and unhandled exceptions. Console output uses `RenderedCompactJsonFormatter`. File output is opt-in via `Logging__FilePath` and rolls at 10 MB, keeping 10 files. `Microsoft.AspNetCore` logs below warning are dropped, since the request log already records every request.

### Client (Gleam/Lustre SPA)

Two-layer MVU:

- `app.gleam` — Entry point + thin shell: routing via custom navigation effects + delegate to feature pages. Effect interpreter wiring.
- `<feature>_page.gleam` — Full MVC per feature: model, update (pure, returns `Effect` values), view.
- `effect.gleam` — Custom `Effect` type with 9 variants (`HttpRequest`, `LoadFromStore`, `SaveToStore`, `Redirect`, `After`, `Navigate`, `PushUrl`, `ReplaceUrl`, `Batch`, `None`) + interpreter (`run`) + `map`/`batch`/`none` helpers + thin per-method HTTP constructors (`get`, `post`, `put`, `patch`, `delete`) + navigation constructors. Feature modules only need to import `effect` for everyday effects.
- `http_effect.gleam` — HTTP transport layer. Defines `HttpMethod` and `HttpError` types, exposes `send` / `send_with` (configurable request building). Depends only on stdlib — the extension point for auth headers, retry logic, or custom HTTP methods.
- `effect_ffi.mjs` — Thin JS wrappers for `window.localStorage`, `window.location.assign`, and client-side navigation (click interception on internal links, popstate listener, `history.pushState`/`replaceState`).
- `response.gleam` — `decode_success` (2xx body → typed `Result`) and `http_error_to_api_error` (`HttpError` → `ApiError`) helpers.
- `guard.gleam` — `use`-compatible helpers (`guard.some`, `guard.ok`) for early return from `Option`/`Result`.

### Effect System Design

The `Effect` type is the cornerstone of the client architecture. Every `update` function returns `#(Model, Effect(Msg))` — pure data describing what side effects to perform. A single `effect.run` interpreter executes them, keeping `update` functions testable without mocking.

| Effect               | Side effect                             |
| -------------------- | --------------------------------------- |
| `HttpRequest`        | `gleam_fetch` → typed callback          |
| `LoadFromStore`      | `window.localStorage.getItem`           |
| `SaveToStore`        | `window.localStorage.setItem`           |
| `Redirect`           | `window.location.assign`                |
| `After(delay, msg)`  | Dispatch `msg` after `delay` ms         |
| `Navigate`           | Click/popstate interception + pushState |
| `PushUrl/ReplaceUrl` | `history.pushState` / `replaceState`    |
| `Batch([...])`       | Run multiple effects                    |
| `None`               | No-op                                   |

Bridging into Lustre:

```gleam
fn update_with_effect(model, msg) -> #(Model, lustre_effect.Effect(Msg)) {
  let #(new_model, effect) = update(model, msg)
  #(new_model, lustre_effect.from(fn(dispatch) { effect.run(effect, dispatch) }))
}
```

### Tests

Three layers: server Expecto tests through the Expecto test adapter (`just server-test`, i.e. `dotnet test server/__PROJECT_NAME__.slnx`, ephemeral SQLite, no auth), client gleeunit tests (`just client-test`, pure `update` unit tests, no browser), and E2E Playwright tests (`just e2e-test`, full stack in Docker Compose with Authelia). E2E tests use `data-testid` attributes for selectors — add them to feature page views when new interactive elements are introduced.

### Static Assets

**Client assets** (images, fonts, favicons, PDFs — anything the SPA references) live in `client/public/`. Vite serves them at root in dev and copies them into `dist/` on build. They reach the server via `just copy-client-dist`.

**Server-only assets** (e.g. `robots.txt` that should exist regardless of the client bundle) live in `server/src/__PROJECT_NAME__.Server/wwwroot/`. Note that `wwwroot/` is gitignored and recreated by `copy-client-dist`, so the source of truth for any persisted file must live elsewhere (or use a build step).

## Code Style & Conventions

### Formatting (fantomas via `.editorconfig`)

- Stroustrup bracket style, spaces before parameters/colons/invocations
- Space after commas and semicolons, not before

### Naming

- Modules: PascalCase, matching filename
- Functions: camelCase
- Types (records, DUs): PascalCase
- `[<RequireQualifiedAccess>]` on modules that expose a type alias (e.g. `Store`)
- `[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]` when module and type share a name

### Error Handling

Json-serialized `ApiError` records (`{ Error: string; Details: string; StatusCode: int option }`) with appropriate HTTP status codes. No exceptions.

## Gotchas

- **Lockfile enforced**: After adding/updating NuGet packages, run `dotnet restore --force-evaluate <project>`.
- **Compilation order**: Insert new `.fs` files at the correct position in `.fsproj`.
- **SqlHydra query parameters**: Function parameters can't be captured directly in query expressions. Bind them to local `let` values first (e.g. `let idStr = id.ToString()` before using in a `where` clause).
- **Central Package Management**: Versions in `Directory.Packages.props`; project files use bare `<PackageReference Include="..." />`.
- **Client needs `npm install`** before first `just client-watch` or `just client-build`.
- **Tests** — xUnit in `server/tests/`, gleeunit in `client/test/`, Playwright E2E in `tests/e2e/`.
