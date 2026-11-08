# AGENTS.md

> This file should follow the [AGENTS.md standard](https://agents.md/).

## Communication Style

When communicating with the user directly or writing documentation prefer a dry,
direct and technical tone. Try to be clear and concise. Avoid words like: seams,
lands, wire (e.g. wire format).

## Project Overview

A template for full-stack web apps. Run `scripts/rename.sh` to adopt it for your
own project; it renames every `LustreTodos`, `lustre-todos`, and `lustre_todos`,
in code and docs alike.

- **Backend** (`server/`) — F# on .NET 10 with Oxpecker, SQLite, OIDC auth, and
  OpenAPI. Organised as vertical slices.
- **Frontend** (`client/`) — Gleam/Lustre SPA with Tailwind CSS v4 and Vite. MVU
  with an effect system that keeps `update` pure.

The example domain is a todo app. The `Todo` slice (server) and the todo page
(client) are the reference implementations of the patterns in the docs; replace
them with your own features.

## Documentation

| Doc                                                   | Covers                                                      |
| ----------------------------------------------------- | ----------------------------------------------------------- |
| [architecture.md](docs/architecture.md)               | Server design: slices, domain types, errors, auth, logging  |
| [client-architecture.md](docs/client-architecture.md) | Client design: pages, effects, routing, remote data, toasts |
| [database.md](docs/database.md)                       | Migrations, generated code, schema conventions, workflows   |
| [dates.md](docs/dates.md)                             | Calendar dates vs instants                                  |
| [openapi.md](docs/openapi.md)                         | How the OpenAPI document describes F# types                 |
| [testing-strategy.md](docs/testing-strategy.md)       | What to test, at which layer, and how much                  |
| [server-tests.md](docs/server-tests.md)               | Server test host design                                     |
| [e2e-tests.md](docs/e2e-tests.md)                     | E2E isolation and conventions                               |
| [deployment.md](docs/deployment.md)                   | Publishing, Docker, production database, static assets      |
| [prod-oidc-setup.md](docs/prod-oidc-setup.md)         | Configuring an OIDC provider for production                 |

Docs record requirements, design decisions and their rationale, and conventions
that the code cannot express. Keep them at a level that survives refactoring:
name concepts and directories, not functions, and avoid code snippets.
Implementation detail belongs in the code and its comments. When a decision
changes, update the doc in the same change.

## Essential Commands

| Command                    | Purpose                                               |
| -------------------------- | ----------------------------------------------------- |
| `just server-build`        | Build the server                                      |
| `just server-watch`        | Run the server at :5000 (auto-applies DB migrations)  |
| `just server-test`         | Server Expecto tests (via the test adapter)           |
| `just server-restore`      | Restore server packages (needed after the E2E stack)  |
| `just client-install-deps` | Install npm packages (run once before any client cmd) |
| `just client-watch`        | Client dev server at :5173 (Vite + Gleam watch)       |
| `just client-test`         | Client gleeunit tests                                 |
| `just e2e-test`            | Playwright E2E tests in Docker                        |
| `just db-update`           | Apply migrations and regenerate `Data/Db.fs`          |
| `just publish`             | Single-file publish, client bundle included           |
| `just format`              | Format with dprint (see `dprint.json`), Gleam, F#     |
| `just lint`                | Lint F# with fsharplint + [custom lints](./scripts)   |

Dev environment via Nix: `nix develop` (or `direnv allow`). The justfile is the
source of truth for the full target list. Quick start: `docker compose -f
docker/docker-compose.yml up` (log in with `dev`/`dev-password`), or natively
`just server-watch` plus `just client-watch`. See [README.md](README.md).

## Design Principles

Broadly:

- Make the right thing easy: architecture and design should push developers
  toward correct, clear, concise code.
- Prefer simple and direct code, where simple is defined as not "complected" as
  per Hickey's "Simple made easy" talk.
- Prefer systemic fixes over workarounds. When code is convoluted, ask whether
  the design is wrong and fix at the right level, so code churn reduces
  accidental complexity.
- Only add abstractions when there is a clear advantage; avoid over-engineering.

More specifically:

- **Functional programming** — push I/O to the edges (functional core,
  imperative shell, ports and adapters), make illegal states unrepresentable,
  functions as the default abstraction, programs as data (the effect system).
- **Domain-Driven Design** (Wlaschin, _Domain Modeling Made Functional_) — start
  from the pure domain; everything else follows. Focus on _strategic_ DDD
  (ubiquitous language, bounded contexts) over tactical DDD.
- **Vertical Slice Architecture** — group code by feature/workflow; some
  duplication is acceptable, especially when establishing new features or when
  code changes for different reasons; limit blast radius by minimising coupling
  and maximising coherence.
- **Client MVU/TEA** — two-tier shell + page modules; avoid stateful components
  (nested TEA).
- **Testing** — prefer tests where confidence is low (multi-step/stateful logic,
  validation, save/error/retry), not trivial mappings. Each feature needs at
  least one test driving the happy path through the real event flow. Only E2E
  tests exercise client-server interactions. Minimise maintenance while
  maximising confidence. See
  [docs/testing-strategy.md](docs/testing-strategy.md).

## Code Review

Reviews push the codebase toward the design principles and are an opportunity to
simplify and reduce code. Findings must be evidence based, reference the
offending code, and state severity, likelihood, confidence, recommended fix(es),
and trade-offs.

## Rules

Each rule is explained in the linked doc.

- **Slices and shared modules** — server features live in `Feature/<Name>/`, one
  file per HTTP operation. `Shared/`, `Data/`, and (once a second slice needs
  it) `Domain/` are the shared modules. Slices depend on them, never the
  reverse, and slices never import each other (enforced by `just lint`). The
  client mirrors this with `shared/` and one folder per page. See
  [architecture](docs/architecture.md#dependency-rules) and
  [client architecture](docs/client-architecture.md#layout).
- **User scoping** — every query filters by the user id from the `sub` claim. A
  slice that skips this leaks data across users; each slice has a test acting as
  a second user.
- **Errors** — handlers return `Result<unit, DomainError>`; no exceptions escape
  them. Database constraints are checked before writes so violations return
  `400`. See [architecture](docs/architecture.md#errors).
- **Refined types** — value invariants live in single-case unions with a private
  case, built only through `create`. Requests and responses are primitive DTOs,
  mapped at the edges. See [architecture](docs/architecture.md#domain-types).
- **Dates** — calendar dates and instants are never mixed; instants are UTC. See
  [docs/dates.md](docs/dates.md).
- **Migrations** — never modify a released migration; `Data/Db.fs` is generated,
  committed, and never hand-edited. See [docs/database.md](docs/database.md).
- **Pure `update`** — client `update` functions return effects as data; all I/O
  goes through the effect interpreter. See
  [client architecture](docs/client-architecture.md#effect-system).
- **E2E isolation** — specs import `test` and `expect` from
  `tests/e2e/support/fixtures.ts` and create the data they need. Selectors use
  `data-testid` attributes; add them when introducing interactive elements. See
  [docs/e2e-tests.md](docs/e2e-tests.md).
- **Secrets** — never log secrets or tokens; source them from environment
  variables or config, not source control.

## Code Style

- **Formatting** (fantomas via `.editorconfig`): 80 columns; Stroustrup bracket
  style; spaces before parameters/colons/invocations; space after commas and
  semicolons, not before. Fantomas does not wrap comments, so wrap them at 80 by
  hand.
- **Naming**: PascalCase modules matching filenames; camelCase functions;
  PascalCase types (records/DUs); `[<RequireQualifiedAccess>]` on modules
  exposing a type alias.

## Verification & CI

CI runs server tests, client tests, E2E tests, Gleam/F#/dprint format checks,
fsharplint, the custom lints, and a check that `Db.fs` matches the migrations.
Before finishing work, run the relevant tests plus `just lint` and `just
format`; after schema changes run `just db-update` and commit the regenerated
`Db.fs`. When a CI check fails, its log message names the local command to
reproduce it.

## Gotchas

- **Compilation order**: F# compiles server files in the order listed in
  `.fsproj` — insert new `.fs` files before files that depend on them.
- **SqlHydra query parameters**: function parameters can't be captured directly
  in query expressions. Bind them to local `let` values first.
- **Central Package Management**: versions live in `Directory.Packages.props`;
  project files use bare `<PackageReference Include="..." />`.
- **Request body limit**: Kestrel caps request bodies at 64 KB. An endpoint that
  needs more raises its own limit through endpoint metadata rather than raising
  the global limit.
- **After the E2E stack runs**, host builds and `just lint` fail with "package
  was not found" until `just server-restore` runs.
- **Client links to `/login` or `/logout`** are caught by the client's router;
  reach them through a form or a redirect effect.
