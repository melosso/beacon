---
title: Install
description: "Install Beacon with Docker, on Linux or on Windows Server"
---

# Install

Beacon is distributed as a container image, as Linux packages, and as Linux, macOS and Windows archives. Each release targets x64 and arm64.

::: cards 3
- [Docker](#docker)
  Docker Compose file, keys and volumes.
- [Linux](#linux)
  Package as a systemd service.
- [Windows Server](#windows-server)
  Release archive as a Windows service.
:::

Beacon requires four keys: `SIGNING_KEY`, `ENCRYPTION_KEY`, `PEPPER` and `ADMIN_API_KEY`. The `.env` file contains the keys.

:::warning
`ADMIN_API_KEY` grants full access to the API and the admin panel. Store the key in a secret store.
:::

## Docker

Generate `.env`. Docker Compose reads `.env` at startup:

```bash
if [ -f .env ]; then echo ".env already exists. Aborting."; else ADMIN_KEY=$(openssl rand -base64 48 | tr -d '\n'); printf "SIGNING_KEY=%s\nENCRYPTION_KEY=%s\nPEPPER=%s\nADMIN_API_KEY=%s\n" "$(openssl rand -base64 32)" "$(openssl rand -base64 32)" "$(openssl rand -base64 32)" "$ADMIN_KEY" > .env && chmod 600 .env && echo "Your API key is: $ADMIN_KEY"; fi
```

```yaml [docker-compose.yml]
services:
  beacon:
    image: ghcr.io/melosso/beacon:latest
    ports:
      - "5000:5000"  # Public API
      - "5001:5001"  # Admin panel
    volumes:
      - beacon_data:/app/data    # Database storage
      - beacon_core:/app/.core   # Encryption keys
    env_file: .env
    environment:
      - CONNECTION_STRING=Data Source=/app/data/Beacon.db
    restart: unless-stopped

volumes:
  beacon_data:
  beacon_core:
```

```bash
docker compose up -d
```

Each release includes a `docker-compose.yml` pinned to the release version.

## Linux

### Release files

| File | Architecture | .NET runtime |
|---|---|---|
| `beacon_<version>_amd64.deb` | x64 | Included |
| `beacon_<version>_arm64.deb` | arm64 | Included |
| `beacon-<version>-1.x86_64.rpm` | x64 | Included |
| `beacon-<version>-1.aarch64.rpm` | arm64 | Included |
| `Beacon-<tag>-linux-x64.tar.gz` | x64 | [ASP.NET Core Runtime 11](https://get.dot.net/11) required |
| `Beacon-<tag>-linux-arm64.tar.gz` | arm64 | [ASP.NET Core Runtime 11](https://get.dot.net/11) required |

`SHA256SUMS` contains the SHA-256 checksum of each release file:

```bash
sha256sum --check --ignore-missing SHA256SUMS
```

### Install the package

::: code-group

```bash [Debian / Ubuntu]
sudo apt install ./beacon_<version>_amd64.deb
```

```bash [RHEL / Fedora / openSUSE]
sudo dnf install ./beacon-<version>-1.x86_64.rpm
```

:::

| Path | Contents |
|---|---|
| `/opt/beacon/` | Application files and `appsettings.json` |
| `/opt/beacon/.env` | Generated keys, mode `600`. The first install creates the file. |
| `/usr/lib/systemd/system/beacon.service` | systemd unit |
| `beacon` | System user that runs the service and owns `/opt/beacon/` |

Record `ADMIN_API_KEY` from `/opt/beacon/.env`:

```bash
sudo grep ADMIN_API_KEY /opt/beacon/.env
```

The package does not enable or start the service:

```bash
sudo systemctl enable --now beacon
curl http://localhost:5000/health
```

### Upgrade and removal

Install the newer package with the install command. The package restarts a running service. `appsettings*.json` is a configuration file: `.deb` prompts if the release changes the file, `.rpm` writes the release version as `<file>.rpmnew`.

::: code-group

```bash [Debian / Ubuntu]
sudo apt remove beacon
```

```bash [RHEL / Fedora / openSUSE]
sudo dnf remove beacon
```

:::

Removal stops and disables the service. `.env`, the database and `log/` remain in `/opt/beacon/`.

### Logs

```bash
sudo journalctl -u beacon -e
```

## Windows Server

1. Download `Beacon-<tag>-win-x64.zip` from [Releases](https://github.com/melosso/beacon/releases) and extract the archive.
2. Install the .NET 11 runtime:

```powershell
winget install --id Microsoft.DotNet.RunTime.11 -e
```

3. Generate `.env` in the Beacon folder:

```powershell
if (Test-Path .env) { throw ".env already exists" }
function New-Key { $b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b) }
$adminKey = New-Key
"SIGNING_KEY=$(New-Key)", "ENCRYPTION_KEY=$(New-Key)", "PEPPER=$(New-Key)", "ADMIN_API_KEY=$adminKey" | Set-Content .env
Write-Host "Your API key is: $adminKey"
```

4. Install and start the service as Administrator:

```powershell
.\Beacon.bat install
.\Beacon.bat start
```

## Ports

Beacon listens on all interfaces.

| Port | Purpose |
|---|---|
| `5000` | Public API and preference pages (`API_PORT`) |
| `5001` | Admin panel and OpenAPI docs (`ADMIN_PORT`) |

A firewall rule restricts port `5001` to a private network. HTTPS requires a reverse proxy: [Configuration](/guide/configuration#host-based-routing).
