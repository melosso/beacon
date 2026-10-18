// Shared by the login page and the admin shell. Requires /js/lit.js (html, render, nothing).

const ACTION_EVENTS = {
  click: "click",
  dblclick: "dblclick",
  change: "change",
  input: "input",
  focus: "focus",
  blur: "blur",
  enter: "keydown",
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

// Escapes interpolations like lit; replaces all children like innerHTML.
function paint(el, template) {
  const fragment = document.createDocumentFragment();
  render(template, fragment);
  el.replaceChildren(fragment);
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
  document.getElementById("toastContainer").appendChild(toast);
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
  const usernameInput = document.getElementById("usernameInput");
  const apiKeyInput = document.getElementById("apiKeyInput");
  const desc = document.getElementById("authOverlayDesc");
  const toggleBtn = document.querySelector("#authToggleBtn button");
  usernameInput.style.display = isPassword ? "" : "none";
  apiKeyInput.placeholder = isPassword ? "Password" : "API Key";
  apiKeyInput.setAttribute("aria-label", apiKeyInput.placeholder);
  apiKeyInput.autocomplete = isPassword ? "current-password" : "off";
  desc.textContent = isPassword
    ? "Enter your username and password to access the admin panel."
    : "Enter your personal API key to sign in.";
  paint(
    toggleBtn,
    isPassword
      ? html`<svg
            xmlns="http://www.w3.org/2000/svg"
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
            style="flex-shrink:0"
          >
            <circle cx="7.5" cy="15.5" r="5.5" />
            <path d="m21 2-9.6 9.6" />
            <path d="m15.5 7.5 3 3L22 7l-3-3" />
          </svg>
          Access Token`
      : html`<svg
            xmlns="http://www.w3.org/2000/svg"
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
            style="flex-shrink:0"
          >
            <circle cx="12" cy="8" r="4" />
            <path d="M20 21a8 8 0 0 0-16 0" />
          </svg>
          Password`,
  );
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
  document.getElementById("authToggleBtn").style.display = isBoth ? "" : "none";
  document.getElementById("authToggleSep").style.display = isBoth
    ? "flex"
    : "none";
}

function toggleAuthMode() {
  _authFormMode = _authFormMode === "password" ? "apikey" : "password";
  localStorage.setItem("beacon_auth_preference", _authFormMode);
  document.getElementById("authError").style.display = "none";
  document.getElementById("apiKeyInput").value = "";
  _applyAuthFormMode(_authFormMode === "password");
}

// Server sets the HttpOnly auth cookie; sessionStorage keeps non-sensitive UI state only.
async function authenticate() {
  const btn = document.getElementById("signInBtn");
  const secret = document.getElementById("apiKeyInput");
  let body;
  if (
    AUTH_METHOD === "user" ||
    (AUTH_METHOD === "both" && _authFormMode === "password")
  ) {
    const username = document.getElementById("usernameInput").value.trim();
    if (!username || !secret.value) {
      notify("warning", "Input Required", "Enter your username and password.");
      return;
    }
    body = { username, password: secret.value };
  } else {
    const apiKey = secret.value.trim();
    if (!apiKey) {
      notify("warning", "Input Required", "Enter an API key.");
      return;
    }
    body = { apiKey };
  }

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
      document.getElementById("authError").style.display = "block";
      secret.value = "";
      notify(
        "error",
        "Authentication Failed",
        res.status === 429
          ? "Too many attempts. Wait and retry."
          : "Invalid credentials.",
      );
      return;
    }
    const data = await res.json();
    sessionStorage.setItem("beacon_user_role", data.role || "admin");
    sessionStorage.setItem("beacon_username", data.username || "");
    if (data.expiresAt)
      sessionStorage.setItem("beacon_jwt_exp", data.expiresAt);
    window.location.href = "/admin";
  } catch {
    notify("error", "Network Error", "Could not connect to server.");
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
      ? "Session Expired"
      : "Signed Out";
    document.getElementById("authOverlayDesc").textContent = expired
      ? "Your session has expired. Sign in again to continue."
      : "You have been signed out.";
  }
  overlay.style.display = "flex";
}

bindActions();
initLoginPage();
