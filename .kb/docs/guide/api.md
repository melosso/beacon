---
title: API
description: "Generate opt-out tokens, check consent and override consent through the Beacon REST API"
---

# API

Authenticated endpoints require the `X-Api-Key` header. The OpenAPI reference is available on the admin port (`ADMIN_PORT`).

## Generate tokens

`POST /api/tokens/generate` creates consent records and returns one signed token per item.

```bash
curl -X POST http://localhost:5000/api/tokens/generate \
  -H "X-Api-Key: your-api-key" \
  -H "Content-Type: application/json" \
  -d '[{
    "bucket": "q1-campaign",
    "email": "user@example.com",
    "name": "Jane Doe",
    "permissions": { "newsletter": true, "marketing": false }
  }]'
```

Response: `[{"token":"<signed_token>","doubleOptIn":false}]`.

Optional fields:

| Field | Purpose |
|---|---|
| `customFields` | Key-value pairs stored with the record, e.g. `externalId` |
| `allowReplay` | If `false`: the token link is single-use |
| `expiryDays` | Token validity in days. Defaults to `TOKEN_EXPIRY_DAYS` (30) |
| `language` | Preference page language: `en` (default), `de`, `fr`, `nl`, `pl`, `es`, `it`, `pt`, `ja` |
| `skipPermissionUpdate` | If `true`: existing permissions are not overwritten |

## Opt-out link

`GET /u/{token}` opens the preference page. Newsletters and notifications include the link:

```
https://beacon.example.com/u/v1.eyJiIjoicTEtY2FtcGFpZ24iLCJlIjoia...
```

## Check consent

`POST /api/consent/check` returns the consent status of one permission. Call the endpoint before a send.

```bash
curl -X POST http://localhost:5000/api/consent/check \
  -H "X-Api-Key: your-api-key" \
  -H "Content-Type: application/json" \
  -d '{"bucket": "q1-campaign", "email": "user@example.com", "permission": "newsletter"}'
```

## Override consent

`POST /api/consent/override` sets the consent status of one permission.

```bash
curl -X POST http://localhost:5000/api/consent/override \
  -H "X-Api-Key: your-api-key" \
  -H "Content-Type: application/json" \
  -d '{"bucket": "q1-campaign", "email": "user@example.com", "permission": "newsletter", "status": "OptedIn"}'
```

## Delete a bucket

`DELETE /api/admin/buckets/{bucket}` deletes a bucket.

```bash
curl -X DELETE http://localhost:5000/api/admin/buckets/q1-campaign \
  -H "X-Api-Key: your-api-key"
```
