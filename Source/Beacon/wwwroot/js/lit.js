// Exposes lit-html to the classic (deferred) scripts that follow this module.
import { html, render, nothing } from "/lib/lit-html/lit-html.js";
Object.assign(globalThis, { html, render, nothing });
