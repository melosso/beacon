## Configuration

Depending on your environment, these settings are changed in your `.env`, `docker-compose.yml` or `appsettings.json` file.

### Core Settings

| Variable | Purpose | Default |
|----------|---------|---------|
| `Beacon__DatabaseProvider` | sqlite, sqlserver, postgres, mysql | sqlite |
| `Beacon__ConnectionString` | Database connection string | Data Source=Beacon.db |
| `Beacon__SigningKey` | HMAC signing key (base64, 32 bytes) | **Required** |
| `Beacon__EncryptionKey` | AES-256 encryption key (base64, 32 bytes) | **Required** |
| `Beacon__Pepper` | Email hashing pepper | **Required** |
| `Beacon__AdminApiKey` | API key for authenticated endpoints | **Required** |
| `Beacon__TokenExpiryDays` | Default token validity period | 30 |

### Host-Based Routing

When deploying behind a reverse proxy (nginx, Traefik, Caddy), use host-based routing to separate public API and admin traffic on different subdomains:

| Variable | Purpose | Example |
|----------|---------|---------|
| `Beacon__ApiHosts` | Hosts for public API access | beacon-api.example.com |
| `Beacon__AdminHosts` | Hosts for admin panel access | beacon-admin.example.com |
| `Beacon__AllowedOrigins` | Additional CORS origins | https://app.example.com |
| `Beacon__TrustForwardedHeaders` | Trust X-Forwarded-* headers from proxy | true |
| `Beacon__PublicUrl` | Override the base URL used in confirmation email links | https://beacon-api.example.com |

> When using double opt-in emails, Beacon builds confirmation links using the first entry of `Beacon__ApiHosts` (prefixed with `https://`). `Beacon__PublicUrl` is only needed when the public URL cannot be derived from `ApiHosts`. So, for example, in port-based deployments without a configured hostname, or when the external URL differs from the API host (CDN, custom domain).

### Port-Based Routing

When `ApiHosts`/`AdminHosts` are not configured, Beacon uses the following ports that can be overridden by changing the following variables:

| Variable | Purpose | Default |
|----------|---------|---------|
| `Beacon__ApiPort` | Port for public API endpoints | 5000 |
| `Beacon__AdminPort` | Port for admin panel and OpenAPI docs | 5001 |

You may need to combine both the host- and port-based variables when working with a reverse proxy (e.g. Cloudflare Tunnels or Pangolin).

### Other settings

You can configure the following additional, completely optional settings:

| Variable | Purpose | Default |
|----------|---------|---------|
| `Beacon__UserAuthentication` | Allow user authentication, API tokens, or both (`user`, `api`, `both`). Leave blank to only allow `Beacon__AdminApiKey` | both |