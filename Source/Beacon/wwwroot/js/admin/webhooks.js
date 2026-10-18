// WEBHOOK CONFIGURATION
const WEBHOOK_DEFAULT_BODY = JSON.stringify(
  {
    bucket: "{{bucket}}",
    emailHash: "{{emailHash}}",
    permissions: "{{permissions}}",
    timestamp: "{{timestamp}}",
  },
  null,
  2,
).replace('"{{permissions}}"', "{{permissions}}");

let webhookHeaders = {};

function insertWebhookVar(name) {
  const ta = document.getElementById("webhookBody");
  const start = ta.selectionStart;
  const end = ta.selectionEnd;
  const text = ta.value;
  const varText = `{{${name}}}`;
  ta.value = text.substring(0, start) + varText + text.substring(end);
  ta.selectionStart = ta.selectionEnd = start + varText.length;
  ta.focus();
}

function resetWebhookBodyTemplate() {
  document.getElementById("webhookBody").value = WEBHOOK_DEFAULT_BODY;
  checkWebhookEmailVar(WEBHOOK_DEFAULT_BODY);
  closeWebhookTemplateMenu();
}

function checkWebhookEmailVar(body) {
  const warning = document.getElementById("webhookEmailWarning");
  if (warning)
    warning.style.display = /\{\{\s*email\s*\}\}/i.test(body) ? "" : "none";
}

function toggleWebhookTemplateMenu(trigger) {
  const menu = document.getElementById("webhookTemplateMenu");
  const isOpen = menu.classList.contains("open");
  closeAllMenus();
  if (!isOpen) {
    menu.classList.add("open");
    const rect = trigger.getBoundingClientRect();
    menu.style.top = `${rect.bottom + 4}px`;
    menu.style.left = `${rect.right - menu.offsetWidth}px`;
    openMenuId = "webhookTemplateMenu";
  }
}

function closeWebhookTemplateMenu() {
  document.getElementById("webhookTemplateMenu")?.classList.remove("open");
  if (openMenuId === "webhookTemplateMenu") openMenuId = null;
}

function formatBodyTemplate(template) {
  if (!template) return null;
  try {
    return JSON.stringify(JSON.parse(template), null, 2);
  } catch {
    return template;
  }
}

function toggleOptionsSection(header) {
  header.classList.toggle("expanded");
  const body = header.nextElementSibling;
  body.classList.toggle("open");
}

function setWebhookBadge(configured) {
  const badge = document.getElementById("webhookStatusBadge");
  if (configured) {
    badge.textContent = "Active";
    badge.classList.add("active");
  } else {
    badge.textContent = "Not configured";
    badge.classList.remove("active");
  }
}

async function showOptionsModal(pushState = true) {
  if (!currentBucket) return;

  // Set bucket ID in basic section
  document.getElementById("optionsBucketId").value = currentBucket;

  // Update archive button state
  const archiveBtn = document.getElementById("optionsArchiveBtn");
  archiveBtn.title = currentBucketArchived
    ? "Unarchive Bucket"
    : "Archive Bucket";
  archiveBtn.style.color = currentBucketArchived
    ? "hsl(var(--primary))"
    : "hsl(var(--muted-foreground))";

  // Load bucket permissions
  loadBucketPerms();

  // Reset webhook fields
  webhookHeaders = {};
  document.getElementById("webhookUrl").value = "";
  document.getElementById("webhookMethod").value = "POST";
  document.getElementById("webhookBody").value = WEBHOOK_DEFAULT_BODY;
  paint(document.getElementById("webhookHeadersGrid"), nothing);
  document.getElementById("deleteWebhookBtn").style.display = "none";
  document.getElementById("webhookSecretSection").style.display = "none";
  document.getElementById("webhookSecretValue").value = "";
  setWebhookBadge(false);

  // Expand basic section, collapse webhook by default
  const sections = document.querySelectorAll(
    "#optionsModal .options-section-header",
  );
  sections.forEach((h, i) => {
    const body = h.nextElementSibling;
    if (i === 0) {
      h.classList.add("expanded");
      body.classList.add("open");
    } else {
      h.classList.remove("expanded");
      body.classList.remove("open");
    }
  });

  // Load bucket options (email settings)
  loadBucketOptions();

  // Load existing webhook config if exists
  try {
    const result = await apiRequest(
      `/api/admin/buckets/${encodeURIComponent(currentBucket)}/webhook`,
    );
    if (result.ok && result.data?.configured) {
      const config = result.data;
      document.getElementById("webhookUrl").value = config.url || "";
      document.getElementById("webhookMethod").value = config.method || "POST";
      const webhookBodyVal =
        formatBodyTemplate(config.bodyTemplate) || WEBHOOK_DEFAULT_BODY;
      document.getElementById("webhookBody").value = webhookBodyVal;
      checkWebhookEmailVar(webhookBodyVal);
      webhookHeaders = config.headers || {};
      renderWebhookHeaders();
      document.getElementById("deleteWebhookBtn").style.display = "block";
      setWebhookBadge(true);

      // Auto-expand webhook section if configured
      sections[1]?.classList.add("expanded");
      sections[1]?.nextElementSibling?.classList.add("open");
    }
  } catch (err) {
    // No webhook configured
  }

  // Populate brand identity section
  const biContent = document.getElementById("optionsBrandIdentityContent");
  const biBadge = document.getElementById("optionsBrandBadge");
  const identity = getBrandIdentityForBucket(currentBucket);
  if (biContent) {
    if (identity) {
      const accent = safeColor(identity.settings?.primaryAccent, null);
      paint(biContent, html`<div style="display:flex;align-items:center;padding:0.625rem 0.75rem;background:hsl(var(--muted));border-radius:var(--radius);font-size:0.875rem">${accent
        ? html`<span style="display:inline-block;width:0.875rem;height:0.875rem;border-radius:50%;background:${accent};border:1px solid hsl(var(--border));vertical-align:middle;margin-right:0.375rem;flex-shrink:0"></span>`
        : nothing}<span>${identity.name}</span></div>`);
      if (biBadge) {
        biBadge.textContent = identity.name;
        biBadge.style.display = "";
      }
    } else {
      paint(biContent, html`<p style="font-size:0.875rem;color:hsl(var(--muted-foreground));margin:0">Using default identity.</p>`);
      if (biBadge) biBadge.style.display = "none";
    }
  }

  document.getElementById("optionsModal").style.display = "flex";

  // Update URL to include modal state
  if (pushState) {
    const url = new URL(window.location);
    url.searchParams.set("modal", "options");
    history.pushState(null, "", url);
  }
}

function closeOptionsModal() {
  document.getElementById("optionsModal").style.display = "none";
  webhookHeaders = {};
  bucketPermsData = [];
  document.getElementById("newBucketPermInput").value = "";

  // Remove modal param from URL
  const url = new URL(window.location);
  url.searchParams.delete("modal");
  history.replaceState(null, "", url);
}

async function loadBucketOptions() {
  if (!currentBucket) return;
  const toggle = document.getElementById("bucketDoubleOptIn");
  const label = document.getElementById("bucketDoubleOptInLabel");
  const desc = document.getElementById("bucketDoubleOptInDesc");
  const emailDisabled =
    typeof DISABLE_EMAIL_NOTIFICATIONS !== "undefined" &&
    DISABLE_EMAIL_NOTIFICATIONS;
  const globalEnabled = !emailDisabled && appSettings.enableDoubleOptIn;

  // Default state: inherit global (true), grayed out when global feature is off or email is disabled
  if (toggle) toggle.checked = true;
  if (toggle) toggle.disabled = !globalEnabled;
  if (label) label.style.opacity = globalEnabled ? "1" : "0.5";
  if (desc)
    desc.textContent = globalEnabled
      ? "Subscribers must confirm via email. Disable to opt this bucket out of the global setting."
      : emailDisabled
        ? "Email notifications are disabled by your administrator."
        : "Enable the global double opt-in setting first to configure this option.";

  if (!globalEnabled) return;

  try {
    const result = await apiRequest(
      `/api/admin/buckets/${encodeURIComponent(currentBucket)}/options`,
    );
    if (result.ok && result.data) {
      if (toggle) toggle.checked = result.data.doubleOptIn ?? true;
      const utmEl = document.getElementById("bucketUtmCampaign");
      if (utmEl) utmEl.value = result.data.utmCampaign ?? "";
    }
  } catch {}
}

async function saveBucketUtmCampaign() {
  if (!currentBucket) return;
  const utmEl = document.getElementById("bucketUtmCampaign");
  const utmCampaign = utmEl?.value.trim() || null;
  const doubleOptInToggle = document.getElementById("bucketDoubleOptIn");
  await apiRequest(
    `/api/admin/buckets/${encodeURIComponent(currentBucket)}/options`,
    {
      method: "PUT",
      body: { doubleOptIn: doubleOptInToggle?.checked ?? true, utmCampaign },
    },
  );
}

async function saveBucketDoubleOptIn(value) {
  if (!currentBucket) return;
  const utmEl = document.getElementById("bucketUtmCampaign");
  const utmCampaign = utmEl?.value.trim() || null;
  const res = await apiRequest(
    `/api/admin/buckets/${encodeURIComponent(currentBucket)}/options`,
    {
      method: "PUT",
      body: { doubleOptIn: value, utmCampaign },
    },
  );
  if (res.ok) {
    notify("success", "Saved", "Bucket email settings updated.");
  } else {
    notify("error", "Save Failed", "Failed to save bucket email settings.");
    const toggle = document.getElementById("bucketDoubleOptIn");
    if (toggle) toggle.checked = !value;
  }
}

function addWebhookHeader() {
  const keyInput = document.getElementById("newHeaderKey");
  const valueInput = document.getElementById("newHeaderValue");
  const key = keyInput.value.trim();
  const value = valueInput.value.trim();

  if (!key || !value) return;

  webhookHeaders[key] = value;
  renderWebhookHeaders();

  keyInput.value = "";
  valueInput.value = "";
}

function removeWebhookHeader(key) {
  delete webhookHeaders[key];
  renderWebhookHeaders();
}

function renderWebhookHeaders() {
  const grid = document.getElementById("webhookHeadersGrid");
  const entries = Object.entries(webhookHeaders);

  paint(grid, entries.length === 0
    ? html`<div style="color:hsl(var(--muted-foreground));font-size:0.875rem">No headers configured</div>`
    : html`${entries.map(([key, value]) => html`
        <div class="custom-field-row">
          <div class="custom-field-key">${key}</div>
          <div class="custom-field-value">${value}</div>
          <button type="button" class="btn-icon-small" @click=${() => removeWebhookHeader(key)} title="Remove" aria-label="Remove header">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
              <line x1="18" y1="6" x2="6" y2="18"></line>
              <line x1="6" y1="6" x2="18" y2="18"></line>
            </svg>
          </button>
        </div>`)}`);
}

async function saveWebhook() {
  const url = document.getElementById("webhookUrl").value.trim();
  const method = document.getElementById("webhookMethod").value;
  const bodyTemplate = document.getElementById("webhookBody").value.trim();

  if (!url) {
    notify("error", "Validation Error", "Webhook URL is required");
    return;
  }

  const payload = {
    url,
    method,
    headers: Object.keys(webhookHeaders).length > 0 ? webhookHeaders : null,
    bodyTemplate: bodyTemplate || null,
  };

  const result = await apiRequest(`/api/admin/buckets/${encodeURIComponent(currentBucket)}/webhook`, { method: "POST", body: payload });

  if (result.ok) {
    if (result.data?.signingSecret) {
      document.getElementById("webhookSecretValue").value =
        result.data.signingSecret;
      document.getElementById("webhookSecretSection").style.display = "block";
      document.getElementById("deleteWebhookBtn").style.display = "block";
      setWebhookBadge(true);
      notify(
        "success",
        "Webhook Saved",
        "Configuration saved. Copy the signing secret now. It won't be shown again.",
      );
    } else {
      setWebhookBadge(true);
      notify(
        "success",
        "Webhook Saved",
        "Webhook configuration updated successfully",
      );
      closeOptionsModal();
    }
  }
}

async function deleteWebhook() {
  if (!confirm("Delete webhook configuration? This cannot be undone.")) {
    return;
  }

  const result = await apiRequest(`/api/admin/buckets/${encodeURIComponent(currentBucket)}/webhook`, { method: "DELETE" });
  if (result.ok) {
    notify(
      "success",
      "Webhook Deleted",
      "Webhook configuration removed successfully",
    );
    setWebhookBadge(false);
    closeOptionsModal();
  }
}

// ROW ACTIONS DROPDOWN
let openMenuId = null;

function toggleRowMenu(event, idx) {
  event.stopPropagation();
  toggleMenu(`rowMenu-${idx}`, event.currentTarget);
}

function toggleOverviewMenu(event, idx) {
  event.stopPropagation();
  toggleMenu(`overviewMenu-${idx}`, event.currentTarget);
}

function toggleMenu(menuId, trigger) {
  const menu = document.getElementById(menuId);
  if (!menu) return;

  if (openMenuId && openMenuId !== menuId) {
    document.getElementById(openMenuId)?.classList.remove("open");
  }

  menu.classList.toggle("open");
  openMenuId = menu.classList.contains("open") ? menuId : null;

  if (menu.classList.contains("open") && trigger) {
    const rect = trigger.getBoundingClientRect();
    menu.style.top = `${rect.bottom + 4}px`;
    menu.style.left = `${rect.right - menu.offsetWidth}px`;
  }
}

function positionDropdown(input, dropdown) {
  const rect = input.getBoundingClientRect();
  dropdown.style.top = rect.bottom + "px";
  dropdown.style.left = rect.left + "px";
  dropdown.style.width = rect.width + "px";
}

function closeAllMenus() {
  if (openMenuId) {
    document.getElementById(openMenuId)?.classList.remove("open");
    openMenuId = null;
  }
  document.getElementById("webhookErrorsDropdown")?.classList.remove("open");
  document
    .getElementById("overviewWebhookErrorsDropdown")
    ?.classList.remove("open");
  document.querySelectorAll(".autocomplete-dropdown").forEach((d) => {
    d.style.display = "";
    d.classList.remove("open");
  });
}

document.addEventListener("click", function (e) {
  if (e.target.closest(".autocomplete-wrapper")) return;
  closeAllMenus();
});

// WEBHOOK ERRORS
let webhookErrorsCache = [];
let overviewErrorsCache = []; // flat: [{bucket, ...errorFields}]
let activeErrorDetail = null;

async function loadWebhookErrors(bucket) {
  const trigger = document.getElementById("webhookErrorsTrigger");
  const dropdown = document.getElementById("webhookErrorsDropdown");

  trigger.classList.remove("has-errors");

  const errorsResult = await apiRequest(
    `/api/admin/buckets/${encodeURIComponent(bucket)}/webhook/errors`,
  );
  const errors = errorsResult.ok && errorsResult.data ? errorsResult.data : [];
  webhookErrorsCache = errors;

  trigger.classList.toggle("has-errors", errors.length > 0);
  paint(dropdown, errors.length === 0
    ? EMPTY_NOTIFICATIONS
    : html`<div class="error-header"><span>Recent Errors</span><button class="clear-all-btn" @click=${(e) => { e.stopPropagation(); clearAllWebhookErrors(); }}>Clear All</button></div>
      ${errors.map((err) => html`<div class="error-item" @click=${(e) => { e.stopPropagation(); openErrorDetail(err); }}>
        <div class="error-time">${formatDate(err.occurredAt)}</div>
        <div class="error-msg">${truncate(err.errorMessage, 120)}${errorStatus(err)}</div>
      </div>`)}`);
}

const EMPTY_NOTIFICATIONS = html`<div class="error-header" style="padding-bottom:0.8rem;">Notifications</div><div class="error-item" style="text-align:center;color:hsl(var(--muted-foreground));padding:1rem 0.875rem;cursor:default">No notifications.</div>`;

function truncate(text, max) {
  return text.length > max ? text.substring(0, max) + "…" : text;
}

function errorStatus(err) {
  return err.statusCode ? html`<span class="error-status">${err.statusCode}</span>` : nothing;
}

function openOverviewErrorDetail(error) {
  currentBucket = error.bucket;
  showBucket(error.bucket);
  openErrorDetail(error);
  // Reflect in URL so the modal is directly linkable
  const url = new URL(window.location);
  url.searchParams.set("modal", "error-detail");
  url.searchParams.set("errorId", error.id);
  history.replaceState({}, "", url);
}

function openErrorDetail(error) {
  activeErrorDetail = error;
  document.getElementById("errorDetailTime").textContent = new Date(
    error.occurredAt,
  ).toLocaleString();
  paint(document.getElementById("errorDetailBadge"), errorStatus(error));
  const parts = [];
  if (error.requestMethod && error.requestUrl) parts.push(`${error.requestMethod} ${error.requestUrl}`);
  if (error.attemptCount > 0) parts.push(`${error.attemptCount} attempt${error.attemptCount !== 1 ? "s" : ""}`);
  document.getElementById("errorDetailRequest").textContent = parts.join(" · ");

  document.getElementById("errorDetailMessage").textContent =
    error.errorMessage;

  // Stack trace
  const stackWrap = document.getElementById("errorDetailStackWrap");
  if (error.stackTrace) {
    document.getElementById("errorDetailStack").textContent = error.stackTrace;
    stackWrap.style.display = "block";
  } else {
    stackWrap.style.display = "none";
  }

  closeAllMenus();
  document.getElementById("errorDetailModal").style.display = "flex";
}

function closeErrorDetailModal() {
  document.getElementById("errorDetailModal").style.display = "none";
  activeErrorDetail = null;
  const url = new URL(window.location);
  if (url.searchParams.get("modal") === "error-detail") {
    url.searchParams.delete("modal");
    url.searchParams.delete("errorId");
    history.replaceState({}, "", url);
  }
}

function copyErrorMessage() {
  if (!activeErrorDetail) return;
  const e = activeErrorDetail;
  let text = e.errorMessage;
  const meta = [];
  if (e.requestMethod && e.requestUrl)
    meta.push(`Request: ${e.requestMethod} ${e.requestUrl}`);
  if (e.statusCode) meta.push(`Status: ${e.statusCode}`);
  if (e.attemptCount > 0) meta.push(`Attempts: ${e.attemptCount}`);
  meta.push(`Time: ${new Date(e.occurredAt).toLocaleString()}`);
  if (meta.length) text = meta.join("\n") + "\n\n" + text;
  if (e.stackTrace) text += "\n\nStack Trace:\n" + e.stackTrace;
  clipboardWrite(text).then(() => {
    notify("success", "Copied to clipboard");
  });
}

async function removeWebhookError() {
  if (!activeErrorDetail) return;
  const result = await apiRequest(
    `/api/admin/buckets/${encodeURIComponent(currentBucket)}/webhook/errors/${activeErrorDetail.id}`,
    { method: "DELETE" },
  );
  if (result.ok) {
    closeErrorDetailModal();
    await loadWebhookErrors(currentBucket);
  }
}

async function clearAllWebhookErrors() {
  const result = await apiRequest(
    `/api/admin/buckets/${encodeURIComponent(currentBucket)}/webhook/errors`,
    { method: "DELETE" },
  );
  if (result.ok) {
    await loadWebhookErrors(currentBucket);
  }
}

function toggleWebhookErrors(event) {
  event.stopPropagation();
  const dropdown = document.getElementById("webhookErrorsDropdown");
  const trigger = document.getElementById("webhookErrorsTrigger");
  const isOpen = dropdown.classList.contains("open");

  closeAllMenus();

  if (!isOpen) {
    dropdown.classList.add("open");
    const rect = trigger.getBoundingClientRect();
    dropdown.style.top = `${rect.bottom + 4}px`;
    dropdown.style.left = `${Math.max(0, rect.right - dropdown.offsetWidth)}px`;
  }
}

function toggleOverviewWebhookErrors(event) {
  event.stopPropagation();
  const dropdown = document.getElementById("overviewWebhookErrorsDropdown");
  const trigger = document.getElementById("overviewWebhookErrorsTrigger");
  const isOpen = dropdown.classList.contains("open");

  closeAllMenus();

  if (!isOpen) {
    renderOverviewErrorsDropdown();
    dropdown.classList.add("open");
    const rect = trigger.getBoundingClientRect();
    dropdown.style.top = `${rect.bottom + 4}px`;
    dropdown.style.left = `${Math.max(0, rect.right - dropdown.offsetWidth)}px`;
  }
}

function renderOverviewErrorsDropdown() {
  const dropdown = document.getElementById("overviewWebhookErrorsDropdown");
  paint(dropdown, overviewErrorsCache.length === 0
    ? EMPTY_NOTIFICATIONS
    : html`<div class="error-header"><span>Notifications</span></div>
      ${overviewErrorsCache.map((err) => html`<div class="error-item" @click=${(e) => { e.stopPropagation(); openOverviewErrorDetail(err); }}>
        <div class="error-time"><strong>${err.bucket}</strong> &middot; ${formatDate(err.occurredAt)}</div>
        <div class="error-msg">${truncate(err.errorMessage, 100)}${errorStatus(err)}</div>
      </div>`)}`);
}

// EDIT PERMISSIONS MODAL
let editingRecord = null;
let editingBucket = "";

function openEditPermissions(record) {
  editingRecord = record;
  editingBucket = currentBucket;

  document.getElementById("editPermissionsEmail").textContent =
    editingRecord.email ||
    `Hash: ${editingRecord.emailHash?.substring(0, 16)}...`;
  document.getElementById("editPermissionsBucket").textContent = editingBucket;
  document.getElementById("editName").value = editingRecord.name || "";

  paint(document.getElementById("editPermissionsGrid"), html`${currentBucketPermissions.map((p) => {
    const state = editingRecord.permissions[p];
    return html`
      <div class="permission-row" data-perm=${p}>
        <span class="permission-name">${formatPermission(p)}</span>
        <div class="permission-toggle">
          <button type="button" class="opted-in ${state === true ? "active" : ""}" @click=${(e) => setPermState(e.currentTarget, true)}>In</button>
          <button type="button" class="opted-out ${state === false ? "active" : ""}" @click=${(e) => setPermState(e.currentTarget, false)}>Out</button>
        </div>
      </div>`;
  })}`);

  document.getElementById("editLanguage").value =
    appSettings.uiLanguage || "en";

  // Populate custom fields
  editCustomFields = { ...(editingRecord.customFields || {}) };
  renderEditCustomFieldsList();

  document.getElementById("editPermissionsModal").style.display = "flex";
  closeAllMenus();
}

// Called from the subscriptions detail view to open the modal for a different bucket without changing the record being edited
function openEditFromSubscription(bucket, permissions, record) {
  currentBucketPermissions = permissions;
  openEditPermissions(record);
  editingBucket = bucket;
  document.getElementById("editPermissionsBucket").textContent = bucket;
}

function closeEditPermissionsModal() {
  document.getElementById("editPermissionsModal").style.display = "none";
  document.getElementById("editName").value = "";
  editingRecord = null;
  editingBucket = "";
}

function toggleBucketSubmenu(e) {
  if (e) e.stopPropagation();
  const submenu = document.getElementById("bucketSubmenu");
  const wasOpen = submenu.classList.contains("open");
  closeAllSubmenus();
  if (!wasOpen) submenu.classList.add("open");
}

function toggleModulesSubmenu(e) {
  if (e) e.stopPropagation();
  const submenu = document.getElementById("modulesSubmenu");
  const wasOpen = submenu.classList.contains("open");
  closeAllSubmenus();
  if (!wasOpen) submenu.classList.add("open");
}

function closeAllSubmenus() {
  document
    .querySelectorAll(".submenu.open")
    .forEach((el) => el.classList.remove("open"));
}

document.addEventListener("click", function (e) {
  if (!e.target.closest(".submenu, .btn-plus")) {
    closeAllSubmenus();
  }
});

function showPermissionsConfirmModal() {
  if (!editingRecord || !editingRecord.email) {
    notify(
      "error",
      "Update Failed",
      "Record data is missing, try reloading the page.",
    );
    return;
  }
  document.getElementById("permissionsConfirmModal").style.display = "flex";
}

function closePermissionsConfirmModal() {
  document.getElementById("permissionsConfirmModal").style.display = "none";
}

async function confirmSavePermissions() {
  closePermissionsConfirmModal();
  await savePermissions();
}

function showDeleteRecordModal() {
  if (!editingRecord || !editingRecord.emailHash) {
    notify(
      "error",
      "Delete Failed",
      "Record data is missing, try reloading the page.",
    );
    return;
  }
  document.getElementById("deleteRecordEmail").textContent =
    editingRecord.email || editingRecord.emailHash;
  document.getElementById("deleteRecordModal").style.display = "flex";
}

function closeDeleteRecordModal() {
  document.getElementById("deleteRecordModal").style.display = "none";
}

async function confirmDeleteRecord() {
  if (!editingRecord || !editingRecord.emailHash) {
    notify(
      "error",
      "Delete Failed",
      "Record data is missing, try reloading the page.",
    );
    return;
  }

  const bucket = editingBucket || currentBucket;
  const result = await apiRequest(
    `/api/admin/buckets/${encodeURIComponent(bucket)}/records/${encodeURIComponent(editingRecord.emailHash)}`,
    {
      method: "DELETE",
    },
  );

  if (result.ok) {
    notify("success", "Record Removed", "Consent record has been deleted");
    closeDeleteRecordModal();
    closeEditPermissionsModal();
    if (currentView === "subscriptions" && subDetailHash) {
      showIdentityDetails(subDetailHash, false);
    } else if (currentBucket) {
      loadBucket(currentBucket);
    }
  }
}

async function savePermissions() {
  if (!editingRecord || !editingRecord.email) {
    notify(
      "error",
      "Update Failed",
      "Record data is missing, try reloading the page.",
    );
    return;
  }

  const permissions = {};
  document
    .querySelectorAll("#editPermissionsGrid .permission-row")
    .forEach((row) => {
      const perm = row.dataset.perm;
      const inBtn = row.querySelector(".opted-in");
      permissions[perm] = inBtn.classList.contains("active")
        ? "OptedIn"
        : "OptedOut";
    });

  const customFields =
    Object.keys(editCustomFields).length > 0 ? editCustomFields : undefined;
  const bucket = editingBucket || currentBucket;
  const nameVal = document.getElementById("editName").value.trim() || undefined;

  const result = await apiRequest(
    `/api/admin/buckets/${encodeURIComponent(bucket)}/override`,
    {
      method: "POST",
      body: {
        email: editingRecord.email,
        name: nameVal,
        permissions,
        customFields,
      },
    },
  );

  if (result.ok) {
    notify(
      "success",
      "Permissions Updated",
      "Consent preferences have been saved",
    );
    closeEditPermissionsModal();
    if (currentBucket) loadBucket(currentBucket);
  }
}

async function openOptOutPage(record) {
  closeAllMenus();

  if (!record.email) {
    notify(
      "error",
      "Link Generation Failed",
      "Email address not available for this record.",
    );
    return;
  }

  const permissions = {};
  currentBucketPermissions.forEach((p) => {
    permissions[p] = record.permissions[p] === true;
  });

  const language = document.getElementById("editLanguage")?.value || "en";

  const result = await apiRequest("/api/tokens/generate", {
    method: "POST",
    body: [
      {
        bucket: currentBucket,
        email: record.email,
        permissions,
        expiryDays: 30,
        allowReplay: true,
        skipPermissionUpdate: true,
        language,
      },
    ],
  });

  if (result.ok) {
    const url = `${PUBLIC_URL || API_BASE}/u/${result.data[0].token}`;
    window.open(url, "_blank", "noopener");
  }
}

// SSE WEBHOOK ERROR NOTIFICATIONS
let eventSource = null;

// EventSource reconnects on network errors by itself; an HTTP error (expired session) closes it.
function connectSSE() {
  eventSource?.close();
  eventSource = new EventSource("/api/admin/events", { withCredentials: true });
  for (const type of ["webhook-error", "consent-update"]) {
    eventSource.addEventListener(type, (e) => handleSSEEvent(type, e.data));
  }
}

// Debounced consent-update handling to prevent DOM thrashing
let _consentUpdateTimer = null;
const _consentUpdatePending = new Set();

function _flushConsentUpdates() {
  _consentUpdateTimer = null;
  const pendingBuckets = new Set(_consentUpdatePending);
  _consentUpdatePending.clear();

  // Fetch fresh bucket list and diff against current
  apiRequest("/api/admin/buckets").then((result) => {
    if (!result.ok) return;
    const fresh = result.data || [];
    const oldNames = new Set(buckets.map((b) => b.name));
    const sidebarChanged =
      fresh.length !== buckets.length ||
      fresh.some((b) => !oldNames.has(b.name));

    // Always update data so record counts / permissions stay current
    buckets = fresh;
    try {
      sessionStorage.setItem("beacon_buckets", JSON.stringify(buckets));
    } catch {}

    // Only re-render sidebar DOM when the bucket list itself changed
    if (sidebarChanged) {
      renderBucketsSidebar();
    }

    // A re-render would close the row menu the user has open.
    if (openMenuId) return;

    if (currentView === "overview") {
      loadOverview();
    }

    // Refresh the active bucket view only if it was affected
    if (
      currentView === "bucket" &&
      currentBucket &&
      pendingBuckets.has(currentBucket)
    ) {
      loadBucket(currentBucket);
    }

    // Refresh subscriptions view if active
    if (currentView === "subscriptions") {
      if (subDetailHash) showIdentityDetails(subDetailHash, false);
      else loadIdentities(false);
    }
  });
}

function handleSSEEvent(type, data) {
  try {
    const evt = JSON.parse(data);
    if (type === "webhook-error") {
      notify("warning", "Webhook Error", `${evt.bucket}: ${evt.errorMessage}`);
      if (currentBucket && evt.bucket === currentBucket) {
        loadWebhookErrors(currentBucket);
      }
      if (currentView === "overview") loadOverview();
    } else if (type === "consent-update") {
      _consentUpdatePending.add(evt.bucket);
      if (!_consentUpdateTimer) {
        _consentUpdateTimer = setTimeout(_flushConsentUpdates, 500);
      }
    }
  } catch {}
}

// START
window.addEventListener("popstate", () => {
  const params = new URLSearchParams(window.location.search);
  if (!params.has("modal")) {
    document.getElementById("optionsModal").style.display = "none";
  }
  restoreViewFromUrl();
});
