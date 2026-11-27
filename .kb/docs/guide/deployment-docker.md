---
title: Deploying with Docker
description: "Run Beacon with Docker Compose"
---

# Deploying with Docker

## Prerequisites

- [Docker Engine](https://www.docker.com/get-started) with Compose support

## Installation

### 1. Generate the keys

Docker Compose reads `.env` at startup. The command does not overwrite an existing `.env`:

```bash
if [ -f .env ]; then echo ".env already exists. Aborting."; else ADMIN_KEY=$(openssl rand -base64 48 | tr -d '\n'); printf "SIGNING_KEY=%s\nENCRYPTION_KEY=%s\nPEPPER=%s\nADMIN_API_KEY=%s\n" "$(openssl rand -base64 32)" "$(openssl rand -base64 32)" "$(openssl rand -base64 32)" "$ADMIN_KEY" > .env && chmod 600 .env && echo "Your API key is: $ADMIN_KEY"; fi
```

### 2. Create the compose file

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

Each release includes a `docker-compose.yml` pinned to the release version.

### 3. Start the container

```bash
docker compose up -d
```

### 4. Verify

```bash
curl http://localhost:5000/health
```

The admin panel is available at `http://localhost:5001`. Sign in with `ADMIN_API_KEY`.
