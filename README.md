# 🌌 Beacon

[![License](https://img.shields.io/badge/license-AGPL%203.0-blue)](LICENSE)
[![Last commit](https://img.shields.io/github/last-commit/melosso/beacon)](https://github.com/melosso/beacon/commits/main)
[![Latest Release](https://img.shields.io/github/v/release/melosso/beacon)](https://github.com/melosso/beacon/releases/latest)

This is **Beacon**, a lightweight service for handling email consent and opt-outs—no bloat, no dependencies. Built on .NET 10, it works independently of your CRM, ERP, or marketing tools. Generate secure, temporary opt-out links, validate them instantly, and let other systems check consent status via a clean API.

![Screenshot of Beacon](https://github.com/melosso/beacon/blob/main/.github/images/screenshot.webp?raw=true)

> Try it out! Feel free to play around in our live demo instance, available on [beacon-demo.melosso.com](https://beacon-demo.melosso.com). Use `Beacon-Api-Key` as your access token.

## What is Beacon?

Beacon **centralizes consent and communication preferences**, so your other systems don’t have to. Organize permissions into **Buckets** (e.g., newsletters, campaigns) and let recipients manage their preferences via secure, embeddable links.

### **How It Works**
Beacon handles consent for your automation tools, simple and efficient:

- Generates cryptographically signed opt-out URLs.
- Validates permission changes without database lookups (unless configured otherwise).
- Keeps your sending systems lightweight, no complicated logic required.

### **Built for Your Workflow**
Beacon adapts to your setup:

- Supports **SQLite, SQL Server, PostgreSQL, and MySQL**.
- Admin panel for managing consent buckets and records.
- Optional double opt-in confirmations.
- Form builder for signup pages.
- Granular permissions and security (encrypted data, hashed emails, rate limiting).

## Getting Started

We've prepared two methods to deploy Beacon. It's up to you to choose your preferred method:

### Docker Compose (Recommended)
```yaml
services:
  beacon:
    image: ghcr.io/melosso/beacon:latest
    ports:
      - "5000:5000"  # Public API
      - "5001:5001"  # Admin panel
    volumes:
      - beacon_data:/app/data    # Database storage
      - beacon_core:/app/.core   # Encryption keys
    environment:
      # Core settings (required)
      - Beacon__SigningKey=${BEACON_SIGNING_KEY}
      - Beacon__EncryptionKey=${BEACON_ENCRYPTION_KEY}
      - Beacon__Pepper=${BEACON_PEPPER}
      - Beacon__AdminApiKey=${BEACON_ADMIN_API_KEY}
      - Beacon__ConnectionString=Data Source=/app/data/Beacon.db

      # Port-based routing (default, no reverse proxy)
      - Beacon__ApiPort=5000
      - Beacon__AdminPort=5001

      # Host-based routing (for reverse proxy deployments)
      # - Beacon__ApiHosts=beacon-api.example.com
      # - Beacon__AdminHosts=beacon-admin.example.com
      # - Beacon__AllowedOrigins=https://app.example.com
      # - Beacon__TrustForwardedHeaders=true

volumes:
  beacon_data:
  beacon_core:
```

```bash
# Create the .env file
[ -f .env ] && echo ".env already exists! Aborting." && exit 1; ADMIN_KEY=$(openssl rand -base64 48 | tr -d '\n'); ENC_KEY=$(openssl rand -base64 32); printf "BEACON_SIGNING_KEY=%s\nBEACON_ENCRYPTION_KEY=%s\nBEACON_PEPPER=%s\nBEACON_ADMIN_API_KEY=%s\n" "$(openssl rand -base64 32)" "$ENC_KEY" "$(openssl rand -base64 32)" "$ADMIN_KEY" > .env && echo "Your X-Api-Key is: $ADMIN_KEY"

# Start the container
docker compose up -d
```

Access the Admin panel at **http://localhost:5001** and API at **http://localhost:5000**.

### Windows Installation

Download the latest release from [Releases](https://github.com/melosso/beacon/releases).

1. **Install .NET 10 Runtime:**
```powershell
winget install --id Microsoft.DotNet.Runtime.10 -e
```

2. **Set encryption key:**
```powershell
$bytes = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes); [Environment]::SetEnvironmentVariable("BEACON_ENCRYPTION_KEY", [Convert]::ToBase64String($bytes), "Machine")
```

3. **Install service:**
```powershell
.\Beacon.bat install
.\Beacon.bat start
```

4. Open browser → **http://localhost:5000** / **http://localhost:5001**

On first run, sensitive configuration values in `appsettings.json` will be automatically encrypted. You should, ofcourse, safely store your API key to keep access to the admin panel too.

---

For production deployments with host-based routing, see the [Configuration](#configuration) section.

## How to Use

Beacon will provide you a simple, non-customizable API that does one thing: securely store permissions for an e-mail address in a bucket. Your application can use this API to create a new permission state in the bucket–and return a token. This JWT-token contains all data, allowing the user to access its data without putting load on the database:

```bash
https://beacon.acme-corporation.com/u/v1.eyJiIjoicTEtY2FtcGFpZ24iLCJlIjoia...
```

You can incorporate this in your newsletters, system notifications, or anything you'd like – allowing your user to configure their permissions in decentralized system and keeping them outside of your data source:

![Screenshot of Permissions](https://github.com/melosso/beacon/blob/main/.github/images/screenshot2.webp?raw=true)

## API-first

As Beacon is an API-first platform, all consent management operations should be handled programmatically. While manual execution via the web UII is possible, integration typically involves automating these calls within your specific workflow. The first step requires creating a permission state for an email address in a bucket–which triggers the automatic creation of the target bucket if it is not already present. 

#### Generate Token
Creates consent records and returns a signed opt-out token (`[{"token":"<signed_jwt>"}]`).


```bash
curl -X POST http://localhost:5000/api/tokens/generate \
  -H "X-Api-Key: your-api-key" \
  -H "Content-Type: application/json" \
  -d '[{
    "bucket": "q1-campaign",
    "email": "user@example.com",
    "permissions": {
      "newsletter": true,
      "marketing": false
    }
  }]'
```

Response is an array of `[{"token":"<signed_token>","doubleOptIn":false}]`. Access the first element for single-item requests.

If you're planning on updating the permission record after insertion, may want to use configure `skipPermissionUpdate` to prevent overwriting (user) updated permissions. 

#### Process Opt-Out
User clicks the token link to update preferences.

```
GET /u/{token}
```

#### Check Consent
Query consent status before sending email.

```bash
curl -X POST http://localhost:5000/api/consent/check \
  -H "X-Api-Key: your-api-key" \
  -H "Content-Type: application/json" \
  -d '{"bucket": "q1-campaign", "email": "user@example.com", "permission": "newsletter"}'
```

#### Override Consent
```bash
curl -X POST http://localhost:5000/api/consent/override \
  -H "X-Api-Key: your-api-key" \
  -H "Content-Type: application/json" \
  -d '{"bucket": "q1-campaign", "email": "user@example.com", "permission": "newsletter", "status": "OptedIn"}'
```

#### Delete Bucket
```bash
curl -X DELETE http://localhost:5000/api/admin/buckets/q1-campaign \
  -H "X-Api-Key: your-api-key"
```

#### Generate Token with Optional Features
```bash
# Supported languages: en (default), de, fr, nl, pl, es
curl -X POST http://localhost:5000/api/tokens/generate \
  -H "X-Api-Key: your-api-key" \
  -H "Content-Type: application/json" \
  -d '[{
    "bucket": "q1-campaign",
    "email": "beige@example.com",
    "permissions": {
      "newsletter": true,
      "marketing": false
    },
    "customFields": {
      "externalId": "external-reference"
    }
    "allowReplay": false,
    "expiryDays": 30,
    "language": "nl",
    "skipPermissionUpdate": true
  }]'
```

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

### Generating Secure Keys

```bash
# Linux/macOS
openssl rand -base64 32

# PowerShell
$b = New-Object Byte[] 32; [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)
```

## License

Free for open source projects and personal use under the **AGPL 3.0** license. For more information, please see the [license](LICENSE) file.

## Contributing

Contributions are always welcome! Please submit issues and pull requests, using the templates we provided.
