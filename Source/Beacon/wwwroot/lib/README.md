# Vendored libraries

Third-party front-end code served from `/lib`. Files are unmodified upstream builds at the pinned version. No CDN is used at runtime.

| Library | Version | License | Used by |
|---|---|---|---|
| [lit-html](https://lit.dev) | 3.3.3 | BSD-3-Clause | Admin UI and login page templates (`/js/lit.js`) |
| [cally](https://github.com/WickyNilliams/cally) | 0.9.2 | MIT | Date fields (`input[data-date]`, `/js/base.js`) |
| [@floating-ui/dom](https://floating-ui.com) | 1.8.0 (core 1.8.0) | MIT | Tooltip positioning (`/js/tooltip.js`) |
| [@scalar/api-reference](https://github.com/scalar/scalar) | 1.78.1 | MIT | API reference page (`/openapi`) |

## Files

| File | Source | SHA-256 |
|---|---|---|
| `lit-html/lit-html.js` | https://cdn.jsdelivr.net/npm/lit-html@3.3.3/lit-html.js | `b878e7f95dec8a9b6e9b217faca6b6a11bad82ebc44c0caa77419ed83edd81f2` |
| `lit-html/LICENSE` | https://cdn.jsdelivr.net/npm/lit-html@3.3.3/LICENSE | `2b66f9390afde29edfe4314daff93f01fb182e1178053e228120178987e5db75` |
| `cally/cally.js` | https://cdn.jsdelivr.net/npm/cally@0.9.2/dist/cally.js | `4dcba6a3b8ec63b66ff2fbfc6cf47b7dee1eb4896eac89fe7487d82e28e15870` |
| `cally/LICENSE` | https://cdn.jsdelivr.net/npm/cally@0.9.2/LICENSE | `c48fd407090d6d752b0370d39db55bd750e6dfc678ee252d4c906bddf54132eb` |
| `floating-ui/floating-ui.core.umd.min.js` | https://cdn.jsdelivr.net/npm/@floating-ui/core@1.8.0/dist/floating-ui.core.umd.min.js | `65940d866a6b6d831394a4bbed99ed0a39330bac98b643386d9a2f87a1a1d5da` |
| `floating-ui/floating-ui.dom.umd.min.js` | https://cdn.jsdelivr.net/npm/@floating-ui/dom@1.8.0/dist/floating-ui.dom.umd.min.js | `61a46f943c4e99379eaf073447811aec7e8b8f120d2f646316e01b7694bc90a3` |
| `floating-ui/LICENSE` | https://cdn.jsdelivr.net/npm/@floating-ui/dom@1.8.0/LICENSE | `0e4c9a9b6c71019cbbea3bdc20b01223110a9035700f9c960c8fcbf78c2325ce` |
| `scalar/standalone.js` | https://cdn.jsdelivr.net/npm/@scalar/api-reference@1.78.1/dist/browser/standalone.js | `db9d00b7ee9259ef90554cbf8b6b0f671c9e0fe8540a33d845630fee68cbb966` |
| `scalar/LICENSE` | https://raw.githubusercontent.com/scalar/scalar/main/LICENSE | `380cd0a6ad700e1f821f2a509f0dd9ff835041cee2d43daf5dedc1adb2bcc620` |

## Rules

- Formatters and linters must skip this directory. A reformatted file no longer matches its hash.
- Update: download the new version from the same source URL, replace the file, update version and hash in this file.
- Verify: `sha256sum cally/* floating-ui/* lit-html/* scalar/*` from this directory.
