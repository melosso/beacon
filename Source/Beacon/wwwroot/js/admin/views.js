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

const SHEET_VIEWS = {
  "new-token": (pushState) => openNewTokenSheet(pushState),
  "new-bucket": (pushState) => openNewBucketSheet(pushState),
};

function openUrlSheet(id, key, pushState) {
  openModal(id);
  if (!pushState) return;
  const url = new URL(window.location);
  url.searchParams.set("modal", key);
  history.pushState(null, "", url);
}

function closeUrlSheet(id, force) {
  if (!closeModal(id, force)) return false;
  const url = new URL(window.location);
  url.searchParams.delete("modal");
  history.replaceState(null, "", url);
  return true;
}

function showView(view, pushState = true, skipLoad = false) {
  closeMobileNav();
  if (SHEET_VIEWS[view]) {
    SHEET_VIEWS[view](pushState);
    return;
  }
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
  // submission-edit reuses the create view DOM
  const viewId =
    view === "submission-edit" || view === "submission-embed"
      ? "view-submission-create"
      : `view-${view}`;
  if (guardLeave(viewId, pushState, () => showView(view, pushState, skipLoad)))
    return;
  currentView = view;
  document
    .querySelectorAll(".view")
    .forEach((v) => v.classList.remove("active"));
  document
    .querySelectorAll(".nav-item")
    .forEach((n) => n.classList.remove("active"));

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
  if (view === "settings") loadSettings();
  if (view === "workflow") loadWorkflowPage();
  if (view === "audit") loadAudit();
  if (view === "subscriptions") {
    loadIdentities(pushState);
  }
}

// Gate for every view switch: returns true if unsaved edits on the active view require confirmation.
function guardLeave(targetViewId, pushState, retry) {
  const active = document.querySelector(".view.active");
  if (!active || active.id === targetViewId || !isDirty(active)) return false;
  // Back/forward already changed the URL: restore it, replay the navigation on discard.
  if (!pushState) history.pushState(null, "", guardUrl(active));
  confirmDiscard(active, pushState ? retry : () => history.back());
  return true;
}

function showBucket(bucket, pushState = true) {
  closeMobileNav();
  if (guardLeave("view-bucket", pushState, () => showBucket(bucket, pushState)))
    return;
  if (isModalOpen("optionsModal") && !closeOptionsModal()) return;
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
  document.getElementById("bucketHeading").dataset.tooltip = bucket;

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
  const types = {
    email: ["By email", o.emailLabel || "Enter email", "e.g., *@gmail.com"],
    name: ["By name", "Enter name", "e.g., Jane*"],
    id: ["By ID", o.idLabel, "e.g., a1b2*"],
  };
  const [, labelText, placeholder] = o.onType
    ? types[o.type]
    : [, o.fixedLabel, o.placeholder];
  const typeBtn = (type) =>
    html`<button
      class="search-type-btn ${o.type === type ? "active" : ""}"
      data-type=${type}
      data-label=${types[type][1]}
      data-placeholder=${types[type][2]}
      @click=${(e) => {
        e.stopPropagation();
        o.onType(type);
      }}
    >
      ${types[type][0]}
    </button>`;
  return html` <div class="column-search">
    ${
      o.onSort
        ? html`<span
          class="sortable ${o.sortActive ? o.sortDir : ""}"
          @click=${o.onSort}
          >${o.label} <span class="sort-icon">▲</span></span
        >`
        : html`<span>${o.label}</span>`
    }
    <button
      class="search-trigger ${o.query ? "has-search" : ""}"
      id=${o.ids.trigger}
      data-tooltip=${o.title || "Search"}
      aria-label=${o.title || "Search"}
      @click=${o.onToggle}
    >
      ${SEARCH_ICON}
    </button>
    ${
      o.query
        ? html`<button
            type="button"
            class="filter-chip"
            aria-label=${`Clear search: ${o.query}`}
            @click=${(e) => {
              e.stopPropagation();
              o.onClear();
            }}
          >
            <span class="filter-chip-text">${o.query}</span>
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M18 6 6 18M6 6l12 12"/></svg>
          </button>`
        : nothing
    }
    <div
      class="search-popover"
      id=${o.ids.popover}
    >
      ${
        o.onType
          ? html`<div class="search-type-toggle">
            ${(o.types || ["email", "id"]).map(typeBtn)}
          </div>`
          : nothing
      }
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

  const isArchived = (name) =>
    buckets.some((b) => b.name === name && b.isArchived);
  // Archived buckets are read-only and listed last.
  const sorted = [...subDetailSubscriptions].sort((a, b) => {
    const archivedCmp = isArchived(a.bucket) - isArchived(b.bucket);
    if (archivedCmp) return archivedCmp;
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
        pendingPermissions: s.pendingPermissions,
        customFields: {},
      };
      const viewInBucket = () => showBucketFiltered(s.bucket, subDetailEmail);
      const archived = isArchived(s.bucket);
      return html` <tr class=${archived ? "row--archived" : ""}>
        <td>
          <span
            class="select-none cursor-pointer"
            @click=${viewInBucket} data-tooltip=${s.bucket}>${formatPermission(s.bucket)}</span
          >
          ${
            archived
              ? html`<span class="status-badge status-badge--sm"
                  >Archived</span
                >`
              : nothing
          }
        </td>
        <td>
          <div class="perm-switch-list">
            ${Object.entries(s.permissions).map(
              ([p, optedIn]) =>
                html` <span class="perm-switch select-none"
                  ><span class="permission-name">${formatPermission(p)}</span
                  >${consentChip(optedIn, s.pendingPermissions?.includes(p))}</span
                >`,
            )}
          </div>
        </td>
        <td>
          <button
            type="button"
            class="link-btn"
            data-tooltip="Show audit entries of this bucket"
            @click=${() => showAuditForBucketAndIdentity(s.bucket, subDetailHash)}
          >${formatDate(s.lastChanged)}</button>
        </td>
        <td>
          <div class="row-actions">
            <button
                class="btn-icon btn-icon--sm"
                aria-label="Actions"
                @click=${(e) => toggleRowMenu(e, `sub-${idx}`)}
              >${KEBAB_ICON}</button>
            <div class="dropdown-menu" id="rowMenu-sub-${idx}">
              <button class="dropdown-item" @click=${viewInBucket}>
                View in bucket
              </button>
              <button
                class="dropdown-item${archived ? " disabled" : ""}"
                ?disabled=${archived}
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
  renderPager(document.getElementById("subPager"), {
    page: subCurrentPage,
    pageSize: subPageSize,
    total: subTotalRecords,
    onPage: (n) => {
      subCurrentPage = n;
      loadIdentities();
    },
    onPageSize: (n) => {
      subPageSize = n;
      subCurrentPage = 1;
      loadIdentities();
    },
  });
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

function applySearchType(popoverId, type) {
  const popover = document.getElementById(popoverId);
  if (!popover) return null;
  let active = null;
  for (const btn of popover.querySelectorAll(".search-type-btn")) {
    btn.classList.toggle("active", btn.dataset.type === type);
    if (btn.dataset.type === type) active = btn;
  }
  popover.querySelector("label").textContent = active.dataset.label;
  const input = popover.querySelector("input");
  input.placeholder = active.dataset.placeholder;
  input.focus();
  return input;
}

function setSubSearchType(type) {
  subSearchType = type;
  applySearchType("subSearchPopover", type);
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

function subExportHashes() {
  return subDetailHash ? [subDetailHash] : [...subSelectedHashes];
}

function openSubExportModal() {
  closeAllMenus();
  if (subDetailHash) document.getElementById("subExportContents").value = "full";
  const bucketInput = document.getElementById("subExportBucket");
  attachBucketPicker(bucketInput, { allLabel: "All buckets", onSelect: updateSubExportCount });
  bucketInput.value = "";
  updateSubExportCount();
  openModal("subExportModal");
}

function updateSubExportCount() {
  const selected = subExportHashes().length;
  const bucket = buckets.find(
    (b) => b.name === document.getElementById("subExportBucket").value,
  );
  const count =
    selected > 0
      ? formatNumber(selected)
      : bucket
        ? formatNumber(bucket.totalEmails ?? 0)
        : subSearchQuery
          ? "all"
          : formatNumber(subTotalRecords);
  document.getElementById("subExportCount").textContent =
    selected > 0 && bucket ? `up to ${count}` : count;
}

function openIdentityNameModal() {
  closeAllMenus();
  const input = document.getElementById("identityNameInput");
  input.value = subDetailName || "";
  openModal("identityNameModal");
  input.focus();
}

function closeIdentityNameModal() {
  closeModal("identityNameModal");
}

async function saveIdentityName() {
  const name = document.getElementById("identityNameInput").value.trim();
  const result = await apiRequest(
    `/api/admin/identities/${encodeURIComponent(subDetailHash)}/name`,
    { method: "PUT", body: { name } },
  );
  if (!result.ok) return;
  closeIdentityNameModal();
  notify("success", "Name saved");
  showIdentityDetails(subDetailHash, false);
}

function identityDeleteKey() {
  if (!subDetailHash) return String(subSelectedHashes.size);
  return subDetailEmail || subDetailHash.slice(0, 12);
}

function openIdentityDeleteModal() {
  closeAllMenus();
  const count = subDetailHash ? 1 : subSelectedHashes.size;
  document.getElementById("identityDeleteDesc").textContent =
    count === 1
      ? "Wipes this subscriber from every bucket, with their consent history and queued emails. Connected webhooks get an erasure notice. This cannot be undone."
      : `Wipes ${formatNumber(count)} subscribers from every bucket, with their consent history and queued emails. Connected webhooks get an erasure notice. This cannot be undone.`;
  document.getElementById("identityDeleteLabel").textContent =
    `Type ${identityDeleteKey()} to confirm.`;
  document.getElementById("identityDeleteInput").value = "";
  document.getElementById("identityDeleteBtn").disabled = true;
  openModal("identityDeleteModal");
  document.getElementById("identityDeleteInput").focus();
}

function closeIdentityDeleteModal() {
  closeModal("identityDeleteModal");
}

function checkIdentityDeleteInput(value) {
  document.getElementById("identityDeleteBtn").disabled =
    value.trim().toLowerCase() !== identityDeleteKey().toLowerCase();
}

async function deleteIdentity() {
  if (document.getElementById("identityDeleteBtn").disabled) return;
  const result = subDetailHash
    ? await apiRequest(
        `/api/admin/identities/${encodeURIComponent(subDetailHash)}`,
        { method: "DELETE" },
      )
    : await apiRequest("/api/admin/identities/delete", {
        method: "POST",
        body: { hashes: [...subSelectedHashes] },
      });
  if (!result.ok) return;
  closeIdentityDeleteModal();
  notify(
    "success",
    subDetailHash ? "Subscriber deleted" : "Subscribers deleted",
    subDetailHash
      ? `Wiped from ${formatNumber(result.data.bucketsDeleted)} bucket(s).`
      : `${formatNumber(result.data.identitiesDeleted)} subscriber(s) wiped.`,
  );
  clearSubSelection();
  loadIdentities();
}

function closeSubExportModal() {
  closeModal("subExportModal");
}

async function confirmSubExport() {
  const format = document.getElementById("subExportFormat").value;
  const full = document.getElementById("subExportContents").value === "full";
  const bucket = document.getElementById("subExportBucket").value.trim();
  if (bucket && !buckets.some((b) => b.name === bucket)) {
    notify("warning", "Bucket not found", "Pick a bucket from the list or leave the field empty for all buckets.");
    return;
  }
  const btn = document.getElementById("subExportConfirmBtn");
  btn.disabled = true;
  btn.textContent = "Exporting…";

  const hashes = subExportHashes();
  const body = {
    format,
    full,
    ...(hashes.length > 0 ? { hashes } : {}),
    ...(bucket ? { bucket } : {}),
  };

  try {
    if (
      await downloadExport(
        `${window.location.origin}/api/admin/identities/export`,
        body,
        `beacon-subscribers${full ? "-full" : ""}${bucket ? `-${bucket}` : ""}.${format}`,
      )
    )
      closeSubExportModal();
  } finally {
    btn.disabled = false;
    btn.textContent = "Export";
  }
}

async function loadIdentities(pushState = true) {
  const isCurrent = latestCall("subscriptions");
  const thead = document.getElementById("subscriptionsTableHead");
  const body = document.getElementById("subscriptionsBody");

  document.getElementById("subTitle").textContent = "Subscribers";
  const badge = document.getElementById("subBadge");
  badge.hidden = true;
  badge.textContent = "";
  delete badge.dataset.tooltip;
  badge.removeAttribute("data-hash");
  badge.classList.remove("cursor-pointer");
  badge.onclick = null;
  badge.ondblclick = null;
  subDetailHash = "";
  document.getElementById("subBackBtn").hidden = true;
  document.getElementById("subIdentityActions").hidden = true;
  document.getElementById("subAddBtn").textContent = "Add identity";
  document.querySelector("#view-subscriptions .pagination-container").hidden =
    false;

  if (pushState) syncSubUrl();

  // Created has no column of its own: a saved or linked sort on it falls back to Updated
  if (subSortBy === "firstseen") subSortBy = "lastchanged";

  paint(
    thead,
    html` <th class="col-select">
        <label class="row-check" data-tooltip="Select all"
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
          types: ["email", "name", "id"],
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
  if (!result.ok || !isCurrent()) return;

  const data = result.data;
  subTotalRecords = data.total;

  if (data.records.length === 0) {
    const msg = subSearchQuery
      ? `No identities match "${subSearchQuery}".`
      : "No consent records in any bucket.";
    paint(
      body,
      html`<tr>
        <td class="table-empty" colspan="6"
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
            ${
              id.email
                ? html`<span class="email-text">${id.email}</span>`
                : html`<span class="email-hash"
                    >${id.emailHash.substring(0, 16)}…</span
                  >`
            }
          </td>
          <td>
            ${
              id.name
                ? html`<span class="name-text">${id.name}</span>`
                : html`<span class="muted">—</span>`
            }
          </td>
          <td>${id.bucketCount} bucket${id.bucketCount !== 1 ? "s" : ""}</td>
          <td
            class="col-link"
            data-tooltip="View audit"
            @click=${stop(() => showAuditForIdentity(id.emailHash))}
          >
            ${formatDate(id.lastChanged)}${
              formatDate(id.firstSeen) !== formatDate(id.lastChanged)
                ? html`<span class="cell-sub">Created ${formatDate(id.firstSeen)}</span>`
                : nothing
            }
          </td>
          <td>
            <div class="row-actions">
              <button
                  class="btn-icon btn-icon--sm"
                  aria-label="Actions"
                  @click=${(e) => {
                    e.stopPropagation();
                    toggleRowMenu(e, `sid-${idx}`);
                  }}
                >${KEBAB_ICON}</button>
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

function openSubscriber(emailHash) {
  showView("subscriptions", false, true);
  showIdentityDetails(emailHash);
}

async function showIdentityDetails(emailHash, pushState = true) {
  const isCurrent = latestCall("subscriptions");
  // Update chrome synchronously before the fetch so there is no flash of stale content
  document.getElementById("subTitle").textContent = "Identity details";
  const badge = document.getElementById("subBadge");
  badge.hidden = false;
  badge.dataset.hash = emailHash;
  delete badge.dataset.email;
  badge.dataset.tooltip = "Click to copy. Double-click to copy the ID.";
  badge.classList.add("cursor-pointer");
  badge.onclick = () => copyText(badge.dataset.email || badge.dataset.hash);
  badge.ondblclick = () => copyTextNow(badge.dataset.hash);
  paint(
    badge,
    html`<span class="email-hash text-inherit"
        >${emailHash.substring(0, 16)}…</span
      >`,
  );
  document.getElementById("subBackBtn").hidden = false;
  document.getElementById("subIdentityActions").hidden = false;
  document.getElementById("subAddBtn").textContent = "Add subscription";
  document.querySelector("#view-subscriptions .pagination-container").hidden =
    true;

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
  if (!result.ok || !isCurrent()) return;

  if (pushState) syncSubUrl(emailHash);

  const details = result.data;

  // Now that we have the email, update the badge label and wire copy target
  if (details.email) {
    badge.dataset.email = details.email;
    paint(
      badge,
      html`<span class="text-inherit">${details.email}</span>`,
    );
  }

  subDetailName = details.name || null;
  if (subDetailName)
    document.getElementById("subTitle").textContent = subDetailName;

  subDetailSubscriptions = details.subscriptions;
  subDetailEmail = details.email || null;
  subDetailLoaded = true;
  renderDetailSubscriptions();
  if (isModalOpen("newTokenSheet")) prefillTokenFromIdentity();
}
