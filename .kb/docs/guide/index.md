---
title: What is Beacon?
description: "Beacon is a self-hosted service for email consent and opt-out management, independent of CRM, ERP and marketing platforms"
keywords: [Consent, Opt-Out, GDPR, Email, REST, .NET]
---

# What is Beacon?

Beacon is a self-hosted service for email consent and opt-out management. Beacon stores consent and communication preferences outside of sending systems. Sending systems require no consent logic.

## Building blocks

- **Buckets**: groups of permissions, e.g. `newsletter` and `marketing` for a `q1-campaign`. A token request for a bucket that does not exist creates the bucket.
- **Tokens**: signed, expiring JWTs. Each token contains the bucket, the email address and the permissions. The token link (`/u/{token}`) opens the preference page.
- **Consent records**: the permission state per email address and bucket. The API returns the state before a send.

## Features

- Databases: SQLite, SQL Server, PostgreSQL and MySQL.
- Admin panel for buckets and consent records.
- Optional double opt-in confirmation email.
- Form builder for signup pages.
- Preference pages in `en`, `de`, `fr`, `nl`, `pl`, `es`, `it`, `pt` and `ja`.

## Next steps

::: cards 3
- [Install Beacon](/guide/install)
  Docker, Linux or Windows Server.
- [Call the API](/guide/api)
  Generate tokens and check consent.
- [Live demo](https://beacon-demo.melosso.com/)
  Access token: `Beacon-Api-Key`.
:::
