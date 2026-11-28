// Shared by the login page and the admin shell. Requires /js/lit.js (html, render, nothing).

// The admin UI is English: dates follow it, day-first with a 24-hour clock, regardless of the browser locale
const UI_LOCALE = "en-GB";

// Server stores UTC but may serialize without Z suffix to ensure UTC parsing
function parseServerDate(value) {
  const zoned = /(Z|[+-]\d{2}:?\d{2})$/.test(value);
  return new Date(zoned ? value : `${value}Z`);
}

const NUMBER_FORMAT = new Intl.NumberFormat();

function formatNumber(value) {
  return NUMBER_FORMAT.format(value);
}

function pageRangeText(start, end, total) {
  const n = formatNumber;
  return total === 0 ? "0 of 0" : `${n(start)}–${n(end)} of ${n(total)}`;
}

// PAGER: one table footer. o: { page, pageSize, total, sizes, onPage(n), onPageSize(n) }; the caller stores page state.
const PAGER_ICONS = {
  first: "m11 17-5-5 5-5M18 17l-5-5 5-5",
  prev: "m15 18-6-6 6-6",
  next: "m9 18 6-6-6-6",
  last: "m13 17 5-5-5-5M6 17l5-5-5-5",
};

function renderPager(el, o) {
  if (!el) return;
  const pages = Math.max(1, Math.ceil(o.total / o.pageSize));
  if (o.page > pages) {
    queueMicrotask(() => o.onPage(pages));
    return;
  }
  const page = Math.max(1, o.page);
  const start = o.total === 0 ? 0 : (page - 1) * o.pageSize + 1;
  const end = Math.min(page * o.pageSize, o.total);
  const go = (n) => {
    const target = Math.min(Math.max(1, Math.trunc(n) || 1), pages);
    if (target !== page) o.onPage(target);
  };
  const btn = (key, label, target, disabled) =>
    html`<button type="button" class="btn-icon btn-icon--sm" aria-label=${label} data-tooltip=${label} ?disabled=${disabled} @click=${() => go(target)}>
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d=${PAGER_ICONS[key]}/></svg>
    </button>`;
  paint(
    el,
    html`<span class="pagination-info">${pageRangeText(start, end, o.total)}</span>
      <div class="pagination-controls">
        <label class="page-size-selector">
          <span class="pager-word">Rows</span>
          <select class="page-size-input" aria-label="Rows per page" @change=${(e) => o.onPageSize(Number(e.target.value))}>
            ${(o.sizes || [10, 25, 50, 100]).map((n) => html`<option value=${n} ?selected=${n === o.pageSize}>${n}</option>`)}
          </select>
        </label>
        ${btn("first", "First page", 1, page <= 1)}
        ${btn("prev", "Previous page", page - 1, page <= 1)}
        <label class="page-jump">
          <span class="pager-word">Page</span>
          <input type="number" class="page-input" inputmode="numeric" min="1" max=${pages} aria-label="Page number"
            .value=${String(page)}
            @change=${(e) => go(Number(e.target.value))}
            @keydown=${(e) => e.key === "Enter" && go(Number(e.target.value))}>
          of ${formatNumber(pages)}
        </label>
        ${btn("next", "Next page", page + 1, page >= pages)}
        ${btn("last", "Last page", pages, page >= pages)}
      </div>`,
  );
}

// Stale-response guard: a call returns a check that is false once a newer call with the same key starts. Live refreshes and user paging overlap.
const _latestCalls = new Map();
function latestCall(key) {
  const id = (_latestCalls.get(key) ?? 0) + 1;
  _latestCalls.set(key, id);
  return () => _latestCalls.get(key) === id;
}

const ACTION_EVENTS = {
  click: "click",
  dblclick: "dblclick",
  change: "change",
  input: "input",
  focus: "focus",
  blur: "blur",
  enter: "keydown",
  submit: "submit",
};

function actionArg(token, el, event) {
  switch (token) {
    case "$el":
      return el;
    case "$event":
      return event;
    case "$value":
      return el.value;
    case "$checked":
      return el.checked;
    case "true":
      return true;
    case "false":
      return false;
    case "null":
      return null;
  }
  return /^-?\d+(\.\d+)?$/.test(token) ? Number(token) : token;
}

// Static markup uses data-<event>="fn arg…" in place of inline handlers, which the CSP blocks.
function bindActions(root = document) {
  for (const [attr, type] of Object.entries(ACTION_EVENTS)) {
    for (const el of root.querySelectorAll(`[data-${attr}]`)) {
      el.addEventListener(type, (event) => {
        if (attr === "enter") {
          if (event.key !== "Enter") return;
          event.preventDefault();
        }
        if (attr === "submit") event.preventDefault();
        if (attr === "click") {
          if (el.hasAttribute("data-self") && event.target !== el) return;
          if (el.hasAttribute("data-stop")) event.stopPropagation();
          if (el.matches('a[href="#"]')) event.preventDefault();
        }
        const [fn, ...args] = el.dataset[attr].split(" ");
        window[fn](...args.map((a) => actionArg(a, el, event)));
      });
    }
  }
  for (const el of root.querySelectorAll("[data-stop]:not([data-click])")) {
    el.addEventListener("click", (e) => e.stopPropagation());
  }
}

// Esc on a dialog runs its data-click close handler, which also resets view state.
document.addEventListener(
  "cancel",
  (e) => {
    const close =
      e.target.matches?.("dialog[data-click]") &&
      e.target.dataset.click.split(" ")[0];
    if (!close) return;
    e.preventDefault();
    window[close]();
  },
  true,
);

// UNSAVED CHANGES: one dirty-state guard for dialogs and views (rules in DESIGN.md, Unsaved changes).
const _guardBaselines = new WeakMap();
const _guardExtras = new Map();

function guardScope(scope) {
  return typeof scope === "string" ? document.getElementById(scope) : scope;
}

function guardSnapshot(scope) {
  const controls = scope.querySelectorAll(
    "[data-guard] :is(input, select, textarea):not([data-instant], [type=file])",
  );
  return JSON.stringify([
    [...controls].map((c) => (c.type === "checkbox" || c.type === "radio" ? c.checked : c.value)),
    _guardExtras.get(scope.id)?.() ?? null,
  ]);
}

function guardExtra(scopeId, fn) {
  _guardExtras.set(scopeId, fn);
}

function guardReset(scope) {
  const el = guardScope(scope);
  if (el) _guardBaselines.delete(el);
}

function isDirty(scope) {
  const el = guardScope(scope);
  const base = el && _guardBaselines.get(el);
  return !!base && base.snapshot !== guardSnapshot(el);
}

// The single gate for closing or leaving a scope with unsaved edits.
function confirmDiscard(scope, proceed) {
  const el = guardScope(scope);
  if (!isDirty(el)) return proceed();
  openConfirmModal(
    "Discard changes?",
    el.matches("dialog") ? "Edits in this panel are not saved." : "Edits on this page are not saved.",
    "Discard",
    () => {
      guardReset(el);
      proceed();
    },
  );
}

// URL of the page when its baseline was taken: back/forward restores it while the prompt is open.
function guardUrl(scope) {
  return _guardBaselines.get(guardScope(scope))?.url ?? location.href;
}

for (const type of ["focusin", "pointerdown"]) {
  document.addEventListener(
    type,
    (e) => {
      if (!e.target.closest?.("[data-guard]")) return;
      const scope = e.target.closest("dialog, .view");
      if (scope && !_guardBaselines.has(scope))
        _guardBaselines.set(scope, { snapshot: guardSnapshot(scope), url: location.href });
    },
    true,
  );
}

window.addEventListener("beforeunload", (e) => {
  const scopes = document.querySelectorAll("dialog[open], .view.active");
  if (
    [...scopes].some(isDirty) ||
    document.querySelector("dialog[open] [data-uncopied]:not([hidden])")
  )
    e.preventDefault();
});

document.addEventListener(
  "change",
  (e) => {
    const box = e.target;
    if (!box.matches?.("input[type=checkbox]")) return;
    const turningOn = box.checked;
    const feature = turningOn ? box.dataset.confirmOn : box.dataset.confirmOff;
    if (!feature) return;
    if (box.hasAttribute("data-confirmed")) {
      box.removeAttribute("data-confirmed");
      return;
    }
    e.stopImmediatePropagation();
    box.checked = !turningOn;
    openConfirmModal(
      `Turn ${turningOn ? "on" : "off"} ${feature}?`,
      box.dataset.confirmMessage ||
        "This stops the feature for every bucket and user until someone turns it back on.",
      turningOn ? "Turn on" : "Turn off",
      () => {
        box.checked = turningOn;
        box.setAttribute("data-confirmed", "");
        box.dispatchEvent(new Event("change", { bubbles: true }));
      },
    );
  },
  true,
);

function openModal(id) {
  const dialog = document.getElementById(id);
  if (dialog.open) return;
  guardReset(dialog);
  dialog.showModal();
}

function closeModal(id, force = false) {
  const dialog = document.getElementById(id);
  const uncopied = dialog?.querySelector("[data-uncopied]:not([hidden])");
  const closer = window[dialog?.dataset.click?.split(" ")[0]];
  const retry = () => (closer ? closer(true) : closeModal(id, true));
  if (!force && uncopied) {
    openConfirmModal(
      "Close without copying?",
      "The secret is shown once and cannot be shown again.",
      "Close",
      retry,
    );
    return false;
  }
  if (!force && isDirty(dialog)) {
    confirmDiscard(dialog, retry);
    return false;
  }
  uncopied?.removeAttribute("data-uncopied");
  guardReset(dialog);
  dialog?.close();
  return true;
}

function isModalOpen(id) {
  return !!document.getElementById(id)?.open;
}

const KEBAB_ICON = html`<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><circle cx="12" cy="5" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="12" cy="19" r="1"/></svg>`;

// Escapes interpolations like lit; replaces all children like innerHTML.
function paint(el, template) {
  const fragment = document.createDocumentFragment();
  render(template, fragment);
  applyVars(fragment);
  el.replaceChildren(fragment);
}

// CSP blocks style attributes: data-vars="--name:value;…" sets custom properties through the CSSOM.
function applyVars(root) {
  for (const el of root.querySelectorAll("[data-vars]")) {
    for (const decl of el.dataset.vars.split(";")) {
      const i = decl.indexOf(":");
      const name = decl.slice(0, i).trim();
      if (i > 0 && name.startsWith("--"))
        el.style.setProperty(name, decl.slice(i + 1).trim());
    }
  }
}

const TOAST_ICONS = {
  success: html`<svg
    class="toast-icon"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="2"
    aria-hidden="true"
  >
    <path d="M20 6L9 17l-5-5" />
  </svg>`,
  error: html`<svg
    class="toast-icon"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="2"
    aria-hidden="true"
  >
    <circle cx="12" cy="12" r="10" />
    <line x1="15" y1="9" x2="9" y2="15" />
    <line x1="9" y1="9" x2="15" y2="15" />
  </svg>`,
  warning: html`<svg
    class="toast-icon"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="2"
    aria-hidden="true"
  >
    <path
      d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"
    />
    <line x1="12" y1="9" x2="12" y2="13" />
    <line x1="12" y1="17" x2="12.01" y2="17" />
  </svg>`,
};

function notify(type, title, message = "") {
  const toast = document.createElement("div");
  toast.className = `toast ${type}`;
  paint(
    toast,
    html` ${TOAST_ICONS[type] || TOAST_ICONS.error}
      <div class="toast-content">
        <div class="toast-title">${title}</div>
        ${message ? html`<div class="toast-message">${message}</div>` : nothing}
      </div>`,
  );
  const container = document.getElementById("toastContainer");
  container.appendChild(toast);
  // Re-show moves the popover above any modal dialog in the top layer.
  if (container.matches(":popover-open")) container.hidePopover();
  container.showPopover();
  setTimeout(() => {
    toast.classList.add("hiding");
    setTimeout(() => toast.remove(), 200);
  }, 4000);
}

const SESSION_KEYS = [
  "beacon_user_role",
  "beacon_username",
  "beacon_jwt_exp",
  "beacon_buckets",
  "beacon_user_id",
];

function clearSession() {
  SESSION_KEYS.forEach((k) => sessionStorage.removeItem(k));
}

// AUTH FORM
const AUTH_METHOD =
  typeof USER_AUTH_METHOD !== "undefined" ? USER_AUTH_METHOD : "";
let _authFormMode = "password"; // 'password' | 'apikey'; switchable only in 'both' mode

function _applyAuthFormMode(isPassword) {
  const username = document.getElementById("usernameInput");
  const secret = document.getElementById("apiKeyInput");
  document.getElementById("usernameGroup").hidden = !isPassword;
  username.disabled = !isPassword;
  document.getElementById("apiKeyLabel").textContent = isPassword
    ? "Password"
    : "API key";
  secret.autocomplete = isPassword ? "current-password" : "off";
  document.getElementById("authOverlayDesc").textContent = isPassword
    ? "Enter your credentials to sign in."
    : "Enter your API key to sign in.";
  document.getElementById("authToggleBtn").textContent = isPassword
    ? "Use API key"
    : "Use password";
}

function setupAuthOverlay() {
  const isBoth = AUTH_METHOD === "both";
  _applyAuthFormMode(isBoth || AUTH_METHOD === "user");
  document.getElementById("authToggleBtn").hidden = !isBoth;
  document.getElementById("authToggleSep").hidden = !isBoth;
}

function showAuthError(message) {
  const error = document.getElementById("authError");
  error.textContent = message;
  error.hidden = !message;
}

function toggleAuthMode() {
  _authFormMode = _authFormMode === "password" ? "apikey" : "password";
  showAuthError("");
  document.getElementById("apiKeyInput").value = "";
  _applyAuthFormMode(_authFormMode === "password");
}

// Server sets the HttpOnly auth cookie; sessionStorage keeps non-sensitive UI state only.
async function authenticate() {
  const btn = document.getElementById("signInBtn");
  const secret = document.getElementById("apiKeyInput");
  const username = document.getElementById("usernameInput");
  const body = username.disabled
    ? { apiKey: secret.value.trim() }
    : { username: username.value.trim(), password: secret.value };

  showAuthError("");
  btn.classList.add("is-loading");
  btn.disabled = true;
  try {
    const res = await fetch("/api/admin/auth", {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
    if (!res.ok) {
      secret.value = "";
      showAuthError(
        res.status === 429
          ? "Too many attempts. Wait a minute and retry."
          : "Sign-in failed. Check your credentials and retry.",
      );
      secret.focus();
      return;
    }
    const data = await res.json();
    sessionStorage.setItem("beacon_user_role", data.role || "admin");
    sessionStorage.setItem("beacon_username", data.username || "");
    if (data.expiresAt)
      sessionStorage.setItem("beacon_jwt_exp", data.expiresAt);
    window.location.href = "/admin";
  } catch {
    showAuthError("Server unreachable. Check your connection and retry.");
  } finally {
    btn.classList.remove("is-loading");
    btn.disabled = false;
  }
}

async function logout() {
  try {
    await fetch("/api/admin/auth/logout", {
      method: "POST",
      credentials: "include",
    });
  } catch {
    /* Session cookie expires on its own. */
  }
  clearSession();
  window.location.href = "/admin/logout";
}

const SSO_ERRORS = {
  failed: "Sign-in failed. Retry, or ask an admin to check the provider settings.",
  denied: "Sign-in was cancelled at the provider.",
  no_account: "No account matches this sign-in. Ask an admin to add your email address as a user.",
  inactive: "This account is disabled. Ask an admin to enable it.",
};

async function loadSsoProviders() {
  const reason = new URLSearchParams(location.search).get("sso");
  if (reason) showAuthError(SSO_ERRORS[reason] || SSO_ERRORS.failed);
  try {
    const res = await fetch("/api/admin/auth/providers");
    const providers = res.ok ? await res.json() : [];
    const el = document.getElementById("ssoProviders");
    if (!providers.length) return;
    render(
      html`${document.getElementById("authToggleSep").hidden ? html`<div class="auth-divider">Or</div>` : nothing}
        ${providers.map(
          (p) =>
            html`<a class="btn btn-outline btn-block" href="/api/admin/auth/oidc/${encodeURIComponent(p.slug)}/start">Continue with ${p.name}</a>`,
        )}`,
      el,
    );
    el.hidden = false;
  } catch {}
}

function initLoginPage() {
  const overlay = document.getElementById("loginOverlay");
  if (!overlay) return;
  setupAuthOverlay();
  loadSsoProviders();
  if (location.pathname === "/admin/logout") {
    const expired =
      new URLSearchParams(location.search).get("reason") === "expired";
    document.title = "Signed out · Beacon";
    document.getElementById("loginHeading").textContent = expired
      ? "Session expired"
      : "Signed out";
    document.getElementById("authOverlayDesc").textContent = expired
      ? "Your session expired. Sign in to continue."
      : "You are signed out. Sign in to continue.";
  }
  overlay.hidden = false;
}

const PROMO_DISMISSED_KEY = "beacon_promo_dismissed";

// Dismissal is keyed on the announcement text: an edited announcement shows again.
function showPromoBar() {
  const bar = document.getElementById("promoBar");
  if (!bar) return;
  let dismissed = null;
  try {
    dismissed = localStorage.getItem(PROMO_DISMISSED_KEY);
  } catch {}
  bar.classList.toggle("is-visible", dismissed !== bar.textContent.trim());
}

function dismissPromoBar() {
  const bar = document.getElementById("promoBar");
  if (!bar) return;
  try {
    localStorage.setItem(PROMO_DISMISSED_KEY, bar.textContent.trim());
  } catch {}
  bar.classList.remove("is-visible");
}

// Same beacon_settings.theme value as the admin Appearance setting and js/theme.js.
function readSettings() {
  try {
    return JSON.parse(localStorage.getItem("beacon_settings") || "{}");
  } catch {
    return {};
  }
}

// data-theme is the applied choice, valid without storage. No attribute: the system preference decides.
function effectiveTheme() {
  const applied = document.documentElement.getAttribute("data-theme");
  if (applied === "light" || applied === "dark") return applied;
  return matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

function cycleTheme() {
  const settings = readSettings();
  settings.theme = effectiveTheme() === "dark" ? "light" : "dark";
  document.documentElement.setAttribute("data-theme", settings.theme);
  try {
    localStorage.setItem("beacon_settings", JSON.stringify(settings));
  } catch {}
  syncThemeToggle();
}

function syncThemeToggle() {
  const btn = document.getElementById("themeToggle");
  if (!btn) return;
  const theme = effectiveTheme();
  btn.dataset.mode = theme;
  btn.setAttribute(
    "aria-label",
    theme === "dark" ? "Switch to light theme" : "Switch to dark theme",
  );
  btn.dataset.tooltip = btn.getAttribute("aria-label");
}

matchMedia("(prefers-color-scheme: dark)").addEventListener(
  "change",
  syncThemeToggle,
);

function isoDate(d) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

function enhanceDateFields() {
  for (const input of document.querySelectorAll("input[data-date]")) {
    const field = document.createElement("div");
    field.className = "date-field";
    input.replaceWith(field);
    Object.assign(input, {
      type: "text",
      inputMode: "numeric",
      placeholder: "YYYY-MM-DD",
      autocomplete: "off",
    });
    input.pattern = "\\d{4}-\\d{2}-\\d{2}";
    const pickerId = `${input.id}-picker`;
    const close = () => document.getElementById(pickerId).hidePopover();
    const set = (value) => {
      input.value = value;
      input.dispatchEvent(new Event("change", { bubbles: true }));
      close();
      input.focus();
    };
    const onToggle = (e) => {
      if (e.newState !== "open") return;
      const rect = field.getBoundingClientRect();
      const pop = e.currentTarget;
      const below = rect.bottom + 4 + pop.offsetHeight <= innerHeight - 8;
      pop.style.top = `${below ? rect.bottom + 4 : Math.max(8, rect.top - pop.offsetHeight - 4)}px`;
      pop.style.left = `${Math.min(rect.left, innerWidth - pop.offsetWidth - 8)}px`;
      const cal = pop.querySelector("calendar-date");
      cal.value = /^\d{4}-\d{2}-\d{2}$/.test(input.value) ? input.value : "";
      cal.focusedDate = cal.value || isoDate(new Date());
      setHeading(cal, new Date(`${cal.focusedDate}T00:00`));
    };
    const setHeading = (cal, date) => {
      cal.querySelector('[slot="heading"]').textContent =
        date.toLocaleDateString(UI_LOCALE, { month: "long", year: "numeric" });
    };
    render(
      html`
      ${input}
      <button type="button" class="date-field-btn" popovertarget=${pickerId} aria-label="Choose date">
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4M8 2v4M3 10h18"/></svg>
      </button>
      <div class="date-popover" id=${pickerId} popover @toggle=${onToggle}>
        <calendar-date locale=${UI_LOCALE} first-day-of-week="1" @change=${(e) => set(e.target.value)} @focusday=${(e) => setHeading(e.currentTarget, new Date(e.detail))}>
          <span slot="heading"></span>
          <span slot="previous" aria-label="Previous month"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><path d="m15 18-6-6 6-6"/></svg></span>
          <span slot="next" aria-label="Next month"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><path d="m9 18 6-6-6-6"/></svg></span>
          <calendar-month></calendar-month>
        </calendar-date>
        <div class="date-popover-actions">
          <button type="button" class="btn btn-outline btn-sm" @click=${() => set(isoDate(new Date()))}>Today</button>
          <button type="button" class="btn btn-outline btn-sm" @click=${() => set("")}>Clear</button>
        </div>
      </div>`,
      field,
    );
  }
}

enhanceDateFields();
bindActions();
showPromoBar();
initLoginPage();
syncThemeToggle();
