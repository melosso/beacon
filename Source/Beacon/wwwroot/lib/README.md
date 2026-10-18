# Vendored libraries

Third-party front-end code served from `/lib`. Files are unmodified upstream builds at the pinned version. No CDN is used at runtime.

| Library | Version | License | Used by |
|---|---|---|---|
| [lit-html](https://lit.dev) | 3.3.3 | BSD-3-Clause | Admin UI and login page templates (`/js/lit.js`) |
| [@scalar/api-reference](https://github.com/scalar/scalar) | 1.75.0 | MIT | API reference page (`/openapi`) |

## Files

| File | Source | SHA-256 |
|---|---|---|
| `lit-html/lit-html.js` | https://cdn.jsdelivr.net/npm/lit-html@3.3.3/lit-html.js | `b878e7f95dec8a9b6e9b217faca6b6a11bad82ebc44c0caa77419ed83edd81f2` |
| `lit-html/LICENSE` | https://cdn.jsdelivr.net/npm/lit-html@3.3.3/LICENSE | `2b66f9390afde29edfe4314daff93f01fb182e1178053e228120178987e5db75` |
| `scalar/standalone.js` | https://cdn.jsdelivr.net/npm/@scalar/api-reference@1.75.0/dist/browser/standalone.js | `61145fca93f21c47384fa4533dfe4ddd9bb0a450482dfa873443f9fee37bc9e3` |
| `scalar/LICENSE` | https://raw.githubusercontent.com/scalar/scalar/main/LICENSE | `380cd0a6ad700e1f821f2a509f0dd9ff835041cee2d43daf5dedc1adb2bcc620` |

## Rules

- Formatters and linters must skip this directory. A reformatted file no longer matches its hash.
- Update: download the new version from the same source URL, replace the file, update version and hash in this file.
- Verify: `sha256sum lit-html/* scalar/*` from this directory.
