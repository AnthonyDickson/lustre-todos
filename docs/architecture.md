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
  data, check ownership, call that logic, write, and map requests and
  responses.
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
