---
title: Configuration
description: "Beacon settings: sources, naming, core keys, host-based and port-based routing"
---

# Configuration

Settings are read from environment variables, `.env` and `appsettings.json`.

## Sources

Precedence, highest first:

1. Environment variables
2. `.env` in the binary folder, then `.env` in the working directory
3. .NET user secrets (Development)
4. `appsettings.{Environment}.json`
5. `appsettings.json`

Each setting accepts four names. If more than one name is set: the lowest number applies.

| Style | Example | Precedence |
|---|---|---|
| Canonical | `Beacon__SigningKey` | 1 |
| Prefixed screaming snake | `BEACON_SIGNING_KEY` | 2 |
| Screaming snake | `SIGNING_KEY` | 3 |
| Bare | `SigningKey` | 4 |

:::warning
Use one spelling per setting. Name precedence applies after sources merge: `SIGNING_KEY` from `.env` overrides `SigningKey` in `appsettings.json`.
:::

## Core settings

| Variable | Purpose | Default |
|---|---|---|
| `DATABASE_PROVIDER` | `sqlite`, `sqlserver`, `postgres`, `mysql` | `sqlite` |
| `CONNECTION_STRING` | Database connection string | `Data Source=Beacon.db` |
| `SIGNING_KEY` | HMAC signing key (base64, 32 bytes) | Required |
| `ENCRYPTION_KEY` | AES-256 encryption key (base64, 32 bytes) | Required |
| `PEPPER` | Email hashing pepper | Required |
| `ADMIN_API_KEY` | API key for authenticated endpoints | Required |
| `TOKEN_EXPIRY_DAYS` | Default token validity in days | `30` |

In Production, Beacon encrypts the required keys and `CONNECTION_STRING` in `appsettings.json` at first start. Values from environment variables or `.env` are never written to `appsettings.json`.

## Host-based routing

Host-based routing separates public API and admin traffic by subdomain. Use host-based routing with a reverse proxy (nginx, Traefik, Caddy).

| Variable | Purpose | Example |
|---|---|---|
| `API_HOSTS` | Hosts for the public API | `beacon-api.example.com` |
| `ADMIN_HOSTS` | Hosts for the admin panel | `beacon-admin.example.com` |
| `ALLOWED_ORIGINS` | Additional CORS origins | `https://app.example.com` |
| `TRUST_FORWARDED_HEADERS` | Trust `X-Forwarded-*` headers | `true` |
| `KNOWN_PROXIES` | Trusted proxy IPs or CIDRs. Defaults to private and loopback ranges | `10.0.0.0/8` |
| `PUBLIC_URL` | Base URL of confirmation email links. Defaults to `https://` plus the first `API_HOSTS` entry | `https://beacon-api.example.com` |

## Port-based routing

If `API_HOSTS` and `ADMIN_HOSTS` are not set: Beacon routes by port.

| Variable | Purpose | Default |
|---|---|---|
| `API_PORT` | Public API | `5000` |
| `ADMIN_PORT` | Admin panel and OpenAPI docs | `5001` |

## Other settings

| Variable | Purpose | Default |
|---|---|---|
| `USER_AUTHENTICATION` | `user`, `api` or `both`. Empty: `ADMIN_API_KEY` is the sole credential | `both` |
| `ENFORCE_HTTPS` | Redirect HTTP to HTTPS and send HSTS headers. Set if Beacon terminates TLS | `false` |
| `DISABLE_EMAIL_NOTIFICATIONS` | Disable all outgoing email | `false` |
