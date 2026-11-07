# Production OIDC Setup

> [!IMPORTANT]
> The files under `authelia/`, `docker-compose.yml`, and `appsettings.Development.json`
> are **not used in production**. Configure the server via environment variables.

## Overview

The server uses two OIDC schemes:

| Scheme     | Auth flow                    | Used by         |
| ---------- | ---------------------------- | --------------- |
| Cookie     | Authorization code → session | SPA (browser)   |
| JWT Bearer | Authorization code + PKCE    | Scalar API docs |

Either scheme satisfies the `authenticated` policy on protected endpoints.

## Environment Variables

```bash
Oidc__Authority              # Required. OIDC discovery URL
Oidc__ClientId               # Required. SPA client ID (confidential)
Oidc__ClientSecret           # Required. SPA client secret
Oidc__CallbackPath           # Required. Redirect URI path (e.g. /signin-oidc)
Login__ReturnUrl             # Required. Post-login redirect (e.g. https://app.example.com/)
OAuth2__AuthorizationUrl     # Optional. For Scalar OAuth2 flow
OAuth2__TokenUrl             # Optional. For Scalar OAuth2 flow
```

`OAuth2__AuthorizationUrl` and `OAuth2__TokenUrl` are only needed if you expose
the Scalar API docs in production.

## 1. Deploy an OIDC Provider

Any standards-compliant OIDC provider works — Authelia, Keycloak, Auth0, Authentik, Entra ID, etc.

### Authelia-specific

Adapt the dev config from `authelia/configuration.yml`:

- **Replace TLS certs** under `server.tls` with real certificates (e.g. Let's Encrypt).
- **Regenerate every secret.** The dev secrets are public. Generate 32-byte hex values:
  ```bash
  openssl rand -hex 32
  ```
  Replace: `session.secret`, `storage.encryption_key`,
  `identity_validation.reset_password.jwt_secret`, `identity_providers.oidc.hmac_secret`.
- **Regenerate the JWKS key pair:**
  ```bash
  openssl genpkey -algorithm RSA -out private.pem -pkeyopt rsa_keygen_bits:2048
  ```
  Paste the private key into `identity_providers.oidc.jwks[].key`.
- Replace the file-based user backend with LDAP (or another production backend).
- Set `session.cookies[].domain` to your production domain.
- Run behind a reverse proxy (nginx, Caddy) that terminates TLS, or configure TLS in Authelia directly.

## 2. Register OIDC Clients

Two clients are needed:

|                       | SPA                            | Scalar/API                   |
| --------------------- | ------------------------------ | ---------------------------- |
| **Client type**       | Confidential                   | Public (PKCE)                |
| **Grant types**       | `authorization_code`           | `authorization_code`         |
| **Response types**    | `code`                         | `code`                       |
| **Token auth method** | `client_secret_post`           | `none`                       |
| **Scopes**            | `openid profile email`         | `openid profile email`       |
| **Redirect URIs**     | `https://<domain>/signin-oidc` | `https://<domain>/scalar/v1` |

The SPA does not request `offline_access`: its session is a cookie, and an
expired cookie goes through the OIDC flow again, so it never uses a refresh
token. Requesting one would also make Authelia ask for consent on every login,
whatever the client's `consent_mode`.

The SPA redirect URI must match `Oidc__CallbackPath`.

## 3. Configure the Server

```bash
export Oidc__Authority=https://auth.example.com
export Oidc__ClientId=__PROJECT_KEBAB__
export Oidc__ClientSecret=<your-production-secret>
export Oidc__CallbackPath=/signin-oidc
export Login__ReturnUrl=https://app.example.com/
export OAuth2__AuthorizationUrl=https://auth.example.com/api/oidc/authorization
export OAuth2__TokenUrl=https://auth.example.com/api/oidc/token
```

Then start:

```bash
dotnet run --project server/src/__PROJECT_NAME__.Server
```

## Cookie Security (Non-Development)

When `ASPNETCORE_ENVIRONMENT` is not `Development`:

- `Cookie.SecurePolicy = Always`
- `Cookie.SameSite = Lax`
- `Cookie.HttpOnly = true`
- `RequireHttpsMetadata = true` for JWT bearer (only when `IsProduction()`)
- Self-signed cert validation is **not** bypassed

## Verify

1. `https://<domain>/login` → redirected to OIDC login
2. Authenticate → redirected back to SPA
3. `GET /api/<resource>` → returns data, not `401`
4. `https://<domain>/scalar/v1` → OAuth2 flow works

## Troubleshooting

| Symptom                                     | Likely cause                                   |
| ------------------------------------------- | ---------------------------------------------- |
| `401` on login redirect                     | `Oidc__CallbackPath` or redirect URI mismatch  |
| `Correlation failed` error                  | Cookies blocked (third-party / SameSite issue) |
| `invalid_client` from provider              | Wrong client secret or token auth method       |
| JWT bearer returns `401`                    | `RequireHttpsMetadata` + HTTP authority        |
| Self-signed cert rejected                   | Use a real certificate, not the dev one        |
| `Options.Authority` or `.ClientId` is blank | Missing or misnamed environment variable       |
