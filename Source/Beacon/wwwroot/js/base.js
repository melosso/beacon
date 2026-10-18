// Shared by the login page and the admin shell. Requires /js/lit.js (html, render, nothing).

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
    const close = e.target.matches?.("dialog[data-click]") && e.target.dataset.click.split(" ")[0];
    if (!close) return;
    e.preventDefault();
    window[close]();
  },
  true,
);

function openModal(id) {
  const dialog = document.getElementById(id);
  if (!dialog.open) dialog.showModal();
}

function closeModal(id) {
  document.getElementById(id)?.close();
}

function isModalOpen(id) {
  return !!document.getElementById(id)?.open;
}

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
      if (i > 0 && name.startsWith("--")) el.style.setProperty(name, decl.slice(i + 1).trim());
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
  document.getElementById("apiKeyLabel").textContent = isPassword ? "Password" : "API key";
  secret.autocomplete = isPassword ? "current-password" : "off";
  document.getElementById("authOverlayDesc").textContent = isPassword
    ? "Enter your username and password to sign in."
    : "Enter your API key to sign in.";
  document.getElementById("authToggleBtn").textContent = isPassword ? "Use API key" : "Use password";
}

function setupAuthOverlay() {
  const isBoth = AUTH_METHOD === "both";
  if (isBoth) {
    const saved = localStorage.getItem("beacon_auth_preference");
    if (saved === "password" || saved === "apikey") _authFormMode = saved;
    _applyAuthFormMode(_authFormMode === "password");
  } else if (AUTH_METHOD === "user" || AUTH_METHOD === "api") {
    _applyAuthFormMode(AUTH_METHOD === "user");
  }
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
  localStorage.setItem("beacon_auth_preference", _authFormMode);
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

function initLoginPage() {
  const overlay = document.getElementById("loginOverlay");
  if (!overlay) return;
  setupAuthOverlay();
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
  try { dismissed = localStorage.getItem(PROMO_DISMISSED_KEY); } catch {}
  bar.classList.toggle("is-visible", dismissed !== bar.textContent.trim());
}

function dismissPromoBar() {
  const bar = document.getElementById("promoBar");
  if (!bar) return;
  try { localStorage.setItem(PROMO_DISMISSED_KEY, bar.textContent.trim()); } catch {}
  bar.classList.remove("is-visible");
}

bindActions();
showPromoBar();
initLoginPage();
