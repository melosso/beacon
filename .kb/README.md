# Beacon Documentation

Beacon documentation, served by [Bark](https://github.com/hawkinslabdev/bark). Pages are Markdown files in `docs/`. Site configuration is defined in `docs/config.json`.

## Running locally

```bash
docker compose up -d
```

The site is available at `http://localhost:5992`.

## Layout

| Path | Purpose |
|---|---|
| `docs/config.json` | Site configuration: navigation, sidebar, branding, edit links |
| `docs/guide/` | Guides: introduction, installation, API, configuration |
| `wwwroot/` | Bark theme and favicon |

`.github/workflows/documentation-pages.yml` publishes the site to GitHub Pages on push to `main`.
