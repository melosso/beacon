## Configuration

Depending on your environment, these settings are changed in your `.env`, `docker-compose.yml` or `appsettings.json` file.

Source precedence, highest first:

1. Environment variables
2. `.env` in the binary folder, then `.env` in the working directory. A line is skipped if that variable is already set
3. .NET user secrets (Development only)
4. `appsettings.{Environment}.json`
5. `appsettings.json`

Within one source, the name order in [Naming](#naming) applies. Name order is evaluated after sources merge: a real `SigningKey` loses to `SIGNING_KEY` from `.env`. Use one spelling per setting.

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

Generate a `.env` with the required keys (gitignored):

```bash
cd Source/Beacon
if [ -f .env ]; then echo ".env already exists. Aborting."; else ADMIN_KEY=$(openssl rand -base64 48 | tr -d '\n'); printf "SIGNING_KEY=%s\nENCRYPTION_KEY=%s\nPEPPER=%s\nADMIN_API_KEY=%s\n" "$(openssl rand -base64 32)" "$(openssl rand -base64 32)" "$(openssl rand -base64 32)" "$ADMIN_KEY" > .env && chmod 600 .env && echo "Your X-Api-Key is: $ADMIN_KEY"; fi
```

Beacon never writes a value read from an environment variable or `.env` back to `appsettings.json`.

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

In Production, Beacon encrypts `CONNECTION_STRING`, `SIGNING_KEY`, `ENCRYPTION_KEY`, `PEPPER` and `ADMIN_API_KEY` in `appsettings.json` on first run. Values read from environment variables or `.env` are not written.

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
| `ENFORCE_HTTPS` | Redirect HTTP to HTTPS and send HSTS headers. Set only if Beacon terminates TLS. Leave off behind a TLS proxy | false |
| `DISABLE_EMAIL_NOTIFICATIONS` | Disable all outgoing email for this instance. Not editable in the admin panel | false |
