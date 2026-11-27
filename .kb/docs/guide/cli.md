---
title: Command line
description: "Manage accounts, sign-in providers, API keys and webhooks from the shell"
---

# Command line

The Beacon binary runs management commands in place of the web server. Commands read the same configuration as the server: `.env`, environment variables and `appsettings.json`. Run them from the directory Beacon runs in.

| Command | Action |
|---|---|
| `accounts list` | Lists accounts with role, state, linked provider and last sign-in |
| `accounts create <username> <password> [admin\|user]` | Creates an account. Default role: `admin`. Prints the account API key once |
| `accounts password <username> <password>` | Sets a password. Minimum 12 characters |
| `accounts promote\|demote <username>` | Sets the role to `admin` or `user` |
| `accounts enable\|disable <username>` | Allows or blocks sign-in |
| `accounts delete <username>` | Deletes the account |
| `accounts apikey <username>` | Replaces the account API key. Prints the new key once |
| `accounts providers` | Lists single sign-on providers |
| `accounts providers enable\|disable\|delete <key>` | Changes a provider. Delete unlinks its accounts |
| `apikeys list` | Lists API keys with state, permissions and last use |
| `apikeys enable\|disable\|delete <name or id>` | Changes an API key. Duplicate names require the ID |
| `webhooks list` | Lists webhooks per bucket |
| `webhooks enable\|disable <bucket>` | Starts or stops webhook requests for a bucket |
| `migrate` | Applies schema changes and exits |

The last enabled admin account cannot be demoted, disabled or deleted. `ADMIN_API_KEY` signs in without an account.

## Docker

```bash
docker compose exec beacon dotnet Beacon.dll accounts list
```

## Linux and Windows

```bash
dotnet Beacon.dll accounts password alice "a-new-long-password"
```

Exit code `0` means success. Exit code `1` means the command failed; the reason is written to standard error.
