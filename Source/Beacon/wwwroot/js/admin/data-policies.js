// DATA POLICIES SECTION
async function toggleDataPoliciesEnabled(enabled) {
  await saveSettingImmediate("dataPoliciesEnabled", enabled);
  syncDataPoliciesControls(enabled);
}

function syncDataPoliciesControls(enabled) {
  document
    .getElementById("data-policies-controls")
    ?.classList.toggle("is-disabled", !enabled);
}

async function loadDataPoliciesSection() {
  const el = document.getElementById("setting-dataPoliciesEnabled");
  if (el) {
    el.checked = !!appSettings.dataPoliciesEnabled;
    syncDataPoliciesControls(!!appSettings.dataPoliciesEnabled);
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
  const isCurrent = latestCall("audit");
  const params = new URLSearchParams();
  if (auditFilterBucket) params.set("bucket", auditFilterBucket);
  if (auditFilterIdentity && auditFilterType === "id")
    params.set("emailHash", auditFilterIdentity);
  if (auditFilterEmail && auditFilterType === "email")
    params.set("emailSearch", auditFilterEmail);
  if (auditFilterActor) params.set("actor", auditFilterActor);
  params.set("page", auditCurrentPage);
  params.set("size", auditPageSize);

  const urlParams = { view: "audit" };
  if (auditFilterBucket) urlParams.bucket = auditFilterBucket;
  if (auditFilterIdentity) urlParams.identity = auditFilterIdentity;
  if (auditFilterActor) urlParams.actor = auditFilterActor;
  if (auditCurrentPage > 1) urlParams.page = auditCurrentPage;
  updateUrl(urlParams);

  renderAuditHeader();

  const tbody = document.getElementById("auditBody");
  paint(
    tbody,
    html`<tr><td class="table-empty" colspan="8">Loading…</td></tr>`,
  );

  const res = await apiRequest(`/api/admin/audit?${params}`);
  if (!isCurrent()) return;
  if (!res.ok) {
    paint(
      tbody,
      html`<tr><td class="table-empty" colspan="8">Failed to load audit log.</td></tr>`,
    );
    return;
  }

  auditTotalRecords = res.data.total;
  const msg =
    auditFilterBucket || auditFilterIdentity || auditFilterEmail
      ? "No audit entries match the filter."
      : "No audit entries.";
  paint(
    tbody,
    res.data.records.length === 0
      ? html`<tr><td class="table-empty" colspan="8">${msg}</td></tr>`
      : html`${res.data.records.map(renderAuditRow)}`,
  );
  updateAuditPagination();
}

function renderAuditHeader() {
  const thead = document.getElementById("auditTableHead");
  if (!thead) return;
  paint(
    thead,
    html`
    <th>Timestamp</th>
    <th>${searchColumn({
      label: "Email or ID",
      title: "Filter by identity",
      actionLabel: "Filter",
      ids: {
        trigger: "auditIdentitySearchTrigger",
        popover: "auditIdentityPopover",
        input: "auditIdentityInput",
      },
      type: auditFilterType,
      query:
        auditFilterType === "email" ? auditFilterEmail : auditFilterIdentity,
      idLabel: "Filter by identity hash (partial)",
      emailLabel: "Filter by email",
      onToggle: toggleAuditIdentityPopover,
      onType: setAuditFilterType,
      onSearch: applyAuditIdentityFilter,
      onClear: clearAuditIdentityFilter,
    })}</th>
    <th>${searchColumn({
      label: "Bucket",
      title: "Filter by bucket",
      actionLabel: "Filter",
      ids: {
        trigger: "auditBucketSearchTrigger",
        popover: "auditBucketPopover",
        input: "auditBucketInput",
      },
      fixedLabel: "Filter by bucket name",
      placeholder: "e.g., newsletter",
      query: auditFilterBucket,
      onToggle: toggleAuditBucketPopover,
      onSearch: applyAuditBucketFilter,
      onClear: clearAuditBucketFilter,
    })}</th>
    <th>Permission</th>
    <th>Change</th>
    <th>Source</th>
    <th>${searchColumn({
      label: "Actor",
      title: "Filter by actor",
      actionLabel: "Filter",
      ids: {
        trigger: "auditActorSearchTrigger",
        popover: "auditActorPopover",
        input: "auditActorInput",
      },
      fixedLabel: "Filter by actor or workflow ID",
      placeholder: "e.g., WF-1A2B3C4D",
      query: auditFilterActor,
      onToggle: (e) => toggleColumnSearch(e, "auditActor"),
      onSearch: applyAuditActorFilter,
      onClear: clearAuditActorFilter,
    })}</th>
    <th></th>`,
  );
}

// Column search popovers: ids are <prefix>Popover, <prefix>SearchTrigger, <prefix>Input.
function toggleColumnSearch(event, prefix) {
  event.stopPropagation();
  const popover = document.getElementById(`${prefix}Popover`);
  const isOpen = popover?.classList.contains("open");
  document
    .querySelectorAll(".search-popover.open")
    .forEach((p) => p.classList.remove("open"));
  document
    .querySelectorAll(".search-trigger.active")
    .forEach((t) => t.classList.remove("active"));
  if (isOpen) return;
  popover?.classList.add("open");
  document.getElementById(`${prefix}SearchTrigger`)?.classList.add("active");
  setTimeout(() => document.getElementById(`${prefix}Input`)?.focus(), 50);
}

function closeColumnSearch(prefix) {
  document.getElementById(`${prefix}Popover`)?.classList.remove("open");
  document.getElementById(`${prefix}SearchTrigger`)?.classList.remove("active");
}

const toggleAuditIdentityPopover = (e) => toggleColumnSearch(e, "auditIdentity");
const toggleAuditBucketPopover = (e) => toggleColumnSearch(e, "auditBucket");

function applyAuditActorFilter() {
  auditFilterActor = document.getElementById("auditActorInput")?.value.trim() || null;
  auditCurrentPage = 1;
  closeColumnSearch("auditActor");
  loadAudit();
}

function clearAuditActorFilter() {
  auditFilterActor = null;
  auditCurrentPage = 1;
  closeColumnSearch("auditActor");
  loadAudit();
}

function showAuditForActor(actor) {
  auditFilterBucket = null;
  auditFilterIdentity = null;
  auditFilterActor = actor;
  auditCurrentPage = 1;
  showView("audit");
}

function setAuditFilterType(type) {
  auditFilterType = type;
  const input = applySearchType("auditIdentityPopover", type);
  if (input) input.value = "";
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

const AUDIT_STATUS = {
  OptedIn: ["opted-in", "Opted in"],
  OptedOut: ["opted-out", "Opted out"],
};

function auditStatusChip(status) {
  if (!status) return html`<span class="status-chip">None</span>`;
  const [cls, label] = AUDIT_STATUS[status] || ["pending", status];
  return html`<span class="status-chip ${cls}">${label}</span>`;
}

function renderAuditRow(e) {
  const sourceLabel =
    { Url: "User", Api: "API", Admin: "Administrator", AdminLink: "Admin via link", Erasure: "Erasure" }[e.source] || e.source;
  return html`
    <tr>
      <td>${formatDate(e.changedAt)}</td>
      <td>
        <span @click=${() => auditIdentityClick(e.emailHash)} @dblclick=${() => auditIdentityDblClick(e.emailHash)} data-tooltip="Click to filter · double-click to copy ID">
          ${
            e.email
              ? html`<span class="email-text cursor-pointer">${e.email}</span>`
              : html`<span class="email-hash cursor-pointer">${e.displayId}</span>`
          }${e.name ? html`<span class="cell-sub">${e.name}</span>` : nothing}
          
        </span>
      </td>
      <td><span class="bucket-name cursor-pointer" @click=${() => showAuditForBucket(e.bucket)}>${e.bucket}</span></td>
      <td>${e.permission}</td>
      <td class="nowrap">${e.source === "Erasure" && e.oldStatus === e.newStatus ? html`<span class="status-chip">Personal data removed</span>` : html`${auditStatusChip(e.oldStatus)} <svg class="audit-arrow" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" role="img" aria-label="to"><path d="M5 12h14M12 5l7 7-7 7"/></svg> ${auditStatusChip(e.newStatus)}`}</td>
      <td>${sourceLabel}${e.issuedBy && e.source !== "AdminLink" ? html`<span class="cell-sub">Link from ${e.issuedBy}</span>` : nothing}</td>
      <td>${e.actorId ? html`<span class=${/^WF-[0-9A-F]{8}$/.test(e.actorId) ? "mono nowrap" : ""}>${e.actorId}</span>` : html`<span class="muted">—</span>`}</td>
      <td><div class="row-actions">
        ${
          e.email
            ? html`<button class="btn-icon btn-icon--sm" aria-label="Open subscriber" @click=${() => openSubscriber(e.emailHash)} data-tooltip="Open subscriber">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M7 17 17 7"/><path d="M7 7h10v10"/></svg>
          </button>`
            : nothing
        }
        <button class="btn-icon btn-icon--sm" aria-label="Custom fields" @click=${() => openAuditCustomFields(e.customFields)} data-tooltip="Custom fields"><svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M8 3H7a2 2 0 0 0-2 2v5a2 2 0 0 1-2 2 2 2 0 0 1 2 2v5c0 1.1.9 2 2 2h1"/><path d="M16 21h1a2 2 0 0 0 2-2v-5c0-1.1.9-2 2-2a2 2 0 0 1-2-2V5a2 2 0 0 0-2-2h-1"/></svg></button>
      </div></td>
    </tr>`;
}

// customFields is a JSON object string, or null.
function openAuditCustomFields(customFields) {
  let fields = {};
  try {
    fields = JSON.parse(customFields || "{}") || {};
  } catch {
    /* Malformed JSON: show none. */
  }
  const entries = Object.entries(fields);
  paint(
    document.getElementById("auditCustomFieldsList"),
    entries.length
      ? html`${entries.map(
          ([k, v]) => html`
        <div class="custom-field-row">
          <span class="custom-field-key">${k}</span>
          <span class="custom-field-value">${String(v)}</span>
        </div>`,
        )}`
      : html`<p class="list-empty">No custom fields.</p>`,
  );
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
  auditFilterActor = null;
  auditFilterIdentity = null;
  auditCurrentPage = 1;
  showView("audit");
}

function showAuditForIdentity(hash) {
  auditFilterBucket = null;
  auditFilterActor = null;
  auditFilterIdentity = hash;
  auditCurrentPage = 1;
  showView("audit");
}

function showAuditForBucketAndIdentity(bucket, hash) {
  auditFilterBucket = bucket;
  auditFilterActor = null;
  auditFilterIdentity = hash;
  auditCurrentPage = 1;
  showView("audit");
}

function clearAuditFilter() {
  auditFilterBucket = null;
  auditFilterActor = null;
  auditFilterIdentity = null;
  auditCurrentPage = 1;
  loadAudit();
}

function updateAuditPagination() {
  renderPager(document.getElementById("auditPager"), {
    page: auditCurrentPage,
    pageSize: auditPageSize,
    total: auditTotalRecords,
    onPage: (n) => {
      auditCurrentPage = n;
      loadAudit();
    },
    onPageSize: (n) => {
      auditPageSize = n;
      auditCurrentPage = 1;
      loadAudit();
    },
  });
}
// /Audit

const _workflowTypeLabels = {
  RetentionPurge: "Opted-out anonymisation",
  PendingConfirmationPurge: "Pending confirmation cleanup",
};

function noWorkflowPolicies() {
  return (
    !appSettings.retentionPurgeEnabled &&
    !appSettings.pendingConfirmationPurgeEnabled
  );
}

// A manual run executes the enabled policies only: with none enabled there is nothing to run
function syncWorkflowRunButton() {
  const btn = document.getElementById("workflowRunBtn");
  const none = noWorkflowPolicies();
  if (!btn) return;
  btn.disabled = none;
  btn.dataset.tooltip = none ? "Enable a policy to run" : "Run data policies immediately";
}

async function loadWorkflowPage() {
  const notice = document.getElementById("workflow-policy-notice");
  if (notice) notice.hidden = !noWorkflowPolicies();
  syncWorkflowRunButton();
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
  const pending = _workflowAllTasks.filter((t) => t.status === "PendingApproval").length;
  const count = document.getElementById("navWorkflowCount");
  count.textContent = pending > 99 ? "99+" : pending;
  count.dataset.tooltip = `${pending} awaiting approval`;
  count.hidden = pending === 0;

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
const SHORT_DATE = {
  month: "short",
  day: "numeric",
  hour: "2-digit",
  minute: "2-digit",
};

function workflowStatus(t) {
  return html`${WORKFLOW_BADGES[t.status] || WORKFLOW_BADGES.Rejected}${
    t.errorMessage
      ? html`<span class="info-tip" data-tooltip=${t.errorMessage} aria-label=${t.errorMessage}><svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/></svg></span>`
      : nothing
  }`;
}

function visibleWorkflowTasks() {
  return _workflowArchivedVisible
    ? _workflowAllTasks.filter((t) => t.status !== "Running")
    : _workflowAllTasks.filter(
        (t) => t.status === "PendingApproval" || _locallyActionedIds.has(t.id),
      );
}

function renderWorkflowTable() {
  const tasks = visibleWorkflowTasks().sort(
    (a, b) => new Date(b.scheduledAt) - new Date(a.scheduledAt),
  );
  const total = tasks.length;
  const totalPages = Math.ceil(total / _workflowPageSize) || 1;
  _workflowPage = Math.min(_workflowPage, totalPages);
  const start = (_workflowPage - 1) * _workflowPageSize;
  const page = tasks.slice(start, start + _workflowPageSize);

  const tbody = document.getElementById("workflowTbody");
  if (!tbody) return;

  // With no policy on and no history, the notice is the whole page
  const table = document.getElementById("workflowTable");
  if (table)
    table.hidden = noWorkflowPolicies() && _workflowAllTasks.length === 0;

  const msg = _workflowArchivedVisible
    ? "No runs."
    : "No items awaiting approval.";
  paint(
    tbody,
    total === 0
      ? html`<tr><td class="table-empty" colspan="7">${msg}</td></tr>`
      : html`${page.map(
          (t) => html`
        <tr>
          <td>${workflowRef(t)}</td>
          <td>${_workflowTypeLabels[t.taskType] || t.taskType}</td>
          <td>${t.triggeredBy}</td>
          <td>${t.scheduledAt ? parseServerDate(t.scheduledAt).toLocaleString(UI_LOCALE, SHORT_DATE) : "—"}</td>
          <td>${
            t.status === "Completed" || t.status === "Failed"
              ? t.recordsAffected
              : t.pendingRecords != null
                ? html`<button type="button" class="link-btn" data-tooltip="Show records affected on approval" @click=${() => openWorkflowRecords(t)}>${t.pendingRecords}</button>`
                : "—"
          }</td>
          <td>${workflowStatus(t)}</td>
          <td class="col-actions">${
            t.status === "PendingApproval"
              ? html`<div class="inline-actions">
            <button class="btn btn-outline btn-sm" @click=${(e) => actOnWorkflowTask(t.id, "approve", e.currentTarget)}>Approve</button>
            <button class="btn btn-outline btn-sm text-muted" @click=${(e) => actOnWorkflowTask(t.id, "reject", e.currentTarget)}>Reject</button>
          </div>`
              : nothing
          }</td>
        </tr>`,
        )}`,
  );

  const bar = document.getElementById("workflowPaginationBar");
  if (bar) bar.hidden = total === 0;
  renderPager(bar, {
    page: _workflowPage,
    pageSize: _workflowPageSize,
    total,
    sizes: [10, 25, 50],
    onPage: (n) => {
      _workflowPage = n;
      renderWorkflowTable();
    },
    onPageSize: (n) => {
      _workflowPageSize = n;
      _workflowPage = 1;
      renderWorkflowTable();
    },
  });
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
  paint(
    tbody,
    !tasks?.length
      ? html`<tr><td class="table-empty" colspan="7">No completed runs.</td></tr>`
      : html`${tasks.map((t) => {
          const started = t.startedAt ? parseServerDate(t.startedAt) : null;
          const durationMs =
            started && t.completedAt ? parseServerDate(t.completedAt) - started : null;
          const duration =
            durationMs === null
              ? "—"
              : durationMs < 1000
                ? `${durationMs}ms`
                : `${(durationMs / 1000).toFixed(1)}s`;
          return html`<tr>
          <td>${workflowRef(t)}</td>
          <td>${_workflowTypeLabels[t.taskType] || t.taskType}</td>
          <td>${t.triggeredBy}</td>
          <td>${started ? started.toLocaleString(UI_LOCALE, SHORT_DATE) : "—"}</td>
          <td>${duration}</td>
          <td>${t.recordsAffected}</td>
          <td>${workflowStatus(t)}</td>
        </tr>`;
        })}`,
  );
}

// Task reference. Runs that changed records link to their audit entries.
function workflowRef(t) {
  return t.status === "Completed" && t.recordsAffected > 0
    ? html`<button type="button" class="link-btn mono nowrap" data-tooltip="Show audit entries of this run" @click=${() => showAuditForActor(t.reference)}>${t.reference}</button>`
    : html`<span class="mono nowrap">${t.reference}</span>`;
}

// Drill-down: the records a pending task changes on approval.
const _workflowEffects = {
  RetentionPurge: "Approval removes personal data from these opted-out records.",
  PendingConfirmationPurge: "Approval opts out these unconfirmed records and removes their personal data.",
};
let _workflowRecordsTaskId = null;

async function openWorkflowRecords(task) {
  const res = await apiRequest(
    `/api/admin/data-policies/tasks/${encodeURIComponent(task.id)}/records`,
  );
  if (!res.ok) return;
  const { total, records } = res.data;
  _workflowRecordsTaskId = task.id;
  document.getElementById("workflowRecordsTitle").textContent =
    `${task.reference} · ${_workflowTypeLabels[task.taskType] || task.taskType}`;
  const shown =
    records.length < total
      ? `First ${records.length} of ${total} records, oldest first.`
      : `${total} record${total === 1 ? "" : "s"}, oldest first.`;
  document.getElementById("workflowRecordsSummary").textContent =
    `${_workflowEffects[task.taskType] || ""} ${shown}`.trim();
  paint(
    document.getElementById("workflowRecordsBody"),
    records.length
      ? html`${records.map(
          (r) => html`<tr>
            <td>
              <button type="button" class="link-btn" @click=${() => openWorkflowSubscriber(r.emailHash)}>
                ${r.email || html`<span class="email-hash">${r.emailHash.substring(0, 16)}…</span>`}
              </button>
            </td>
            <td>${formatPermission(r.bucket)}</td>
            <td>${formatPermission(r.permission)}</td>
            <td>${parseServerDate(r.changedAt).toLocaleDateString(UI_LOCALE, { day: "numeric", month: "short", year: "numeric" })}</td>
          </tr>`,
        )}`
      : html`<tr><td class="table-empty" colspan="4">No records match. Approval changes nothing.</td></tr>`,
  );
  openModal("workflowRecordsModal");
}

function closeWorkflowRecords() {
  closeModal("workflowRecordsModal");
  _workflowRecordsTaskId = null;
}

function openWorkflowSubscriber(emailHash) {
  closeWorkflowRecords();
  openSubscriber(emailHash);
}

async function approveWorkflowRecords(button) {
  if (!_workflowRecordsTaskId) return;
  await actOnWorkflowTask(_workflowRecordsTaskId, "approve", button);
  closeWorkflowRecords();
}

async function actOnWorkflowTask(id, action, button) {
  const buttons = button.parentElement.querySelectorAll("button");
  buttons.forEach((b) => (b.disabled = true));
  const res = await apiRequest(
    `/api/admin/data-policies/tasks/${encodeURIComponent(id)}/${action}`,
    { method: "POST" },
  );
  if (!res.ok || !res.data) {
    buttons.forEach((b) => (b.disabled = false));
    if (res.ok)
      notify(
        "error",
        action === "approve" ? "Approval failed" : "Rejection failed",
        "The server returned no task.",
      );
    return;
  }
  _locallyActionedIds.add(id);
  const idx = _workflowAllTasks.findIndex((t) => t.id === id);
  if (idx >= 0) _workflowAllTasks[idx] = res.data;
  renderWorkflowTable();
  if (action !== "approve") return;
  if (res.data.status === "Completed")
    notify(
      "success",
      "Policy executed",
      `${res.data.recordsAffected} record(s) affected.`,
    );
  else
    notify(
      "error",
      "Execution failed",
      res.data.errorMessage || "An error occurred.",
    );
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
  syncWorkflowRunButton();
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
async function saveRetentionPurgeDays() {
  const days = parseInt(
    document.getElementById("setting-retentionPurgeDays").value,
    10,
  );
  if (isNaN(days) || days < 1) {
    notify("warning", "Invalid value", "Enter a number greater than 0.");
    return;
  }
  closeRetentionPurgeModal();
  await saveSettingImmediate("retentionPurgeDays", days);
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
async function savePendingConfirmationPurgeDays() {
  const days = parseInt(
    document.getElementById("setting-pendingConfirmationPurgeDays").value,
    10,
  );
  if (isNaN(days) || days < 1) {
    notify("warning", "Invalid value", "Enter a number greater than 0.");
    return;
  }
  closePendingConfirmationModal();
  await saveSettingImmediate("pendingConfirmationPurgeDays", days);
}
