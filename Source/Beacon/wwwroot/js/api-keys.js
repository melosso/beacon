// API KEYS VIEW
function triggerClass(el, cls, duration = 700) {
  if (!el) return;
  el.classList.remove(cls);
  void el.offsetWidth;
  el.classList.add(cls);
  setTimeout(() => el.classList.remove(cls), duration);
}

const KNOWN_PERMISSIONS = [
  { value: "_all", label: "Full access (_all)" },
  { value: "consent:read", label: "Consent — Read" },
  { value: "consent:write", label: "Consent — Write" },
  { value: "tokens:write", label: "Tokens — Generate" },
  { value: "buckets:read", label: "Buckets — Read" },
  { value: "buckets:write", label: "Buckets — Write" },
  { value: "submissions:read", label: "Submissions — Read" },
  { value: "submissions:write", label: "Submissions — Write" },
  { value: "audit:read", label: "Audit — Read" },
  { value: "webhooks:read", label: "Webhooks — Read" },
  { value: "webhooks:write", label: "Webhooks — Write" },
];

let _apiKeysAll = [],
  _apiKeysPage = 1,
  _apiKeysPageSize = 25;
let _editApiKeyId = null;
let _rotatedApiKey = "";
let _newRawApiKey = "";

async function loadApiKeys() {
  const result = await apiRequest("/api/admin/api-keys");
  if (!result.ok) return;
  _apiKeysAll = result.data || [];
  _apiKeysPage = 1;
  renderApiKeysPage();
}

function renderApiKeysPage() {
  const start = (_apiKeysPage - 1) * _apiKeysPageSize;
  renderApiKeysTable(_apiKeysAll.slice(start, start + _apiKeysPageSize));
  renderApiKeysPagination();
}

function renderApiKeysPagination() {
  const bar = document.getElementById("apiKeysPaginationBar");
  const info = document.getElementById("apiKeysPaginationInfo");
  const prevBtn = document.getElementById("apiKeysPrevBtn");
  const nextBtn = document.getElementById("apiKeysNextBtn");
  const total = _apiKeysAll.length;
  const totalPages = Math.ceil(total / _apiKeysPageSize) || 1;
  if (bar) bar.hidden = !(total > _apiKeysPageSize);
  if (info) {
    const start = (_apiKeysPage - 1) * _apiKeysPageSize + 1;
    const end = Math.min(_apiKeysPage * _apiKeysPageSize, total);
    info.textContent = `${start}–${end} of ${total}`;
  }
  if (prevBtn) prevBtn.disabled = _apiKeysPage <= 1;
  if (nextBtn) nextBtn.disabled = _apiKeysPage >= totalPages;
}

function changeApiKeysPage(dir) {
  const totalPages = Math.ceil(_apiKeysAll.length / _apiKeysPageSize) || 1;
  _apiKeysPage = Math.max(1, Math.min(_apiKeysPage + dir, totalPages));
  renderApiKeysPage();
}

function updateApiKeysPageSize() {
  _apiKeysPageSize = parseInt(
    document.getElementById("apiKeysPageSize").value,
    10,
  );
  _apiKeysPage = 1;
  renderApiKeysPage();
}

function renderPermissionBadges(permissions) {
  if (!permissions?.length) return html`<span class="text-muted">—</span>`;
  return permissions.map((p) => html`<span class="status-badge status-badge--sm${p === "_all" ? "" : " status-badge-muted"}">${p}</span>`);
}

function apiKeyStatus(k) {
  if (!k.isEnabled)
    return { label: "Disabled", tone: "text-muted" };
  const now = new Date();
  if (k.activeFrom && now < new Date(k.activeFrom))
    return { label: "Pending", tone: "text-warning" };
  if (k.activeUntil && now > new Date(k.activeUntil))
    return { label: "Expired", tone: "text-danger" };
  return { label: "Active", tone: "text-success" };
}

function renderApiKeysTable(keys) {
  const tbody = document.getElementById("apiKeysBody");
  if (!keys.length) {
    paint(tbody, html`<tr><td colspan="7"><div class="table-empty-state">
          <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true"><circle cx="7.5" cy="15.5" r="5.5"/><path d="m21 2-9.6 9.6"/><path d="m15.5 7.5 3 3L22 7l-3-3"/></svg>
          <p class="empty-title">No API keys</p>
          <p class="empty-sub">Add an API key to grant programmatic access.</p>
        </div></td></tr>`);
    return;
  }
  paint(tbody, html`${keys.map((k) => {
    const st = apiKeyStatus(k);
    return html`
        <tr>
          <td><strong>${k.name}</strong></td>
          <td>${renderPermissionBadges(k.permissions)}</td>
          <td><span class=${st.tone}>${st.label}</span></td>
          <td>${k.activeUntil ? new Date(k.activeUntil).toLocaleDateString() : html`<span class="text-muted">—</span>`}</td>
          <td>${k.createdAt ? new Date(k.createdAt).toLocaleDateString() : "-"}</td>
          <td>${k.lastUsedAt ? formatRelativeTime(k.lastUsedAt) : html`<span class="text-muted">Never</span>`}</td>
          <td class="col-actions">${rowMenu(`apiKeyDropdown-${k.id}`, [
            ["Edit permissions", () => openEditApiKeyPermissionsModal(k.id, k.name, k.permissions || [])],
            ["Edit validity window", () => openEditApiKeyDatesModal(k.id, k.name, k.activeFrom || "", k.activeUntil || "")],
            [k.isEnabled ? "Disable" : "Enable", () => toggleApiKeyEnabled(k.id, k.isEnabled)],
            ["Rotate key", () => rotateApiKey(k.id, k.name)],
            ["Delete", () => deleteApiKey(k.id, k.name), true],
          ])}</td>
        </tr>`;
  })}`);
  staggerRows(tbody);
}

function buildPermissionCheckboxes(containerId, selected) {
  const container = document.getElementById(containerId);
  if (!container) return;
  // Initial sync keeps the saved selection; toggling _all sets every box to match it.
  const syncAllState = (event) => {
    const isAll = container.querySelector('input[value="_all"]').checked;
    container.querySelectorAll('input[type=checkbox]:not([value="_all"])').forEach((cb) => {
      if (event || isAll) cb.checked = isAll;
      cb.disabled = isAll;
      if (isAll) triggerClass(cb, "cb-flash", 300);
    });
  };
  paint(container, html`${KNOWN_PERMISSIONS.map((p) => html`
        <label class="check-row">
          <input type="checkbox" value=${p.value} ?checked=${selected.includes(p.value)}
            @change=${p.value === "_all" ? syncAllState : null}>
          ${p.label}
        </label>`)}`);
  syncAllState();
}

function getCheckedPermissions(containerId) {
  const container = document.getElementById(containerId);
  if (!container) return [];
  return Array.from(
    container.querySelectorAll("input[type=checkbox]:checked"),
  ).map((cb) => cb.value);
}

function openAddApiKeyModal() {
  document.getElementById("newApiKeyName").value = "";
  document.getElementById("newApiKeyActiveFrom").value = "";
  document.getElementById("newApiKeyActiveUntil").value = "";
  document.getElementById("newApiKeyRevealSection").hidden = true;
  buildPermissionCheckboxes("newApiKeyPermissions", []);
  const btn = document.getElementById("addApiKeyBtn");
  btn.classList.remove("is-loading");
  btn.disabled = false;
  btn.querySelector(".btn-label").textContent = "Create API key";
  document.getElementById("addApiKeyCancelBtn").hidden = false;
  _newRawApiKey = "";
  openModal("addApiKeyModal");
  setTimeout(() => document.getElementById("newApiKeyName")?.focus(), 50);
}

function closeAddApiKeyModal() {
  closeModal("addApiKeyModal");
}

async function createApiKey() {
  if (_newRawApiKey) {
    closeAddApiKeyModal();
    return;
  }
  const name = document.getElementById("newApiKeyName").value.trim();
  const permissions = getCheckedPermissions("newApiKeyPermissions");
  const activeFrom = document.getElementById("newApiKeyActiveFrom").value;
  const activeUntil = document.getElementById("newApiKeyActiveUntil").value;

  if (!name) {
    notify("error", "Name required", "Enter a name.");
    return;
  }
  if (_apiKeysAll.some((k) => k.name.toLowerCase() === name.toLowerCase())) {
    notify("error", "Duplicate name", "An API key with this name exists. Choose another name.");
    return;
  }
  if (!permissions.length) {
    notify("error", "Permission required", "Select at least one permission.");
    return;
  }
  if (
    activeFrom &&
    activeUntil &&
    new Date(activeUntil) <= new Date(activeFrom)
  ) {
    notify("error", "Invalid dates", "Set active until after active from.");
    return;
  }

  const btn = document.getElementById("addApiKeyBtn");
  btn.classList.add("is-loading");
  btn.disabled = true;

  const body = { name, permissions };
  if (activeFrom) body.activeFrom = activeFrom;
  if (activeUntil) body.activeUntil = activeUntil;

  const result = await apiRequest("/api/admin/api-keys", {
    method: "POST",
    body,
  });

  btn.classList.remove("is-loading");
  btn.disabled = false;

  if (result.ok) {
    _newRawApiKey = result.data.apiKey;
    const newKeyOut = document.getElementById("newApiKeyOutput");
    newKeyOut.textContent = _newRawApiKey;
    document.getElementById("newApiKeyRevealSection").hidden = false;
    triggerClass(newKeyOut, "key-new", 1200);
    btn.querySelector(".btn-label").textContent = "Done";
    document.getElementById("addApiKeyCancelBtn").hidden = true;
    loadApiKeys();
  }
}

function copyNewApiKey() {
  if (!_newRawApiKey) return;
  clipboardWrite(_newRawApiKey).then(() => {
    triggerClass(document.getElementById("newApiKeyOutput"), "key-copied", 700);
    notify("success", "API key copied");
  });
}

async function deleteApiKey(id, name) {
  openConfirmModal(
    `Delete API Key`,
    html`Delete <strong>${name}</strong>? This action cannot be undone. Any services using this key will lose access.`,
    "Delete",
    async () => {
      const result = await apiRequest(`/api/admin/api-keys/${id}`, {
        method: "DELETE",
      });
      if (result.ok) {
        loadApiKeys();
        notify("success", "API key deleted");
      }
    },
  );
}

async function toggleApiKeyEnabled(id, currentEnabled) {
  const result = await apiRequest(`/api/admin/api-keys/${id}/enabled`, {
    method: "PATCH",
    body: { isEnabled: !currentEnabled },
  });
  if (result.ok) loadApiKeys();
}

async function rotateApiKey(id, name) {
  openConfirmModal(
    `Rotate API Key`,
    html`Rotate <strong>${name}</strong>? The current key will be invalidated immediately.`,
    "Rotate",
    async () => {
      const result = await apiRequest(`/api/admin/api-keys/${id}/rotate`, {
        method: "POST",
      });
      if (result.ok) {
        _rotatedApiKey = result.data.apiKey;
        document.getElementById("apiKeyRevealDesc").textContent =
          `New key for "${name}". Copy it now — it won't be shown again.`;
        const revealOut = document.getElementById("apiKeyRevealOutput");
        revealOut.textContent = _rotatedApiKey;
        openModal("apiKeyRevealModal");
        triggerClass(revealOut, "key-new", 1200);
        loadApiKeys();
      }
    },
  );
}

function closeApiKeyRevealModal() {
  closeModal("apiKeyRevealModal");
}

function copyRotatedApiKey() {
  if (!_rotatedApiKey) return;
  clipboardWrite(_rotatedApiKey).then(() => {
    triggerClass(
      document.getElementById("apiKeyRevealOutput"),
      "key-copied",
      700,
    );
    notify("success", "API key copied");
  });
}

function openEditApiKeyPermissionsModal(id, name, permissions) {
  _editApiKeyId = id;
  document.getElementById("editApiKeyPermissionsDesc").textContent =
    `Editing permissions for "${name}".`;
  buildPermissionCheckboxes("editApiKeyPermissionsList", permissions);
  const btn = document.getElementById("editApiKeyPermissionsSaveBtn");
  btn.classList.remove("is-loading");
  btn.disabled = false;
  openModal("editApiKeyPermissionsModal");
}

function closeEditApiKeyPermissionsModal() {
  closeModal("editApiKeyPermissionsModal");
  _editApiKeyId = null;
}

async function saveApiKeyPermissions() {
  const permissions = getCheckedPermissions("editApiKeyPermissionsList");
  if (!permissions.length) {
    notify("error", "Permission required", "Select at least one permission.");
    return;
  }
  const btn = document.getElementById("editApiKeyPermissionsSaveBtn");
  btn.classList.add("is-loading");
  btn.disabled = true;
  const result = await apiRequest(
    `/api/admin/api-keys/${_editApiKeyId}/permissions`,
    {
      method: "PATCH",
      body: { permissions },
    },
  );
  btn.classList.remove("is-loading");
  btn.disabled = false;
  if (result.ok) {
    closeEditApiKeyPermissionsModal();
    loadApiKeys();
    notify("success", "Permissions saved");
  }
}

function openEditApiKeyDatesModal(id, name, activeFrom, activeUntil) {
  _editApiKeyId = id;
  document.getElementById("editApiKeyDatesDesc").textContent =
    `Set validity window for "${name}".`;
  document.getElementById("editApiKeyActiveFrom").value = activeFrom
    ? activeFrom.substring(0, 10)
    : "";
  document.getElementById("editApiKeyActiveUntil").value = activeUntil
    ? activeUntil.substring(0, 10)
    : "";
  const btn = document.getElementById("editApiKeyDatesSaveBtn");
  btn.classList.remove("is-loading");
  btn.disabled = false;
  openModal("editApiKeyDatesModal");
}

function closeEditApiKeyDatesModal() {
  closeModal("editApiKeyDatesModal");
  _editApiKeyId = null;
}

async function saveApiKeyDates() {
  const activeFrom =
    document.getElementById("editApiKeyActiveFrom").value || null;
  const activeUntil =
    document.getElementById("editApiKeyActiveUntil").value || null;
  if (
    activeFrom &&
    activeUntil &&
    new Date(activeUntil) <= new Date(activeFrom)
  ) {
    notify("error", "Invalid dates", "Set active until after active from.");
    return;
  }

  const btn = document.getElementById("editApiKeyDatesSaveBtn");
  btn.classList.add("is-loading");
  btn.disabled = true;
  const body = { activeFrom, activeUntil };
  const result = await apiRequest(
    `/api/admin/api-keys/${_editApiKeyId}/dates`,
    { method: "PATCH", body },
  );
  btn.classList.remove("is-loading");
  btn.disabled = false;
  if (result.ok) {
    closeEditApiKeyDatesModal();
    loadApiKeys();
    notify("success", "Validity window saved");
  }
}
