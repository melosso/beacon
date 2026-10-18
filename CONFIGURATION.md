## Configuration

Depending on your environment, these settings are changed in your `.env`, `docker-compose.yml` or `appsettings.json` file.

### Naming

Every setting below accepts four environment variable names:

| Style | Example | Precedence |
|-------|---------|------------|
| Canonical | `Beacon__SigningKey` | 1 |
| Prefixed screaming snake | `BEACON_SIGNING_KEY` | 2 |
| Screaming snake | `SIGNING_KEY` | 3 |
| Bare | `SigningKey` | 4 |

If more than one name is set: the lowest precedence number wins. Empty values are ignored. The examples use screaming snake (`SIGNING_KEY`, `CONNECTION_STRING`, `API_PORT`).

Unprefixed names are not accepted if they equal a name the .NET host reads (`URLS`, `PORT`, `HTTP_PORTS`, `HTTPS_PORTS`, `ASPNETCORE_*`, `DOTNET_*`). Use a prefixed name on hosts where unprefixed names collide with other software.

For local development, keep secrets out of the repository with .NET user secrets:

```bash
cd Source/Beacon
dotnet user-secrets set "Beacon:SigningKey"    "$(openssl rand -base64 32)"
dotnet user-secrets set "Beacon:EncryptionKey" "$(openssl rand -base64 32)"
dotnet user-secrets set "Beacon:Pepper"        "$(openssl rand -base64 32)"
dotnet user-secrets set "Beacon:AdminApiKey"   "$(openssl rand -base64 36 | tr -d '\n')"
```

These are stored outside the working tree, so they cannot be committed. Beacon also never writes back to `appsettings.json` in Development, and never writes a value that came from an environment variable, so your working tree stays clean.

### Core Settings

| Variable | Purpose | Default |
|----------|---------|---------|
| `DATABASE_PROVIDER` | sqlite, sqlserver, postgres, mysql | sqlite |
| `CONNECTION_STRING` | Database connection string | Data Source=Beacon.db |
| `SIGNING_KEY` | HMAC signing key (base64, 32 bytes) | **Required** |
| `ENCRYPTION_KEY` | AES-256 encryption key (base64, 32 bytes) | **Required** |
| `PEPPER` | Email hashing pepper | **Required** |
| `ADMIN_API_KEY` | API key for authenticated endpoints | **Required** |
| `TOKEN_EXPIRY_DAYS` | Default token validity period | 30 |

### Host-Based Routing

When deploying behind a reverse proxy (nginx, Traefik, Caddy), use host-based routing to separate public API and admin traffic on different subdomains:

| Variable | Purpose | Example |
|----------|---------|---------|
| `API_HOSTS` | Hosts for public API access | beacon-api.example.com |
| `ADMIN_HOSTS` | Hosts for admin panel access | beacon-admin.example.com |
| `ALLOWED_ORIGINS` | Additional CORS origins | https://app.example.com |
| `TRUST_FORWARDED_HEADERS` | Trust X-Forwarded-* headers from proxy | true |
| `KNOWN_PROXIES` | Proxy IPs or CIDRs trusted for X-Forwarded-* headers. Defaults to private and loopback ranges | 10.0.0.0/8 |
| `PUBLIC_URL` | Override the base URL used in confirmation email links | https://beacon-api.example.com |

> When using double opt-in emails, Beacon builds confirmation links using the first entry of `API_HOSTS` (prefixed with `https://`). `PUBLIC_URL` is only needed when the public URL cannot be derived from `ApiHosts`. So, for example, in port-based deployments without a configured hostname, or when the external URL differs from the API host (CDN, custom domain).

### Port-Based Routing

When `ApiHosts`/`AdminHosts` are not configured, Beacon uses the following ports that can be overridden by changing the following variables:

| Variable | Purpose | Default |
|----------|---------|---------|
| `API_PORT` | Port for public API endpoints | 5000 |
| `ADMIN_PORT` | Port for admin panel and OpenAPI docs | 5001 |

You may need to combine both the host- and port-based variables when working with a reverse proxy (e.g. Cloudflare Tunnels or Pangolin).

### Other settings

You can configure the following additional, completely optional settings:

| Variable | Purpose | Default |
|----------|---------|---------|
| `USER_AUTHENTICATION` | Allow user authentication, API tokens, or both (`user`, `api`, `both`). Leave blank to only allow `ADMIN_API_KEY` | both |