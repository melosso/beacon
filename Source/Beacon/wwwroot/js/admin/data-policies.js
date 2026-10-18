// DATA POLICIES SECTION
function toggleDataPoliciesEnabled(enabled) {
  saveSetting("dataPoliciesEnabled", enabled);
  const controls = document.getElementById("data-policies-controls");
  if (controls) {
    controls.classList.toggle("is-disabled", !enabled);
  }
}

async function loadDataPoliciesSection() {
  const el = document.getElementById("setting-dataPoliciesEnabled");
  if (el) {
    el.checked = !!appSettings.dataPoliciesEnabled;
    toggleDataPoliciesEnabled(!!appSettings.dataPoliciesEnabled);
  }
  const cronEl = document.getElementById("setting-dataPolicyCron");
  if (cronEl) cronEl.value = appSettings.dataPolicyCron || "0 0 * * *";
  const rpe = document.getElementById("setting-retentionPurgeEnabled");
  if (rpe) rpe.checked = !!appSettings.retentionPurgeEnabled;
  const rpra = document.getElementById("setting-retentionPurgeRequireApproval");
  if (rpra) rpra.checked = !!appSettings.retentionPurgeRequireApproval;
  const pce = document.getElementById(
    "setting-pendingConfirmationPurgeEnabled",
  );
  if (pce) pce.checked = !!appSettings.pendingConfirmationPurgeEnabled;
  const pcra = document.getElementById(
    "setting-pendingConfirmationPurgeRequireApproval",
  );
  if (pcra)
    pcra.checked = !!appSettings.pendingConfirmationPurgeRequireApproval;
  await loadWorkflowTasks();
}

// Track tasks actioned in this session to prevent duplicate actions and enable instant UI updatess
const _locallyActionedIds = new Set();
let _workflowAllTasks = [];
let _workflowPage = 1;
let _workflowPageSize = 25;
let _workflowArchivedVisible = false;

// Audit
let auditFilterType = "id"; // 'id' | 'email'
let auditFilterEmail = null; // used when auditFilterType === 'email'

async function loadAudit() {
  const params = new URLSearchParams();
  if (auditFilterBucket) params.set("bucket", auditFilterBucket);
  if (auditFilterIdentity && auditFilterType === "id")
    params.set("emailHash", auditFilterIdentity);
  if (auditFilterEmail && auditFilterType === "email")
    params.set("emailSearch", auditFilterEmail);
  params.set("page", auditCurrentPage);
  params.set("size", auditPageSize);

  const urlParams = { view: "audit" };
  if (auditFilterBucket) urlParams.bucket = auditFilterBucket;
  if (auditFilterIdentity) urlParams.identity = auditFilterIdentity;
  if (auditCurrentPage > 1) urlParams.page = auditCurrentPage;
  updateUrl(urlParams);

  renderAuditHeader();

  const tbody = document.getElementById("auditBody");
  paint(tbody, html`<tr><td class="table-empty" colspan="8">Loading…</td></tr>`);

  const res = await apiRequest(`/api/admin/audit?${params}`);
  if (!res.ok) {
    paint(tbody, html`<tr><td class="table-empty" colspan="8">Failed to load audit log.</td></tr>`);
    return;
  }

  auditTotalRecords = res.data.total;
  const msg = auditFilterBucket || auditFilterIdentity || auditFilterEmail
    ? "No audit entries match the filter."
    : "No audit entries.";
  paint(tbody, res.data.records.length === 0
    ? html`<tr><td class="table-empty" colspan="8">${msg}</td></tr>`
    : html`${res.data.records.map(renderAuditRow)}`);
  updateAuditPagination();
}

function renderAuditHeader() {
  const thead = document.getElementById("auditTableHead");
  if (!thead) return;
  paint(thead, html`
    <th>Timestamp</th>
    <th>${searchColumn({
      label: "Email or ID", title: "Filter by identity", actionLabel: "Filter",
      ids: { trigger: "auditIdentitySearchTrigger", popover: "auditIdentityPopover", input: "auditIdentityInput" },
      type: auditFilterType, query: auditFilterType === "email" ? auditFilterEmail : auditFilterIdentity,
      idLabel: "Filter by identity hash (partial)", emailLabel: "Filter by email — use * as wildcard",
      onToggle: toggleAuditIdentityPopover, onType: setAuditFilterType, onSearch: applyAuditIdentityFilter, onClear: clearAuditIdentityFilter,
    })}</th>
    <th>${searchColumn({
      label: "Bucket", title: "Filter by bucket", actionLabel: "Filter",
      ids: { trigger: "auditBucketSearchTrigger", popover: "auditBucketPopover", input: "auditBucketInput" },
      fixedLabel: "Filter by bucket name", placeholder: "e.g., newsletter", query: auditFilterBucket,
      onToggle: toggleAuditBucketPopover, onSearch: applyAuditBucketFilter, onClear: clearAuditBucketFilter,
    })}</th>
    <th>Permission</th>
    <th>Change</th>
    <th>Source</th>
    <th>Actor</th>
    <th></th>`);
}

function toggleAuditIdentityPopover(event) {
  event.stopPropagation();
  const popover = document.getElementById("auditIdentityPopover");
  const trigger = document.getElementById("auditIdentitySearchTrigger");
  const isOpen = popover?.classList.contains("open");
  document
    .querySelectorAll(".search-popover.open")
    .forEach((p) => p.classList.remove("open"));
  document
    .querySelectorAll(".search-trigger.active")
    .forEach((t) => t.classList.remove("active"));
  if (!isOpen) {
    popover?.classList.add("open");
    trigger?.classList.add("active");
    setTimeout(
      () => document.getElementById("auditIdentityInput")?.focus(),
      50,
    );
  }
}

function toggleAuditBucketPopover(event) {
  event.stopPropagation();
  const popover = document.getElementById("auditBucketPopover");
  const trigger = document.getElementById("auditBucketSearchTrigger");
  const isOpen = popover?.classList.contains("open");
  document
    .querySelectorAll(".search-popover.open")
    .forEach((p) => p.classList.remove("open"));
  document
    .querySelectorAll(".search-trigger.active")
    .forEach((t) => t.classList.remove("active"));
  if (!isOpen) {
    popover?.classList.add("open");
    trigger?.classList.add("active");
    setTimeout(() => document.getElementById("auditBucketInput")?.focus(), 50);
  }
}

function setAuditFilterType(type) {
  auditFilterType = type;
  document
    .querySelectorAll("#auditIdentityPopover .search-type-btn")
    .forEach((btn) => {
      btn.classList.toggle(
        "active",
        btn.textContent.trim() === (type === "id" ? "By ID" : "By email"),
      );
    });
  const label = document.querySelector("#auditIdentityPopover label");
  if (label)
    label.textContent =
      type === "id"
        ? "Filter by identity hash (partial)"
        : "Filter by email — use * as wildcard";
  const input = document.getElementById("auditIdentityInput");
  if (input) {
    input.placeholder = type === "id" ? "e.g., a1b2*" : "e.g., *@gmail.com";
    input.value = "";
    input.focus();
  }
}

function applyAuditIdentityFilter() {
  const val = document.getElementById("auditIdentityInput")?.value.trim();
  if (auditFilterType === "email") {
    auditFilterEmail = val || null;
    auditFilterIdentity = null;
  } else {
    auditFilterIdentity = val || null;
    auditFilterEmail = null;
  }
  auditCurrentPage = 1;
  document.getElementById("auditIdentityPopover")?.classList.remove("open");
  document
    .getElementById("auditIdentitySearchTrigger")
    ?.classList.remove("active");
  loadAudit();
}

function applyAuditBucketFilter() {
  const val = document.getElementById("auditBucketInput")?.value.trim();
  auditFilterBucket = val || null;
  auditCurrentPage = 1;
  document.getElementById("auditBucketPopover")?.classList.remove("open");
  document
    .getElementById("auditBucketSearchTrigger")
    ?.classList.remove("active");
  loadAudit();
}

function clearAuditIdentityFilter() {
  auditFilterIdentity = null;
  auditFilterEmail = null;
  auditCurrentPage = 1;
  document.getElementById("auditIdentityPopover")?.classList.remove("open");
  document
    .getElementById("auditIdentitySearchTrigger")
    ?.classList.remove("active");
  loadAudit();
}

function clearAuditBucketFilter() {
  auditFilterBucket = null;
  auditCurrentPage = 1;
  document.getElementById("auditBucketPopover")?.classList.remove("open");
  document
    .getElementById("auditBucketSearchTrigger")
    ?.classList.remove("active");
  loadAudit();
}

const AUDIT_STATUS = { OptedIn: ["opted-in", "Opted in"], OptedOut: ["opted-out", "Opted out"] };

function auditStatusChip(status) {
  if (!status) return html`<span class="status-chip">None</span>`;
  const [cls, label] = AUDIT_STATUS[status] || ["pending", status];
  return html`<span class="status-chip ${cls}">${label}</span>`;
}

function renderAuditRow(e) {
  const sourceLabel = { Url: "User", Api: "API", Admin: "Administrator" }[e.source] || e.source;
  return html`
    <tr>
      <td>${formatDate(e.changedAt)}</td>
      <td>
        <span class="tooltip-wrapper" @click=${() => auditIdentityClick(e.emailHash)} @dblclick=${() => auditIdentityDblClick(e.emailHash)}>
          ${e.email
            ? html`<span class="email-text cursor-pointer">${e.email}</span>`
            : html`<span class="email-hash cursor-pointer">${e.displayId}</span>`}${e.name ? html`<span class="cell-sub">${e.name}</span>` : nothing}
          <span class="tooltip">Click to filter · double-click to copy ID</span>
        </span>
      </td>
      <td><span class="bucket-name cursor-pointer" @click=${() => showAuditForBucket(e.bucket)}>${e.bucket}</span></td>
      <td>${e.permission}</td>
      <td class="nowrap">${auditStatusChip(e.oldStatus)} → ${auditStatusChip(e.newStatus)}</td>
      <td>${sourceLabel}</td>
      <td>${e.actorId || html`<span class="muted">—</span>`}</td>
      <td><div class="row-actions">
        <span class="tooltip-wrapper">
          <button class="btn-actions" aria-label="Custom fields" @click=${() => openAuditCustomFields(e.customFields)}>:</button>
          <span class="tooltip tooltip-above tooltip-right">Custom fields</span>
        </span>
      </div></td>
    </tr>`;
}

// customFields is a JSON object string, or null.
function openAuditCustomFields(customFields) {
  let fields = {};
  try {
    fields = JSON.parse(customFields || "{}") || {};
  } catch { /* Malformed JSON: show none. */ }
  const entries = Object.entries(fields);
  paint(document.getElementById("auditCustomFieldsList"), entries.length
    ? html`${entries.map(([k, v]) => html`
        <div class="custom-field-row">
          <span class="custom-field-key">${k}</span>
          <span class="custom-field-value">${String(v)}</span>
        </div>`)}`
    : html`<p class="list-empty">No custom fields.</p>`);
  openModal("auditCustomFieldsModal");
}

function closeAuditCustomFieldsModal() {
  closeModal("auditCustomFieldsModal");
}

function auditIdentityClick(hash) {
  clearTimeout(_auditClickTimer);
  _auditClickTimer = setTimeout(() => showAuditForIdentity(hash), 250);
}

function auditIdentityDblClick(hash) {
  clearTimeout(_auditClickTimer);
  copyTextNow(hash);
}

function showAuditForBucket(bucket) {
  auditFilterBucket = bucket;
  auditFilterIdentity = null;
  auditCurrentPage = 1;
  showView("audit");
}

function showAuditForIdentity(hash) {
  auditFilterBucket = null;
  auditFilterIdentity = hash;
  auditCurrentPage = 1;
  showView("audit");
}

function showAuditForBucketAndIdentity(bucket, hash) {
  auditFilterBucket = bucket;
  auditFilterIdentity = hash;
  auditCurrentPage = 1;
  showView("audit");
}

function clearAuditFilter() {
  auditFilterBucket = null;
  auditFilterIdentity = null;
  auditCurrentPage = 1;
  loadAudit();
}

function updateAuditPageSize() {
  auditPageSize = parseInt(
    document.getElementById("auditPageSizeSelect").value,
  );
  auditCurrentPage = 1;
  loadAudit();
}

function changeAuditPage(delta) {
  const maxPage = Math.ceil(auditTotalRecords / auditPageSize) || 1;
  auditCurrentPage = Math.max(1, Math.min(auditCurrentPage + delta, maxPage));
  loadAudit();
}

function updateAuditPagination() {
  const maxPage = Math.ceil(auditTotalRecords / auditPageSize) || 1;
  const start =
    auditTotalRecords === 0 ? 0 : (auditCurrentPage - 1) * auditPageSize + 1;
  const end = Math.min(auditCurrentPage * auditPageSize, auditTotalRecords);
  const info = document.getElementById("auditPaginationInfo");
  if (info)
    info.textContent =
      auditTotalRecords === 0
        ? "No entries"
        : `${start}–${end} of ${auditTotalRecords}`;
  const prev = document.getElementById("auditPrevBtn");
  const next = document.getElementById("auditNextBtn");
  if (prev) prev.disabled = auditCurrentPage <= 1;
  if (next) next.disabled = auditCurrentPage >= maxPage;
}
// /Audit

const _workflowTypeLabels = {
  RetentionPurge: "Opted-out anonymisation",
  PendingConfirmationPurge: "Pending confirmation cleanup",
};

async function loadWorkflowPage() {
  const notice = document.getElementById("workflow-policy-notice");
  const noPolicies =
    !appSettings.retentionPurgeEnabled &&
    !appSettings.pendingConfirmationPurgeEnabled;
  if (notice) notice.hidden = !noPolicies;
  _locallyActionedIds.clear();
  _workflowArchivedVisible = false;
  _workflowPage = 1;
  const btn = document.getElementById("workflowArchiveToggle");
  if (btn) btn.textContent = "Show archived";
  await loadWorkflowTasks();
}

async function loadWorkflowTasks() {
  const res = await apiRequest("/api/admin/data-policies/tasks?limit=200");
  if (!res.ok || !res.data) return;
  _workflowAllTasks = res.data;

  // Settings panel: last 5 completed/failed only
  renderSettingsRunHistory(
    _workflowAllTasks
      .filter((t) => t.status === "Completed" || t.status === "Failed")
      .slice(0, 5),
  );

  // Workflow page
  renderWorkflowTable();
}

const WORKFLOW_BADGES = {
  PendingApproval: html`<span class="status-badge pending-approval">Pending approval</span>`,
  Completed: html`<span class="status-badge completed">Completed</span>`,
  Failed: html`<span class="status-badge failed">Failed</span>`,
  Rejected: html`<span class="status-badge rejected">Rejected</span>`,
};
const SHORT_DATE = { month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" };

function workflowStatus(t) {
  return html`${WORKFLOW_BADGES[t.status] || WORKFLOW_BADGES.Rejected}${t.errorMessage
    ? html`<span class="info-tip" title=${t.errorMessage} aria-label=${t.errorMessage}><svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/></svg></span>`
    : nothing}`;
}

function visibleWorkflowTasks() {
  return _workflowArchivedVisible
    ? _workflowAllTasks.filter((t) => t.status !== "Running" && t.status !== "Pending")
    : _workflowAllTasks.filter((t) => t.status === "PendingApproval" || _locallyActionedIds.has(t.id));
}

function renderWorkflowTable() {
  const tasks = visibleWorkflowTasks().sort((a, b) => new Date(b.scheduledAt) - new Date(a.scheduledAt));
  const total = tasks.length;
  const totalPages = Math.ceil(total / _workflowPageSize) || 1;
  _workflowPage = Math.min(_workflowPage, totalPages);
  const start = (_workflowPage - 1) * _workflowPageSize;
  const page = tasks.slice(start, start + _workflowPageSize);

  const tbody = document.getElementById("workflowTbody");
  if (!tbody) return;

  const msg = _workflowArchivedVisible ? "No runs." : "No items awaiting approval.";
  paint(tbody, total === 0
    ? html`<tr><td class="table-empty" colspan="6">${msg}</td></tr>`
    : html`${page.map((t) => html`
        <tr>
          <td>${_workflowTypeLabels[t.taskType] || t.taskType}</td>
          <td>${t.triggeredBy}</td>
          <td>${t.scheduledAt ? new Date(t.scheduledAt).toLocaleString(undefined, SHORT_DATE) : "—"}</td>
          <td>${t.status === "Completed" || t.status === "Failed" ? t.recordsAffected : "—"}</td>
          <td>${workflowStatus(t)}</td>
          <td>${t.status === "PendingApproval" ? html`<div class="inline-actions nowrap">
            <button class="btn btn-outline btn-sm" @click=${(e) => actOnWorkflowTask(t.id, "approve", e.currentTarget)}>Approve</button>
            <button class="btn btn-outline btn-sm text-muted" @click=${(e) => actOnWorkflowTask(t.id, "reject", e.currentTarget)}>Reject</button>
          </div>` : nothing}</td>
        </tr>`)}`);

  // Pagination
  const bar = document.getElementById("workflowPaginationBar");
  if (bar) bar.hidden = false;
  const startN = total === 0 ? 0 : start + 1;
  const endN = Math.min(start + _workflowPageSize, total);
  const info = document.getElementById("workflowPaginationInfo");
  if (info) info.textContent = `Showing ${startN} to ${endN} of ${total}`;
  const prevBtn = document.getElementById("workflowPrevBtn");
  const nextBtn = document.getElementById("workflowNextBtn");
  if (prevBtn) prevBtn.disabled = _workflowPage <= 1;
  if (nextBtn) nextBtn.disabled = _workflowPage >= totalPages;
}

function changeWorkflowPage(dir) {
  const tasks = visibleWorkflowTasks();
  const totalPages = Math.ceil(tasks.length / _workflowPageSize) || 1;
  _workflowPage = Math.max(1, Math.min(totalPages, _workflowPage + dir));
  renderWorkflowTable();
}

function updateWorkflowPageSize() {
  const el = document.getElementById("workflowPageSize");
  if (el) _workflowPageSize = parseInt(el.value, 10);
  _workflowPage = 1;
  renderWorkflowTable();
}

function toggleWorkflowArchived() {
  _workflowArchivedVisible = !_workflowArchivedVisible;
  _workflowPage = 1;
  const btn = document.getElementById("workflowArchiveToggle");
  if (btn)
    btn.textContent = _workflowArchivedVisible
      ? "Hide archived"
      : "Show archived";
  renderWorkflowTable();
}

function renderSettingsRunHistory(tasks) {
  const tbody = document.getElementById("workflowTasksTbody");
  if (!tbody) return;
  paint(tbody, !tasks?.length
    ? html`<tr><td class="table-empty" colspan="6">No completed runs.</td></tr>`
    : html`${tasks.map((t) => {
        const started = t.startedAt ? new Date(t.startedAt) : null;
        const durationMs = started && t.completedAt ? new Date(t.completedAt) - started : null;
        const duration = durationMs === null ? "—" : durationMs < 1000 ? `${durationMs}ms` : `${(durationMs / 1000).toFixed(1)}s`;
        return html`<tr>
          <td>${_workflowTypeLabels[t.taskType] || t.taskType}</td>
          <td>${t.triggeredBy}</td>
          <td>${started ? started.toLocaleString(undefined, SHORT_DATE) : "—"}</td>
          <td>${duration}</td>
          <td>${t.recordsAffected}</td>
          <td>${workflowStatus(t)}</td>
        </tr>`;
      })}`);
}

async function actOnWorkflowTask(id, action, button) {
  const buttons = button.parentElement.querySelectorAll("button");
  buttons.forEach((b) => (b.disabled = true));
  const res = await apiRequest(`/api/admin/data-policies/tasks/${encodeURIComponent(id)}/${action}`, { method: "POST" });
  if (!res.ok || !res.data) {
    buttons.forEach((b) => (b.disabled = false));
    if (res.ok) notify("error", action === "approve" ? "Approval failed" : "Rejection failed", "The server returned no task.");
    return;
  }
  _locallyActionedIds.add(id);
  const idx = _workflowAllTasks.findIndex((t) => t.id === id);
  if (idx >= 0) _workflowAllTasks[idx] = res.data;
  renderWorkflowTable();
  if (action !== "approve") return;
  if (res.data.status === "Completed") notify("success", "Policy executed", `${res.data.recordsAffected} record(s) affected.`);
  else notify("error", "Execution failed", res.data.errorMessage || "An error occurred.");
}

async function triggerDataPoliciesRun() {
  // Update both possible Run Now buttons (settings panel + workflow page)
  const btns = ["dataPoliciesRunBtn", "workflowRunBtn"]
    .map((id) => document.getElementById(id))
    .filter(Boolean);
  btns.forEach((b) => {
    b.classList.add("is-loading");
    b.disabled = true;
  });
  const res = await apiRequest("/api/admin/data-policies/run", {
    method: "POST",
  });
  if (res.ok || res.status === 202) {
    notify("success", "Run started", "Policies run in the background.");
    // Poll twice: once quickly, once after giving the worker time to finish
    setTimeout(loadWorkflowTasks, 1500);
    setTimeout(loadWorkflowTasks, 6000);
  } else {
    notify("error", "Run failed", "The run did not start. Retry.");
  }
  btns.forEach((b) => {
    b.classList.remove("is-loading");
    b.disabled = false;
  });
}

// RETENTION PURGE MODAL
function openRetentionPurgeModal() {
  const el = document.getElementById("setting-retentionPurgeDays");
  if (el) el.value = appSettings.retentionPurgeDays ?? 1095;
  openModal("retentionPurgeModal");
}
function closeRetentionPurgeModal() {
  closeModal("retentionPurgeModal");
}
function saveRetentionPurgeDays() {
  const days = parseInt(
    document.getElementById("setting-retentionPurgeDays").value,
    10,
  );
  if (isNaN(days) || days < 1) {
    notify("warning", "Invalid value", "Enter a number greater than 0.");
    return;
  }
  saveSetting("retentionPurgeDays", days);
  closeRetentionPurgeModal();
}

// PENDING CONFIRMATION MODAL
function openPendingConfirmationModal() {
  const el = document.getElementById("setting-pendingConfirmationPurgeDays");
  if (el) el.value = appSettings.pendingConfirmationPurgeDays ?? 30;
  openModal("pendingConfirmationModal");
}
function closePendingConfirmationModal() {
  closeModal("pendingConfirmationModal");
}
function savePendingConfirmationPurgeDays() {
  const days = parseInt(
    document.getElementById("setting-pendingConfirmationPurgeDays").value,
    10,
  );
  if (isNaN(days) || days < 1) {
    notify("warning", "Invalid value", "Enter a number greater than 0.");
    return;
  }
  saveSetting("pendingConfirmationPurgeDays", days);
  closePendingConfirmationModal();
}
