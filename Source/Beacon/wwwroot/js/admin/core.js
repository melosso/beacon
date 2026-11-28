// Served as /js/admin.js: Program.cs concatenates js/admin/*.js in a fixed order; add new files there.

document.getElementById("openapi-link").href =
  (typeof API_BASE !== "undefined" && API_BASE ? API_BASE : "") + "/openapi";
const MAX_SIDEBAR_BUCKETS = 10;
let currentBucket = "";
let currentView = "overview";
let auditCurrentPage = 1,
  auditPageSize = 25,
  auditTotalRecords = 0;
let auditFilterBucket = null,
  auditFilterIdentity = null,
  auditFilterActor = null;
let _auditClickTimer = null;
let currentBucketArchived = false;
let currentPage = 1;
let pageSize = 10;
let totalRecords = 0;
const _sortPrefs = JSON.parse(
  localStorage.getItem("beacon_sort_prefs") || "{}",
);
let sortBy = _sortPrefs.sortBy || "lastchanged";
let sortDir = _sortPrefs.sortDir || "desc";
let searchQuery = "";
let searchType = "email";
let buckets = [];

// SESSION STATE (token lives in HttpOnly cookie)
let currentUserRole = sessionStorage.getItem("beacon_user_role") || "admin";
let currentUsername = sessionStorage.getItem("beacon_username") || "";
let tokenExpiresAt = sessionStorage.getItem("beacon_jwt_exp")
  ? new Date(sessionStorage.getItem("beacon_jwt_exp"))
  : null;
let lastActivity = Date.now();
let refreshInterval = null;
let _redirectingToLogout = false;

// CENTRALIZED API REQUESTS
async function apiRequest(endpoint, options = {}) {
  const config = {
    ...options,
    credentials: "include", // Send the HttpOnly auth cookie with every request
    headers: { ...options.headers },
  };

  if (options.body && typeof options.body === "object") {
    config.headers["Content-Type"] = "application/json";
    config.body = JSON.stringify(options.body);
  }

  try {
    const res = await fetch(endpoint, config);

    if (res.status === 401) {
      if (!options.skipAuthRedirect && !_redirectingToLogout) {
        _redirectingToLogout = true;
        fetch("/api/admin/auth/logout", {
          method: "POST",
          credentials: "include",
        }).catch(() => {});
        clearSession();
        window.location.href = "/admin/logout?reason=expired";
      }
      return { ok: false, status: 401, data: null };
    }

    let data = null;
    const contentType = res.headers.get("content-type");
    if (contentType && contentType.includes("application/json")) {
      data = await res.json();
    }

    if (!res.ok) {
      const errorMsg = data?.error || `Request failed (${res.status})`;
      notify("error", "Request failed", errorMsg);
      return { ok: false, status: res.status, data };
    }

    return { ok: true, status: res.status, data };
  } catch (e) {
    const msg =
      e instanceof TypeError
        ? "Server unreachable. Check the connection and the server logs."
        : e.message || "Request failed. Retry.";
    notify("error", "Request failed", msg);
    return { ok: false, status: 0, data: null };
  }
}

async function testAuth() {
  const [bucketsResult, meResult] = await Promise.all([
    apiRequest("/api/admin/buckets", { skipAuthRedirect: true }),
    apiRequest("/api/admin/auth/me", { skipAuthRedirect: true }),
  ]);
  if (bucketsResult.ok) {
    buckets = bucketsResult.data || [];
    try {
      sessionStorage.setItem("beacon_buckets", JSON.stringify(buckets));
    } catch {}
    if (meResult.ok && meResult.data?.role) {
      currentUserRole = meResult.data.role;
      currentUsername = meResult.data.username || currentUsername;
      sessionStorage.setItem("beacon_user_role", currentUserRole);
      sessionStorage.setItem("beacon_username", currentUsername);
    }
    return true;
  }
  return false;
}

// TOKEN REFRESH (slides the HttpOnly cookie expiry)
function startTokenRefresh() {
  if (refreshInterval) clearInterval(refreshInterval);
  refreshInterval = setInterval(
    async () => {
      if (!tokenExpiresAt) return;
      const now = Date.now();
      const expiresIn = tokenExpiresAt.getTime() - now;
      const wasActiveRecently = now - lastActivity < 10 * 60 * 1000; // 10 min
      const expiresSoon = expiresIn < 15 * 60 * 1000; // 15 min

      if (wasActiveRecently && expiresSoon) {
        try {
          const result = await apiRequest("/api/admin/auth/refresh", {
            method: "POST",
            skipAuthRedirect: true,
          });
          if (result.ok && result.data?.expiresAt) {
            tokenExpiresAt = new Date(result.data.expiresAt);
            sessionStorage.setItem("beacon_jwt_exp", result.data.expiresAt);
          } else if (result.status === 401) {
            notify("error", "Session expired", "Sign in again.");
            window.location.href = "/admin/login";
          }
        } catch {
          /* network error, will retry next interval */
        }
      }
    },
    5 * 60 * 1000,
  ); // every 5 minutes
}

// INIT
async function init() {
  document.getElementById("tokenExpiry").value = DEFAULT_EXPIRY_DAYS;

  // Track user activity for refresh decisions
  ["mousemove", "keydown", "click", "scroll"].forEach((evt) =>
    document.addEventListener(
      evt,
      () => {
        lastActivity = Date.now();
      },
      { passive: true },
    ),
  );

  startTokenRefresh();

  // Restore cached buckets for instant render while we verify the session
  try {
    const cached = sessionStorage.getItem("beacon_buckets");
    if (cached) {
      buckets = JSON.parse(cached);
      renderBucketsSidebar();
    }
  } catch {}

  // Verify cookie session with server (also loads fresh buckets)
  const valid = await testAuth();
  if (!valid) {
    window.location.href = "/admin/login";
    return;
  }

  renderBucketsSidebar();
  setupUserAuthNav(currentUserRole);
  updateSidebarUser();
  restoreViewFromUrl();
  connectSSE();
  refreshWorkflow();
  loadServerSettings();
  console.log(
    "%c@melosso/beacon%c. Licensed under %cEUPL 1.2%c.",
    "color:#6f42c1;font-weight:bold",
    "",
    "color:#28a745;font-weight:bold",
    "",
  );
}

function restoreViewFromUrl() {
  const params = new URLSearchParams(window.location.search);
  const view = params.get("view");
  const bucket = params.get("bucket");
  const modal = params.get("modal");

  if (view === "bucket" && bucket) {
    sortBy = params.get("sort") || sortBy;
    sortDir = params.get("dir") || sortDir;
    currentPage = parseInt(params.get("page")) || 1;
    pageSize = parseInt(params.get("size")) || 10;
    searchQuery = params.get("q") || "";
    searchType = params.get("qtype") || "email";
    showBucket(bucket, false);
    if (modal === "options") showOptionsModal(false);
    if (modal === "error-detail") {
      const errorId = params.get("errorId");
      if (errorId)
        loadWebhookErrors(bucket).then(() => {
          const error = webhookErrorsCache.find((e) => e.id === errorId);
          if (error) openErrorDetail(error);
        });
    }
  } else if (view === "new-bucket") {
    showView("overview", false);
    showView("new-bucket", false);
  } else if (view === "new-token") {
    showView("overview", false);
    showView("new-token", false);
  } else if (view === "submissions") {
    showView("submissions", false);
  } else if (view === "submission-create") {
    showView("submission-create", false);
  } else if (view === "subscriptions") {
    subSearchQuery = params.get("q") || "";
    subSearchType = params.get("qtype") || "email";
    subSortBy = params.get("sort") || subSortBy;
    subSortDir = params.get("dir") || subSortDir;
    subCurrentPage = parseInt(params.get("page")) || 1;
    subPageSize = parseInt(params.get("size")) || 10;
    const identity = params.get("identity");
    if (identity) {
      showView("subscriptions", false, true);
      showIdentityDetails(identity, false);
    } else {
      showView("subscriptions", false);
    }
  } else if (view === "submission-edit") {
    const nlId = params.get("id");
    if (nlId) {
      editSubmissionForm(nlId, false);
    } else {
      showView("submissions", false);
    }
  } else if (view === "submission-embed") {
    const nlId = params.get("id");
    if (nlId) {
      showEmbedCode(nlId);
    } else {
      showView("submissions", false);
    }
  } else if (view === "submission-preview") {
    const nlId = params.get("id");
    const mode = params.get("mode") || "iframe";
    if (nlId) {
      nlCurrentFormId = nlId;
      showPreview(nlId, mode);
    } else {
      showView("submissions", false);
    }
  } else if (view === "settings") {
    const section = params.get("section") || "general";
    showView("settings", false);
    showSettingsSection(section, false);
  } else if (view === "workflow") {
    showView("workflow", false);
  } else if (view === "audit") {
    auditFilterBucket = params.get("bucket") || null;
    auditFilterIdentity = params.get("identity") || null;
    auditFilterActor = params.get("actor") || null;
    auditCurrentPage = parseInt(params.get("page") || "1");
    showView("audit", false);
  } else {
    showView("overview", false);
  }
  if (SHEET_VIEWS[modal]) showView(modal, false);
}

// BUCKET PICKER: one bucket field. opts: { onSelect(name), allLabel }; free text is allowed for new buckets.
function attachBucketPicker(input, opts = {}) {
  if (!input || input.dataset.bucketPicker) return;
  input.dataset.bucketPicker = "";
  const list = document.createElement("div");
  list.className = "autocomplete-dropdown";
  list.id = `${input.id}Suggestions`;
  list.setAttribute("role", "listbox");
  // Top layer: positions the list relative to the viewport, also inside transformed dialogs.
  list.setAttribute("popover", "manual");
  input.after(list);
  input.autocomplete = "off";
  input.setAttribute("role", "combobox");
  input.setAttribute("aria-autocomplete", "list");
  input.setAttribute("aria-controls", list.id);
  input.setAttribute("aria-expanded", "false");
  let options = [];
  let active = -1;

  const close = () => {
    list.classList.remove("open");
    if (list.matches(":popover-open")) list.hidePopover();
    input.setAttribute("aria-expanded", "false");
    input.removeAttribute("aria-activedescendant");
    active = -1;
  };
  const choose = (option) => {
    input.value = option.all ? "" : option.name;
    close();
    opts.onSelect?.(input.value);
  };
  const render = () => {
    const q = input.value.trim().toLowerCase();
    const matches = [...buckets]
      .filter((b) => b.name.includes(q) || formatPermission(b.name).toLowerCase().includes(q))
      .sort((a, b) => Number(!!a.isArchived) - Number(!!b.isArchived));
    options = [...(opts.allLabel && !q ? [{ all: true }] : []), ...matches.slice(0, 50)];
    if (!options.length || (matches.length === 1 && matches[0].name === q)) return close();
    paint(
      list,
      html`${options.map(
        (o, i) => html`<div
          class="autocomplete-item ${i === active ? "selected" : ""}"
          id="${list.id}-${i}"
          role="option"
          aria-selected=${i === active}
          @pointerdown=${(e) => {
            e.preventDefault();
            choose(o);
          }}
          @mouseenter=${() => {
            active = i;
            render();
          }}
        >
          ${
            o.all
              ? html`<div class="bucket-name">${opts.allLabel}</div>`
              : html`<div class="bucket-name">
                  ${formatPermission(o.name)}<span class="autocomplete-code">${o.name}</span>
                  ${o.isArchived ? html`<span class="status-badge status-badge--sm archived">Archived</span>` : nothing}
                </div>
                <div class="bucket-info">
                  ${formatNumber(o.totalEmails || 0)} records · ${(o.permissions || []).length} permissions
                </div>`
          }
        </div>`,
      )}`,
    );
    if (active >= 0) input.setAttribute("aria-activedescendant", `${list.id}-${active}`);
    if (!list.matches(":popover-open")) list.showPopover();
    positionDropdown(input, list);
    list.classList.add("open");
    input.setAttribute("aria-expanded", "true");
  };

  input.addEventListener("input", () => {
    active = -1;
    render();
  });
  input.addEventListener("focus", render);
  input.addEventListener("blur", close);
  input.addEventListener("keydown", (e) => {
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      e.preventDefault();
      if (!list.classList.contains("open")) return render();
      const step = e.key === "ArrowDown" ? 1 : -1;
      active = (active + step + options.length) % options.length;
      render();
      list.children[active]?.scrollIntoView({ block: "nearest" });
    } else if (e.key === "Enter" && active >= 0 && list.classList.contains("open")) {
      e.preventDefault();
      choose(options[active]);
    } else if (e.key === "Escape" && list.classList.contains("open")) {
      e.stopPropagation();
      close();
    }
  });
}

