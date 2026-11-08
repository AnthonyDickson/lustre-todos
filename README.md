[![CI](https://github.com/AnthonyDickson/lustre-todos/actions/workflows/ci.yml/badge.svg)](https://github.com/AnthonyDickson/lustre-todos/actions/workflows/ci.yml)
[![E2E Tests](https://github.com/AnthonyDickson/lustre-todos/actions/workflows/e2e.yml/badge.svg)](https://github.com/AnthonyDickson/lustre-todos/actions/workflows/e2e.yml)

# LustreTodos

A full-stack web app with an F#/Oxpecker backend (SQLite + OIDC auth + OpenAPI)
and a Gleam/Lustre SPA frontend (Tailwind CSS v4, Vite).
This repo is a template — fork or clone it, then run `scripts/rename.sh` to adopt it for your own project.
This repo only covers the core patterns and infrastructure, for more complete examples see these projects based on this template:

- [Budgeteur](https://github.com/AnthonyDickson/budgeteur-fs)
  Modals, componentless architecture

## Getting Started

Using Docker:

```bash
docker compose up
```

Starts three services:

| Service  | Port | Notes                                           |
| -------- | ---- | ----------------------------------------------- |
| Authelia | 9091 | Dev OIDC provider (user `dev` / `dev-password`) |
| Server   | 5000 | .NET backend with API docs at `/scalar/v1`      |
| Client   | 5173 | Vite dev server with hot reload                 |

Open `http://localhost:5173` and log in with `dev` / `dev-password`.

Or natively:

```bash
just server-build    # Build the server
just server-watch    # Run at :5000 (auto-creates SQLite DB + applies migrations)

# In a second terminal:
just client-install-deps # First time only
just client-watch        # Vite dev server at :5173
```

See the [Justfile](./justfile) for all targets.

## How It Works

- **Backend** — Oxpecker on .NET 10 with OIDC auth (cookie + JWT bearer).
  Endpoints live in vertical slices (one folder per feature, one file per
  operation). SQLite with DbUp
  migrations and SqlHydra type-safe queries. OpenAPI spec at `/openapi/v1.json`,
  interactive API docs at `/scalar/v1`. See [Database](docs/database.md).
- **Frontend** — Gleam/Lustre SPA with nested MVU. A custom `Effect` type
  keeps `update` pure — all I/O (HTTP, localStorage, navigation) runs through
  one interpreter. See [Client Architecture](docs/client-architecture.md).
- **Auth** — Dev OIDC via Authelia (`docker compose up -d`, test user
  `dev`/`dev-password`). See [Production OIDC Setup](docs/prod-oidc-setup.md).
- **Testing** — Expecto server tests, gleeunit client unit tests, Playwright E2E
  tests via Docker Compose. See `docs/server-tests.md`, `docs/client-architecture.md#client-tests`, and `docs/e2e-tests.md`.
- **Deployment** — Single-file publish (`just publish`) or Docker
  (`docker build` + `docker compose -f docker-compose.prod.yml up -d`).
  Intended to be hosted behind a reverse proxy.
  See [Deployment](docs/deployment.md).

## Dev Environment

Nix flake (.NET SDK 10, fsautocomplete, fantomas). NuGet Central Package
Management — versions in `Directory.Packages.props`.

## Docs

- [Server Architecture](docs/architecture.md)
- [Client Architecture](docs/client-architecture.md)
- [Database](docs/database.md)
- [Dates and Timestamps](docs/dates.md)
- [Deployment](docs/deployment.md)
- [OpenAPI Schemas](docs/openapi.md)
- [Production OIDC Setup](docs/prod-oidc-setup.md)
- [Testing Strategy](docs/testing-strategy.md)
- [Server Tests](docs/server-tests.md)
- [E2E Tests](docs/e2e-tests.md)
