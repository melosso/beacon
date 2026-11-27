---
title: Install
description: "Install Beacon with Docker, on Linux or on Windows Server"
---

# Install

Beacon is distributed as a container image, as Linux packages, and as Linux, macOS and Windows archives. Each release targets x64 and arm64.

::: cards 3
- [Docker](/guide/deployment-docker)
  Docker Compose file, keys and volumes.
- [Linux](/guide/deployment-linux)
  `.deb` or `.rpm` package as a systemd service.
- [Windows Server](/guide/deployment-windows)
  Release archive as a Windows service.
:::

Beacon requires four keys: `SIGNING_KEY`, `ENCRYPTION_KEY`, `PEPPER` and `ADMIN_API_KEY`. The `.env` file contains the keys.

:::warning
`ADMIN_API_KEY` grants full access to the API and the admin panel. Store the key in a secret store.
:::

## Ports

Beacon listens on all interfaces.

| Port | Purpose |
|---|---|
| `5000` | Public API and preference pages (`API_PORT`) |
| `5001` | Admin panel and OpenAPI docs (`ADMIN_PORT`) |

Restrict port `5001` to a private network with a firewall rule. HTTPS requires a reverse proxy: [Configuration](/guide/configuration#host-based-routing).

First token and consent check: [API](/guide/api).
