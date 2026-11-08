# Server Architecture

How the server is structured and the rules that hold across it. The client is
described in [client architecture](client-architecture.md); storage, dates, and
tests have their own docs.

## Vertical slices

Each feature is a folder under `Feature/`, with one file per HTTP operation. A
slice's endpoint list is grouped by HTTP method and used by both the production
host and the test host, so a route is declared once. Routes follow
`/api/<resource>` for collections and `/api/<resource>/{id}` for items. Every
endpoint carries OpenAPI metadata ([openapi](openapi.md)).

Dependencies are passed as function arguments. The database query context is
created once at startup and passed to each endpoint; there is no DI container.

The todo slice is the example to copy. Its client chooses each todo's ID (a v7
UUID) and creation time, so the client can show a new todo before the server
responds.

## Where domain logic lives

Domain logic is pure and free of I/O:

- **Workflow logic, in its slice.** A slice's own types and rules are pure
  functions in a module of the slice, apart from its handlers. Handlers load
  data, check ownership, call that logic, write, and map requests and responses.
- **The shared model, in `Domain/`.** Domain types used by more than one slice,
  their invariants, and the rules several workflows share. The template has no
  `Domain/` folder yet, because the todo slice is the only consumer of its
  types.

Code that translates an external system's model (e.g. a file import format) is
an adapter in the slice that uses it, turning the external model into domain
types at the edge.

## Dependency rules

`Shared/` (cross-cutting infrastructure), `Data/` (the generated database code
and mappings several slices need), and `Domain/` are the shared modules.

- **One-way dependencies: slices → shared modules.** The shared modules never
  depend on a slice, and slices never import each other. `just lint` enforces
  both.
- **Promote on the second consumer.** Code used by one slice stays in that
  slice, and moves to the shared modules when a second slice needs it. This
  applies to row mappings, refined types, and workflow logic alike.
- **Ownership test.** A concept belongs in the shared modules only if more than
  one slice drives its changes, and it should change less often than its
  consumers. A shared module that changes with every feature is mis-owned; move
  it into the slice that drives it.
- **Invariants live with their type; orchestration stays in the handler.**
  Intrinsic validity (non-empty, length) is defined with the type. Orchestration
  (auth, queries, uniqueness checks against the database) is the handler's.
- **A table has one owner of its rules, not one writer.** Any slice may write a
  table, but only through the domain type that owns the table's invariants and
  its row mapping. A user action that changes several tables is one database
  transaction in one slice.
- **Slices own their read shapes.** A feature needing a different shape defines
  its own read model rather than growing a shared type.

## Domain types

Value invariants are enforced by **refined types**: a single-case union with a
private case, defined with the record that owns the field. The only way in is a
`create` function that validates and returns a `Result`; the only way out is
`value`. Consequences:

- **Records stay public.** A record with a refined field cannot be built with an
  invalid value, so the record itself needs no private constructor.
- **Unwrap at the edges only.** Refined values are unwrapped when mapping to a
  database row and when mapping to a response. Elsewhere they are opaque.
  Structural equality still holds, so tests compare them directly.
- **Stored rows are trusted.** Reading a row back into the domain does not
  re-validate; rows were validated on write.

## Representations at the boundary

A domain entity has up to three other representations, each kept separate:

| Representation | Shape                                 | Changes with     |
| -------------- | ------------------------------------- | ---------------- |
| Request DTO    | Raw primitives, validated by `create` | The API contract |
| Response DTO   | Primitives only, encode-only          | The API contract |
| Row            | Generated from the schema             | The schema       |

The row mapping (a codec) and the response mapping are separate modules because
they have different dependencies and change for different reasons. DB columns
never reach the API. Because DTOs hold only primitives, JSON encoding and
OpenAPI schema generation need no custom code per type.

## Errors

Failures are values of one closed union, `DomainError`, and every handler
returns `Result<unit, DomainError>`. A single handler wrapper maps each case to
a status code (validation `400`, not found `404`, conflict `409`, missing user
claims `401`, everything else `500`) and every error response has the same JSON
body: error, details, status code, and request id. Platform exceptions (e.g.
from SQLite) are converted to `DomainError` in that wrapper, so slices never
handle them; a global middleware catches anything else as a last resort.

Integrity constraints are checked explicitly before writes, because SQLite does
not always report which column a violation came from. See
[database](database.md).

## User scoping

Every row belongs to one user, identified by the `sub` claim, and every query
filters by it. A slice that skips the filter leaks data across users, so each
slice has a test acting as a second user ([server tests](server-tests.md)).

## Auth

Two authentication schemes sit behind a policy scheme that picks one per request
by the presence of an `Authorization: Bearer` header:

- **Cookie** — the SPA session, from the authorization code flow.
- **JWT bearer** — the API docs (Scalar), using PKCE. Development only.

Either satisfies the single `authenticated` policy that protects every API
endpoint. `/login` starts the challenge and returns to the page the client was
on when its session expired, a local path resolved against the configured return
URL, which is absolute since the client may be served from another origin. Any
other return path falls back to the configured URL, so `/login` cannot redirect
to another site. `/logout` takes a POST, so another site cannot log the user out
with a link, clears the session cookie, and redirects to the provider's logout
page when one is configured, otherwise to the client's signed out page. That
page makes no API request, so it does not send the user straight back to the
provider; a `401` anywhere else does, so an expired session with a live provider
session signs back in without a click. Outside development, cookies are secure,
`HttpOnly`, `SameSite=Lax`, with a one-hour sliding expiry.

Known limits:

- Claims come from the ID token; the userinfo endpoint is not called.
- Audience validation is off unless audiences are configured, because Authelia
  access tokens carry no `aud` claim.
- Authelia 4.39 lacks RP-initiated logout, so the provider session is ended
  through its logout page rather than the standard end session endpoint.

Provider setup is in [prod-oidc-setup](prod-oidc-setup.md).

## Configuration

Settings are typed sections bound from `appsettings*.json` or environment
variables (e.g. `Oidc__ClientSecret`). Every section is validated at startup,
and the server refuses to boot, listing every missing or malformed setting,
rather than failing on first use.

## Logging

1. **Request-scoped logs.** Handlers append structured entries to a per-request
   log, emitted as one JSON array when the response completes, so entries for a
   request stay together instead of interleaving with concurrent requests.
2. **Global Serilog pipeline.** Startup and unhandled exceptions, as compact
   JSON on the console; file output is opt-in (`Logging__FilePath`) and rolls
   over at 10 MB, keeping 10 files. Framework logs below warning are dropped,
   since the request log already records every request.

## Status endpoint

`GET /api/status` reports the build version, uptime, and database connectivity,
and returns `503` when the database is unreachable. Container health checks use
it, so it is unauthenticated.

## Test support

`DELETE /api/test/user-data` deletes the current user's data, for the E2E suite
to start each test from nothing ([e2e tests](e2e-tests.md)). It is registered in
Development only, so it cannot be reached against real data.
