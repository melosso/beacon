// VIEWS
function updateUrl(params, replace = false) {
  const url = new URL(window.location);
  url.search = "";
  url.hash = "";
  for (const [key, value] of Object.entries(params)) {
    if (value) url.searchParams.set(key, value);
  }
  if (replace) {
    history.replaceState(null, "", url);
  } else {
    history.pushState(null, "", url);
  }
}

function syncBucketUrl(replace = false) {
  const params = { view: "bucket", bucket: currentBucket };
  if (sortBy !== "lastchanged") params.sort = sortBy;
  if (sortDir !== "desc") params.dir = sortDir;
  if (currentPage > 1) params.page = String(currentPage);
  if (pageSize !== 10) params.size = String(pageSize);
  if (searchQuery) params.q = searchQuery;
  if (searchType !== "email") params.qtype = searchType;
  updateUrl(params, replace);
}

function syncSubUrl(identity = null, replace = false) {
  const params = { view: "subscriptions" };
  if (identity) {
    params.identity = identity;
    if (subSearchQuery) params.q = subSearchQuery;
    if (subSearchType !== "email") params.qtype = subSearchType;
  } else {
    if (subSortBy !== "lastchanged") params.sort = subSortBy;
    if (subSortDir !== "desc") params.dir = subSortDir;
    if (subCurrentPage > 1) params.page = String(subCurrentPage);
    if (subPageSize !== 10) params.size = String(subPageSize);
    if (subSearchQuery) params.q = subSearchQuery;
    if (subSearchType !== "email") params.qtype = subSearchType;
  }
  updateUrl(params, replace);
}

function closeMobileNav() {
  const nav = document.getElementById("sidebar");
  if (nav?.matches(":popover-open")) nav.hidePopover();
}

function showView(view, pushState = true, skipLoad = false) {
  closeMobileNav();
  if (view === "users") {
    showView("settings", pushState, true);
    showSettingsSection("users", pushState);
    return;
  }
  if (view === "account") {
    showView("settings", pushState, true);
    showSettingsSection("account", pushState);
    return;
  }
  currentView = view;
  document
    .querySelectorAll(".view")
    .forEach((v) => v.classList.remove("active"));
  document
    .querySelectorAll(".nav-item")
    .forEach((n) => n.classList.remove("active"));

  // submission-edit reuses the create view DOM
  const viewId =
    view === "submission-edit" || view === "submission-embed"
      ? "view-submission-create"
      : `view-${view}`;
  const viewEl = document.getElementById(viewId);
  if (viewEl) viewEl.classList.add("active");

  document
    .querySelector(`.nav-item[data-view="${CSS.escape(view)}"]`)
    ?.classList.add("active");
  // Highlight submission nav when in create/edit
  if (
    view === "submission-create" ||
    view === "submission-edit" ||
    view === "submission-embed" ||
    view === "submission-preview"
  ) {
    document
      .querySelector('.nav-item[data-view="submissions"]')
      ?.classList.add("active");
  }

  if (pushState && view !== "subscriptions") updateUrl({ view });

  if (skipLoad) return;

  if (view === "overview") loadOverview();
  if (view === "submissions") loadSubmissionForms();
  if (view === "submission-create") initSubmissionWizard();
  if (view === "new-bucket") renderNewBucketPerms();
  if (view === "new-token")
    document.getElementById("tokenLanguage").value =
      appSettings.uiLanguage || "en";
  if (view === "settings") loadSettings();
  if (view === "workflow") loadWorkflowPage();
  if (view === "audit") loadAudit();
  if (view === "subscriptions") {
    loadIdentities(pushState);
  }
}

function showBucket(bucket, pushState = true) {
  closeMobileNav();
  if (isModalOpen("optionsModal"))
    closeOptionsModal();
  if (bucket !== currentBucket) {
    bucketSelectedHashes.clear();
    const bar = document.getElementById("bucketBulkBar");
    if (bar) bar.hidden = true;
  }
  currentBucket = bucket;
  currentView = "bucket";
  if (pushState) {
    currentPage = 1;
    searchQuery = "";
    searchType = "email";
  }

  document
    .querySelectorAll(".view")
    .forEach((v) => v.classList.remove("active"));
  document
    .querySelectorAll(".nav-item")
    .forEach((n) => n.classList.remove("active"));

  document.getElementById("view-bucket").classList.add("active");
  document.getElementById("bucketTitle").textContent = formatPermission(bucket);
  document.getElementById("bucketTooltip").textContent = bucket;

  document
    .querySelector(`.nav-item[data-bucket="${CSS.escape(bucket)}"]`)
    ?.classList.add("active");

  if (pushState) syncBucketUrl();
  loadBucket(bucket);
}

function showBucketFiltered(bucket, email) {
  currentPage = 1;
  searchQuery = email || "";
  searchType = "email";
  showBucket(bucket, false);
  syncBucketUrl(true);
}

// SUBSCRIPTIONS
let subCurrentPage = 1;
let subPageSize = 10;
let subTotalRecords = 0;
const _subSortPrefs = JSON.parse(
  localStorage.getItem("beacon_sub_sort_prefs") || "{}",
);
let subSortBy = _subSortPrefs.sortBy || "lastchanged";
let subSortDir = _subSortPrefs.sortDir || "desc";
let subSearchQuery = "";
let subSearchType = "email";
let subSelectedHashes = new Set();

// Detail view sort state (client-side, data is already loaded)
const _subDetailSortPrefs = JSON.parse(
  localStorage.getItem("beacon_sub_detail_sort_prefs") || "{}",
);
let subDetailSortBy = _subDetailSortPrefs.sortBy || "bucket";
let subDetailSortDir = _subDetailSortPrefs.sortDir || "asc";
let subDetailSubscriptions = [];
let subDetailEmail = null;
let subDetailName = null;
let subDetailHash = "";
let subDetailLoaded = false;

function toggleDetailSort(column) {
  if (subDetailSortBy === column) {
    subDetailSortDir = subDetailSortDir === "asc" ? "desc" : "asc";
  } else {
    subDetailSortBy = column;
    subDetailSortDir = column === "lastchanged" ? "desc" : "asc";
  }
  localStorage.setItem(
    "beacon_sub_detail_sort_prefs",
    JSON.stringify({ sortBy: subDetailSortBy, sortDir: subDetailSortDir }),
  );
  renderDetailSubscriptions();
}

function sortHeader(label, active, dir, onClick) {
  return html`<th
    class="sortable ${active ? dir : ""}"
    @click=${onClick}
  >
    ${label} <span class="sort-icon">▲</span>
  </th>`;
}

const SEARCH_ICON = html`<svg
  width="14"
  height="14"
  viewBox="0 0 24 24"
  fill="none"
  stroke="currentColor"
  stroke-width="2"
  stroke-linecap="round"
  stroke-linejoin="round"
  aria-hidden="true"
>
  <circle cx="11" cy="11" r="8"></circle>
  <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
</svg>`;

// Column header with an optional sort toggle and a search popover; used by records, subscribers and audit.
function searchColumn(o) {
  const labelText = o.onType
    ? o.type === "id"
      ? o.idLabel
      : o.emailLabel || "Enter email — use * as wildcard"
    : o.fixedLabel;
  const placeholder =
    o.placeholder || (o.type === "id" ? "e.g., a1b2*" : "e.g., *@gmail.com");
  const typeBtn = (type, text) =>
    html`<button
      class="search-type-btn ${o.type === type ? "active" : ""}"
      @click=${(e) => {
        e.stopPropagation();
        o.onType(type);
      }}
    >
      ${text}
    </button>`;
  return html` <div class="column-search">
    ${o.onSort
      ? html`<span
          class="sortable ${o.sortActive ? o.sortDir : ""}"
          @click=${o.onSort}
          >${o.label} <span class="sort-icon">▲</span></span
        >`
      : html`<span>${o.label}</span>`}
    <button
      class="search-trigger ${o.query ? "has-search" : ""}"
      id=${o.ids.trigger}
      title=${o.title || "Search"}
      aria-label=${o.title || "Search"}
      @click=${o.onToggle}
    >
      ${SEARCH_ICON}
    </button>
    <div
      class="search-popover"
      id=${o.ids.popover}
    >
      ${o.onType
        ? html`<div class="search-type-toggle">
            ${typeBtn("email", "By email")}${typeBtn("id", "By ID")}
          </div>`
        : nothing}
      <label for=${o.ids.input}>${labelText}</label>
      <input
        type="text"
        id=${o.ids.input}
        placeholder=${placeholder}
        .value=${o.query || ""}
        @keydown=${(e) => {
          if (e.key === "Enter") o.onSearch();
        }}
      />
      <div class="search-actions">
        <button
          class="btn btn-outline btn-sm"
          @click=${o.onClear}
        >
          Clear
        </button>
        <button
          class="btn btn-primary btn-sm"
          @click=${o.onSearch}
        >
          ${o.actionLabel || "Search"}
        </button>
      </div>
    </div>
  </div>`;
}

function renderDetailSubscriptions() {
  const body = document.getElementById("subscriptionsBody");
  paint(
    document.getElementById("subscriptionsTableHead"),
    html` ${sortHeader(
        "Bucket",
        subDetailSortBy === "bucket",
        subDetailSortDir,
        () => toggleDetailSort("bucket"),
      )}
      <th>Permissions</th>
      ${sortHeader(
        "Last changed",
        subDetailSortBy === "lastchanged",
        subDetailSortDir,
        () => toggleDetailSort("lastchanged"),
      )}
      <th class="col-actions"></th>`,
  );

  if (subDetailSubscriptions.length === 0) {
    paint(
      body,
      subDetailLoaded
        ? html`<tr>
            <td class="table-empty" colspan="4"
            >
              No subscriptions
            </td>
          </tr>`
        : nothing,
    );
    return;
  }

  const sorted = [...subDetailSubscriptions].sort((a, b) => {
    const cmp =
      subDetailSortBy === "bucket"
        ? a.bucket.localeCompare(b.bucket)
        : new Date(a.lastChanged) - new Date(b.lastChanged);
    return subDetailSortDir === "asc" ? cmp : -cmp;
  });

  paint(
    body,
    html`${sorted.map((s, idx) => {
      const record = {
        email: subDetailEmail,
        name: subDetailName,
        emailHash: subDetailHash,
        permissions: s.permissions,
        customFields: {},
      };
      const viewInBucket = () => showBucketFiltered(s.bucket, subDetailEmail);
      return html` <tr>
        <td>
          <span
            class="tooltip-wrapper select-none cursor-pointer"
            @click=${viewInBucket}
            >${formatPermission(s.bucket)}<span class="tooltip tooltip-above"
              >${s.bucket}</span
            ></span
          >
        </td>
        <td>
          <div class="perm-switch-list">
            ${Object.entries(s.permissions).map(
              ([p, optedIn]) =>
                html` <span
                  class="tooltip-wrapper perm-switch select-none"
                  ><span class="permission-name">${formatPermission(p)}</span
                  ><label class="switch disabled"
                    ><input
                      type="checkbox"
                      .checked=${optedIn}
                      disabled
                      aria-label=${formatPermission(p)} /><span
                      class="slider"
                    ></span></label
                  ><span class="tooltip tooltip-above"
                    >${optedIn ? "Opted-in" : "Opted-out"}</span
                  ></span
                >`,
            )}
          </div>
        </td>
        <td>${formatDate(s.lastChanged)}</td>
        <td>
          <div class="row-actions">
            <span class="tooltip-wrapper">
              <button
                class="btn-actions"
                aria-label="Actions"
                @click=${(e) => toggleRowMenu(e, `sub-${idx}`)}
              >
                :
              </button>
              <span class="tooltip tooltip-above tooltip-right">Actions</span>
            </span>
            <div class="dropdown-menu" id="rowMenu-sub-${idx}">
              <button class="dropdown-item" @click=${viewInBucket}>
                View in Bucket
              </button>
              <button
                class="dropdown-item"
                @click=${() =>
                  openEditFromSubscription(
                    s.bucket,
                    Object.keys(s.permissions),
                    record,
                  )}
              >
                Edit permissions
              </button>
              <button
                class="dropdown-item"
                @click=${() =>
                  showAuditForBucketAndIdentity(s.bucket, subDetailHash)}
              >
                View audit
              </button>
            </div>
          </div>
        </td>
      </tr>`;
    })}`,
  );
}

function updateSubPagination() {
  const totalPages = Math.ceil(subTotalRecords / subPageSize) || 1;
  const start =
    subTotalRecords === 0 ? 0 : (subCurrentPage - 1) * subPageSize + 1;
  const end = Math.min(subCurrentPage * subPageSize, subTotalRecords);
  document.getElementById("subPaginationInfo").textContent =
    `Showing ${start.toLocaleString()} to ${end.toLocaleString()} of ${subTotalRecords.toLocaleString()} entries`;
  document.getElementById("subPrevBtn").disabled = subCurrentPage === 1;
  document.getElementById("subNextBtn").disabled = subCurrentPage >= totalPages;
}

function updateSubPageSize() {
  subPageSize = parseInt(document.getElementById("subPageSize").value);
  subCurrentPage = 1;
  loadIdentities();
}

function changeSubPage(delta) {
  subCurrentPage += delta;
  loadIdentities();
}

function toggleSubSort(column) {
  if (subSortBy === column) {
    subSortDir = subSortDir === "asc" ? "desc" : "asc";
  } else {
    subSortBy = column;
    subSortDir = column === "lastchanged" ? "desc" : "asc";
  }
  localStorage.setItem(
    "beacon_sub_sort_prefs",
    JSON.stringify({ sortBy: subSortBy, sortDir: subSortDir }),
  );
  subCurrentPage = 1;
  loadIdentities();
}

function toggleSubSearchPopover(event) {
  event.stopPropagation();
  const popover = document.getElementById("subSearchPopover");
  const trigger = document.getElementById("subSearchTrigger");
  popover.classList.toggle("open");
  trigger.classList.toggle("active", popover.classList.contains("open"));
  if (popover.classList.contains("open")) {
    setTimeout(() => document.getElementById("subSearchInput")?.focus(), 50);
  }
}

function searchIdentities() {
  const input = document.getElementById("subSearchInput");
  subSearchQuery = input ? input.value.trim() : "";
  subCurrentPage = 1;
  loadIdentities();
  document.getElementById("subSearchPopover")?.classList.remove("open");
  document.getElementById("subSearchTrigger")?.classList.remove("active");
}

function clearSubSearch() {
  const input = document.getElementById("subSearchInput");
  if (input) input.value = "";
  subSearchQuery = "";
  subCurrentPage = 1;
  loadIdentities();
  document.getElementById("subSearchPopover")?.classList.remove("open");
  document.getElementById("subSearchTrigger")?.classList.remove("active");
}

function setSubSearchType(type) {
  subSearchType = type;
  // Update toggle, label, placeholder in-place
  document
    .querySelectorAll("#subSearchPopover .search-type-btn")
    .forEach((btn) => {
      btn.classList.toggle(
        "active",
        btn.textContent.trim() === (type === "id" ? "By ID" : "By email"),
      );
    });
  const label = document.querySelector("#subSearchPopover label");
  if (label)
    label.textContent =
      type === "id"
        ? "Enter ID from logs (partial hash)"
        : "Enter email — use * as wildcard";
  const input = document.getElementById("subSearchInput");
  if (input) {
    input.placeholder = type === "id" ? "e.g., a1b2*" : "e.g., *@gmail.com";
    input.focus();
  }
}

function updateSubBulkBar() {
  const bar = document.getElementById("subBulkBar");
  const count = subSelectedHashes.size;
  if (bar) bar.hidden = !(count > 0);
  const countEl = document.getElementById("subBulkCount");
  if (countEl) countEl.textContent = `${count} selected`;
}

function toggleSubSelect(hash, checked) {
  if (checked) subSelectedHashes.add(hash);
  else subSelectedHashes.delete(hash);
  updateSubBulkBar();
  // Sync select-all state
  const all = document.getElementById("subSelectAll");
  if (all) {
    const rows = document.querySelectorAll(
      '#subscriptionsBody input[type="checkbox"]',
    );
    all.checked = rows.length > 0 && [...rows].every((r) => r.checked);
    all.indeterminate = subSelectedHashes.size > 0 && !all.checked;
  }
}

function toggleSubSelectAll(checked) {
  const rows = document.querySelectorAll(
    '#subscriptionsBody input[type="checkbox"]',
  );
  rows.forEach((cb) => {
    cb.checked = checked;
    const hash = cb.dataset.hash;
    if (hash) {
      if (checked) subSelectedHashes.add(hash);
      else subSelectedHashes.delete(hash);
    }
  });
  updateSubBulkBar();
}

function clearSubSelection() {
  subSelectedHashes.clear();
  document
    .querySelectorAll('#subscriptionsBody input[type="checkbox"]')
    .forEach((cb) => (cb.checked = false));
  const all = document.getElementById("subSelectAll");
  if (all) {
    all.checked = false;
    all.indeterminate = false;
  }
  updateSubBulkBar();
}

function openSubExportModal() {
  const count = subSelectedHashes.size;
  document.getElementById("subExportCount").textContent =
    count > 0 ? count : "all";
  openModal("subExportModal");
}

function closeSubExportModal() {
  closeModal("subExportModal");
}

async function confirmSubExport() {
  const format = document.getElementById("subExportFormat").value;
  const btn = document.getElementById("subExportConfirmBtn");
  btn.disabled = true;
  btn.textContent = "Exporting…";

  const body =
    subSelectedHashes.size > 0
      ? { hashes: [...subSelectedHashes], format }
      : { format };

  try {
    const res = await fetch(
      `${window.location.origin}/api/admin/identities/export`,
      {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      },
    );
    if (!res.ok) {
      const err = await res.json().catch(() => ({}));
      notify("error", "Export failed", err.error || `HTTP ${res.status}`);
      return;
    }
    const blob = await res.blob();
    const cd = res.headers.get("Content-Disposition") || "";
    const fnMatch = cd.match(/filename="([^"]+)"/);
    const filename = fnMatch ? fnMatch[1] : `subscribers.${format}`;
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(url);
    closeSubExportModal();
  } finally {
    btn.disabled = false;
    btn.textContent = "Export";
  }
}

async function loadIdentities(pushState = true) {
  const thead = document.getElementById("subscriptionsTableHead");
  const body = document.getElementById("subscriptionsBody");

  document.getElementById("subTitle").textContent = "Subscribers";
  const badge = document.getElementById("subBadge");
  badge.textContent = "Overview";
  badge.classList.remove("tooltip-wrapper");
  badge.removeAttribute("data-hash");
  badge.classList.remove("cursor-pointer");
  badge.onclick = null;
  badge.ondblclick = null;
  subDetailHash = "";
  document.getElementById("subBackBtn").hidden = true;
  document.getElementById("subRefreshWrapper").hidden = false;
  document.querySelector(
    "#view-subscriptions .pagination-container",
  ).hidden = false;
  const subNameEl = document.getElementById("subDetailName");
  if (subNameEl) subNameEl.hidden = true;

  if (pushState) syncSubUrl();

  const emailSortClass =
    subSortBy === "email" ? (subSortDir === "asc" ? "asc" : "desc") : "";
  const bucketsSortClass =
    subSortBy === "buckets" ? (subSortDir === "asc" ? "asc" : "desc") : "";
  const createdSortClass =
    subSortBy === "firstseen" ? (subSortDir === "asc" ? "asc" : "desc") : "";
  const updatedSortClass =
    subSortBy === "lastchanged" ? (subSortDir === "asc" ? "asc" : "desc") : "";
  const sortIcon = '<span class="sort-icon">▲</span>';
  const hasSearch = subSearchQuery ? "has-search" : "";

  paint(
    thead,
    html` <th class="col-select">
        <label class="row-check" title="Select all"
          ><input
            type="checkbox"
            id="subSelectAll"
            aria-label="Select all"
            @change=${(e) => toggleSubSelectAll(e.target.checked)}
        /></label>
      </th>
      <th>
        ${searchColumn({
          label: "Email",
          sortActive: subSortBy === "email",
          sortDir: subSortDir,
          onSort: () => toggleSubSort("email"),
          ids: {
            trigger: "subSearchTrigger",
            popover: "subSearchPopover",
            input: "subSearchInput",
          },
          type: subSearchType,
          query: subSearchQuery,
          idLabel: "Enter ID from logs (partial hash)",
          onToggle: toggleSubSearchPopover,
          onType: setSubSearchType,
          onSearch: searchIdentities,
          onClear: clearSubSearch,
        })}
      </th>
      ${sortHeader("Name", subSortBy === "name", subSortDir, () => toggleSubSort("name"))}
      ${sortHeader("Buckets", subSortBy === "buckets", subSortDir, () =>
        toggleSubSort("buckets"),
      )}
      ${sortHeader("Created", subSortBy === "firstseen", subSortDir, () =>
        toggleSubSort("firstseen"),
      )}
      ${sortHeader("Updated", subSortBy === "lastchanged", subSortDir, () =>
        toggleSubSort("lastchanged"),
      )}
      <th class="col-actions"></th>`,
  );
  paint(body, nothing);

  let url = `/api/admin/identities?page=${subCurrentPage}&pageSize=${subPageSize}&sortBy=${subSortBy}&sortDir=${subSortDir}`;
  if (subSearchQuery)
    url += `&search=${encodeURIComponent(subSearchQuery)}&searchType=${subSearchType}`;

  const result = await apiRequest(url);
  if (!result.ok) return;

  const data = result.data;
  subTotalRecords = data.total;

  if (data.records.length === 0) {
    const msg = subSearchQuery
      ? `No identities match "${subSearchQuery}".`
      : "No consent records in any bucket.";
    paint(
      body,
      html`<tr>
        <td class="table-empty" colspan="7"
        >
          ${msg}
        </td>
      </tr>`,
    );
  } else {
    paint(
      body,
      html`${data.records.map((id, idx) => {
        const stop = (fn) => (e) => {
          e.stopPropagation();
          fn();
        };
        return html` <tr class="cursor-pointer"
          @click=${() => showIdentityDetails(id.emailHash)}
        >
          <td class="col-select" @click=${(e) => e.stopPropagation()}>
            <label class="row-check"
              ><input
                type="checkbox"
                data-hash=${id.emailHash}
                .checked=${subSelectedHashes.has(id.emailHash)}
                aria-label="Select row"
                @change=${(e) =>
                  toggleSubSelect(id.emailHash, e.target.checked)}
            /></label>
          </td>
          <td>
            ${id.email
              ? html`<span class="tooltip-wrapper"
                  ><span
                    class="email-text select-none cursor-pointer"
                    @click=${stop(() => copyText(id.email))}
                    @dblclick=${stop(() => copyTextNow(id.emailHash))}
                    >${id.email}</span
                  ><span class="tooltip"
                    >Click to copy &middot; double-click for ID</span
                  ></span
                >`
              : html`<span
                  class="tooltip-wrapper"
                  @dblclick=${stop(() => copyTextNow(id.emailHash))}
                  ><span class="email-hash"
                    >${id.emailHash.substring(0, 16)}…</span
                  ><span class="tooltip">Double-click to copy ID</span></span
                >`}
          </td>
          <td>
            ${id.name
              ? html`<span class="name-text">${id.name}</span>`
              : html`<span class="muted">—</span>`}
          </td>
          <td>${id.bucketCount} bucket${id.bucketCount !== 1 ? "s" : ""}</td>
          <td>${formatDate(id.firstSeen)}</td>
          <td
            class="col-link"
            title="View audit"
            @click=${stop(() => showAuditForIdentity(id.emailHash))}
          >
            ${formatDate(id.lastChanged)}
          </td>
          <td>
            <div class="row-actions">
              <span class="tooltip-wrapper">
                <button
                  class="btn-actions"
                  aria-label="Actions"
                  @click=${(e) => {
                    e.stopPropagation();
                    toggleRowMenu(e, `sid-${idx}`);
                  }}
                >
                  :
                </button>
                <span class="tooltip tooltip-above tooltip-right">Actions</span>
              </span>
              <div class="dropdown-menu" id="rowMenu-sid-${idx}">
                <button
                  class="dropdown-item"
                  @click=${() => showIdentityDetails(id.emailHash)}
                >
                  View details
                </button>
                <button
                  class="dropdown-item"
                  @click=${() => showAuditForIdentity(id.emailHash)}
                >
                  View audit
                </button>
              </div>
            </div>
          </td>
        </tr>`;
      })}`,
    );

    // Sync select-all checkbox state
    const all = document.getElementById("subSelectAll");
    if (all && subSelectedHashes.size > 0) {
      const visible = data.records.map((r) => r.emailHash);
      const allSelected = visible.every((h) => subSelectedHashes.has(h));
      const someSelected = visible.some((h) => subSelectedHashes.has(h));
      all.checked = allSelected;
      all.indeterminate = someSelected && !allSelected;
    }
  }
  updateSubPagination();
}

async function showIdentityDetails(emailHash, pushState = true) {
  // Update chrome synchronously before the fetch so there is no flash of stale content
  document.getElementById("subTitle").textContent = "Identity details";
  const badge = document.getElementById("subBadge");
  badge.dataset.hash = emailHash;
  delete badge.dataset.email;
  badge.classList.add("tooltip-wrapper");
  badge.classList.add("cursor-pointer");
  badge.onclick = () => copyText(badge.dataset.email || badge.dataset.hash);
  badge.ondblclick = () => copyTextNow(badge.dataset.hash);
  paint(
    badge,
    html`<span class="email-hash text-inherit"
        >${emailHash.substring(0, 16)}…</span
      ><span class="tooltip">Click to copy. Double-click to copy the ID.</span>`,
  );
  document.getElementById("subBackBtn").hidden = false;
  document.getElementById("subRefreshWrapper").hidden = true;
  document.querySelector(
    "#view-subscriptions .pagination-container",
  ).hidden = true;

  // Clear cached data and render the detail headers with a blank body (no stale rows)
  subDetailSubscriptions = [];
  subDetailEmail = null;
  subDetailName = null;
  subDetailHash = emailHash;
  subDetailLoaded = false;
  renderDetailSubscriptions();

  const result = await apiRequest(
    `/api/admin/identities/${encodeURIComponent(emailHash)}`,
  );
  if (!result.ok) return;

  if (pushState) syncSubUrl(emailHash);

  const details = result.data;

  // Now that we have the email, update the badge label and wire copy target
  if (details.email) {
    badge.dataset.email = details.email;
    paint(
      badge,
      html`<span class="text-inherit">${details.email}</span
        ><span class="tooltip">Click to copy. Double-click to copy the ID.</span>`,
    );
  }

  subDetailName = details.name || null;
  const nameEl = document.getElementById("subDetailName");
  if (nameEl) {
    nameEl.textContent = subDetailName || "";
    nameEl.hidden = !subDetailName;
  }

  subDetailSubscriptions = details.subscriptions;
  subDetailEmail = details.email || null;
  subDetailLoaded = true;
  renderDetailSubscriptions();
}
