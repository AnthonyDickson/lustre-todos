# Deployment

## Publishing via Binary

```bash
just publish                    # linux-x64 (default)
RUNTIME=osx-arm64 just publish  # macOS Apple Silicon
```

Builds the client Vite bundle, copies it into `server/src/__PROJECT_NAME__.Server/wwwroot/`,
then publishes the server as a self-contained single-file binary with trimming.
Output: `server/src/__PROJECT_NAME__.Server/bin/Release/publish/`

## Publishing via Docker

### Building

```bash
# Build
docker build -t ghcr.io/your-org/__PROJECT_KEBAB__:latest .
# Login, e.g. with a GitHub Personal Access Token with the `write:packages` permissions
cat $MY_PAT | docker login ghcr.io -u $(gh api user --jq .login) --password-stdin
# Push
docker push ghcr.io/your-org/__PROJECT_KEBAB__:latest
```

The multi-stage `Dockerfile` installs Node.js and Gleam, builds the client, then
publishes the server for musl (`linux-musl-x64`) into Microsoft's `runtime-deps`
Alpine image, which holds only the native libraries a self-contained .NET app
needs, plus BusyBox for the healthcheck's `wget`. It runs as the image's non-root
`app` user (UID 1654).

The server is built with invariant globalization: it formats with the invariant
culture and compares strings ordinally, so it does not need ICU, the largest
library a .NET image would otherwise carry. Code that needs culture-specific
formatting or sorting would have to add ICU back.

> [!NOTE]
> When publishing from the CLI, you will need to manually set the "package" (image) to publish and then assign the
> package to your repo. See the page on [Working with the Container Registry](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry)
> and [Connecting a repository to a package](https://docs.github.com/en/packages/learn-github-packages/connecting-a-repository-to-a-package)
> Building and pushing the image from a GitHub Workflow will automatically associate the package with the source repo.

### Deploying

```bash
cp docker-compose.prod.example.yml docker-compose.prod.yml
# edit docker-compose.prod.yml with your OIDC settings, then:
docker compose -f docker-compose.prod.yml up -d
```

The example compose file ships with dev OIDC defaults. Override for production:

```bash
Oidc__Authority=https://auth.example.com \
Oidc__ClientId=__PROJECT_KEBAB__ \
Oidc__ClientSecret="$(pass show oidc/__PROJECT_KEBAB__/client-secret)" \
Oidc__CallbackPath=/signin-oidc \
Login__ReturnUrl=https://app.example.com/ \
  docker compose -f docker-compose.prod.yml up -d
```

> **Note**: `docker-compose.prod.yml` is gitignored. Copy from `docker-compose.prod.example.yml` and customize it.

See [Production OIDC Setup](prod-oidc-setup.md) for the full OIDC configuration guide.

### Database in Production

The server uses SQLite. Override the connection string to use a persistent path:

```bash
ConnectionStrings__Default="Data Source=/data/app.db" \
  docker compose -f docker-compose.prod.yml up -d
```

Use an absolute path — relative paths resolve to the container's working directory,
which is ephemeral.

> [!WARNING]
> The container runs as the non-root `app` user (UID 1654). The image creates
> `/data` and `/logs` owned by `app`, so named Docker volumes mounted there
> inherit the right permissions; named volumes mounted at other paths are owned
> by root and the server cannot write to them. **Bind mounts** (`driver_opts`
> with `o = "bind"`) require the host directory to already exist and be writable
> by UID 1654:
>
> ```bash
> mkdir -p /var/lib/my-app && chown 1654:1654 /var/lib/my-app
> ```
>
> On NixOS, use `systemd.tmpfiles.rules`:
>
> ```nix
> systemd.tmpfiles.rules = [ "d /var/lib/my-app 0755 1654 1654 -" ];
> ```
>
> Failure to do this results in `SQLite Error 14: 'unable to open database file'`.

### Health checks

Container healthchecks probe `GET /api/status`, which reports the build version,
uptime, and a database connectivity probe, returning `503` when the database is
unreachable.

The image has BusyBox `wget` for the probe. Point it at `127.0.0.1` rather than
`localhost`: the server listens on IPv4 only, and BusyBox `wget` tries `::1`
first without falling back.

## Static Assets

**Client assets** (images, fonts, favicons — anything the SPA references) live in
`client/public/`. Vite serves them at root in dev and copies them into `dist/`
on build. They reach the server via `just copy-client-dist`.

**Server-only assets** (e.g. `robots.txt`) live in `server/src/__PROJECT_NAME__.Server/wwwroot/`.
The `wwwroot/` directory is gitignored and populated by `copy-client-dist` during
publish. Persistent server assets should have their source of truth elsewhere
(e.g. a build step that copies them in).

## Hosting

The web server is intended to run behind a reverse proxy such as nginx or Caddy.
No effort is made to make the web server secure to deploy on its own.

The server trusts `X-Forwarded-For` and `X-Forwarded-Proto` from loopback
(`127.0.0.0/8`, `::1`) and the private ranges `10.0.0.0/8`, `172.16.0.0/12`, and
`192.168.0.0/16`, which cover a proxy on the same host, including one reaching a
container through a published port. A proxy at any other address has its headers
ignored, so the server sees plain HTTP and builds `http://` OIDC callback URLs,
which the provider rejects.
