---
title: Deploying on Linux
description: "Install Beacon on Linux as a systemd service"
---

# Deploying on Linux

## Release files

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

## Installation

### 1. Install the package

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

### 2. Record the API key

```bash
sudo grep ADMIN_API_KEY /opt/beacon/.env
```

### 3. Start the service

The package does not enable or start the service:

```bash
sudo systemctl enable --now beacon
```

### 4. Verify

```bash
curl http://localhost:5000/health
```

## Upgrade and removal

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

## Logs

```bash
sudo journalctl -u beacon -e
```
