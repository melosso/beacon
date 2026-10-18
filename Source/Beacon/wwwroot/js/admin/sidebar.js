// LOAD BUCKETS
async function loadBuckets() {
  // Restore cached buckets immediately to prevent pop-in
  if (buckets.length === 0) {
    try {
      const cached = sessionStorage.getItem("beacon_buckets");
      if (cached) {
        buckets = JSON.parse(cached);
        renderBucketsSidebar();
      }
    } catch {}
  }
  const result = await apiRequest("/api/admin/buckets");
  if (result.ok) {
    buckets = result.data || [];
    try {
      sessionStorage.setItem("beacon_buckets", JSON.stringify(buckets));
    } catch {}
    renderBucketsSidebar();
  }
}

// Sidebar preferences (persisted in localStorage)
const sidebarPrefs = JSON.parse(
  localStorage.getItem("beacon_sidebar_prefs") || "{}",
);
if (!sidebarPrefs.sort) sidebarPrefs.sort = "default";
if (!sidebarPrefs.display) sidebarPrefs.display = "description";

function toggleSidebarBuckets() {
  sidebarPrefs.expanded = !sidebarPrefs.expanded;
  saveSidebarPrefs();
  renderBucketsSidebar();
}

function saveSidebarPrefs() {
  localStorage.setItem("beacon_sidebar_prefs", JSON.stringify(sidebarPrefs));
}

function cycleSidebarSort() {
  const cycle = { default: "az", az: "za", za: "default" };
  sidebarPrefs.sort = cycle[sidebarPrefs.sort] || "az";
  saveSidebarPrefs();
  renderBucketsSidebar();
}

function toggleSidebarDisplay() {
  sidebarPrefs.display =
    sidebarPrefs.display === "description" ? "code" : "description";
  saveSidebarPrefs();
  renderBucketsSidebar();
}

function updateSidebarButtons() {
  const btn = document.getElementById("btnSort");
  const icon = document.getElementById("btnSortIcon");
  btn.classList.toggle("active", sidebarPrefs.sort !== "default");
  document.getElementById("btnSortTooltip").textContent =
    sidebarPrefs.sort === "az"
      ? "Sorted A\u2013Z"
      : sidebarPrefs.sort === "za"
        ? "Sorted Z\u2013A"
        : "Sort buckets";
  icon.classList.toggle("is-flipped", sidebarPrefs.sort === "za");
  document
    .getElementById("btnDisplayMode")
    .classList.toggle("active", sidebarPrefs.display === "code");
  document.getElementById("btnDisplayTooltip").textContent =
    sidebarPrefs.display === "code" ? "Show descriptions" : "Show code names";
}

function renderBucketsSidebar() {
  const container = document.getElementById("bucketsList");
  updateSidebarButtons();
  if (buckets.length === 0) {
    paint(
      container,
      html`<div class="nav-empty">No buckets, yet!</div>`,
    );
    return;
  }

  let sorted = [...buckets];
  if (sidebarPrefs.sort === "az")
    sorted.sort((a, b) => a.name.localeCompare(b.name));
  else if (sidebarPrefs.sort === "za")
    sorted.sort((a, b) => b.name.localeCompare(a.name));

  const expanded = Boolean(sidebarPrefs.expanded);
  const displayBuckets = expanded ? sorted : sorted.slice(0, MAX_SIDEBAR_BUCKETS);
  const showCode = sidebarPrefs.display === "code";
  const remaining = buckets.length - MAX_SIDEBAR_BUCKETS;
  paint(
    container,
    html` ${displayBuckets.map(
      (b) =>
        html`<a
          href="#"
          class="nav-item"
          data-bucket=${b.name}
          title=${b.name}
          @click=${(e) => {
            e.preventDefault();
            showBucket(b.name);
          }}
          >${showCode ? b.name : formatPermission(b.name)}</a
        >`,
    )}
    ${remaining > 0
      ? html`<button
          type="button"
          class="nav-item show-more"
          aria-expanded=${expanded ? "true" : "false"}
          @click=${toggleSidebarBuckets}
        >
          ${expanded ? "Show fewer" : `… ${remaining} more`}
        </button>`
      : nothing}`,
  );
  if (currentView === "bucket")
    container
      .querySelector(`.nav-item[data-bucket="${CSS.escape(currentBucket)}"]`)
      ?.classList.add("active");
}

let tokenPermissions = new Set();
let removedPermissions = new Set();
let tokenCustomFields = {};
let editCustomFields = {};

// When true, newly added permissions in the token modal default to Out (used when bucket has double opt-in)
let tokenDefaultOptOut = false;

function renderPermissionsGrid() {
  const container = document.getElementById("permissionsGrid");

  // Only auto-populate from the selected bucket
  const selectedBucket = document
    .getElementById("tokenBucket")
    .value.trim()
    .toLowerCase();
  const match = buckets.find((b) => b.name === selectedBucket);
  if (match) {
    (match.permissions || []).forEach((p) => {
      if (!removedPermissions.has(p)) {
        tokenPermissions.add(p);
      }
    });
  }

  if (tokenPermissions.size === 0) {
    paint(
      container,
      html`<p class="list-empty">
        No permissions. Add a custom permission below.
      </p>`,
    );
    _syncConfirmationEmailToggle();
    return;
  }

  const defaultIn = tokenDefaultOptOut ? "" : " active";
  const defaultOut = tokenDefaultOptOut ? " active" : "";

  paint(
    container,
    html`${[...tokenPermissions].sort().map(
      (p) =>
        html` <div class="permission-row" data-perm=${p}>
          <span class="permission-name">${formatPermission(p)}</span>
          <div class="permission-toggle">
            <button
              type="button"
              class="opted-in${defaultIn}"
              @click=${(e) => setPermState(e.currentTarget, true)}
            >
              In
            </button>
            <button
              type="button"
              class="opted-out${defaultOut}"
              @click=${(e) => setPermState(e.currentTarget, false)}
            >
              Out
            </button>
            <button
              type="button"
              class="skip"
              @click=${(e) => setPermState(e.currentTarget, null)}
            >
              Skip
            </button>
          </div>
          <button
            type="button"
            class="btn-remove"
            @click=${() => removePermission(p)}
            title="Remove"
            aria-label="Remove"
          >
            &times;
          </button>
        </div>`,
    )}`,
  );
  _syncConfirmationEmailToggle();
}

function showPermissionSuggestions() {
  const input = document.getElementById("newPermissionInput");
  const dropdown = document.getElementById("permissionAutocomplete");
  const query = input.value.trim().toLowerCase();
  document.getElementById("bucketAutocomplete").classList.remove("open");
  const selectedBucket = document
    .getElementById("tokenBucket")
    .value.trim()
    .toLowerCase();
  const match = buckets.find((b) => b.name === selectedBucket);
  if (!match) {
    hidePermissionSuggestions();
    return;
  }

  const available = (match.permissions || []).filter(
    (p) => !tokenPermissions.has(p) && p.includes(query),
  );

  if (
    available.length === 0 ||
    (available.length === 1 && available[0] === query)
  ) {
    hidePermissionSuggestions();
    return;
  }

  paint(
    dropdown,
    html`${available
      .slice(0, 10)
      .map(
        (p) =>
          html`<div
            class="autocomplete-item"
            @click=${() => selectPermission(p)}
          >
            ${formatPermission(p)}
          </div>`,
      )}`,
  );
  positionDropdown(input, dropdown);
  dropdown.classList.add("open");
}

function hidePermissionSuggestions() {
  document.getElementById("permissionAutocomplete").classList.remove("open");
}

function selectPermission(perm) {
  document.getElementById("newPermissionInput").value = perm;
  hidePermissionSuggestions();
}

function addCustomPermission() {
  const input = document.getElementById("newPermissionInput");
  const perm = input.value
    .trim()
    .toLowerCase()
    .replace(/\s+/g, "_")
    .replace(/[^a-z0-9_-]/g, "");

  if (!perm) {
    notify("warning", "Permission name required", "Enter a permission name.");
    return;
  }

  if (tokenPermissions.has(perm)) {
    notify("warning", "Duplicate permission", "The token already has this permission.");
    return;
  }

  tokenPermissions.add(perm);
  removedPermissions.delete(perm);
  input.value = "";
  hidePermissionSuggestions();
  renderPermissionsGrid();
}

function removePermission(perm) {
  tokenPermissions.delete(perm);
  removedPermissions.add(perm);
  renderPermissionsGrid();
  notify(
    "success",
    "Permission removed",
    `${formatPermission(perm)} is removed from this token.`,
  );
}

// Shared by the token grid and the edit-permissions grid.
function setPermState(button, state) {
  const row = button.closest(".permission-row");
  row
    .querySelectorAll(".permission-toggle button")
    .forEach((btn) => btn.classList.remove("active"));
  if (state === true) row.querySelector(".opted-in").classList.add("active");
  else if (state === false)
    row.querySelector(".opted-out").classList.add("active");
  else row.querySelector(".skip")?.classList.add("active");
  _syncConfirmationEmailToggle();
}

function _syncConfirmationEmailToggle() {
  const group = document.getElementById("tokenSkipEmailGroup");
  if (!group || group.hidden) return;
  const hasOptIn = Object.values(getPermissionStates()).some((v) => v === true);
  const checkbox = document.getElementById("tokenSendConfirmation");
  const label = group.querySelector("label.checkbox-toggle");
  checkbox.disabled = !hasOptIn;
  label.classList.toggle("is-disabled", !hasOptIn);
}

function getPermissionStates() {
  const states = {};
  document
    .querySelectorAll("#permissionsGrid .permission-row")
    .forEach((row) => {
      const perm = row.dataset.perm;
      const inBtn = row.querySelector(".opted-in");
      const outBtn = row.querySelector(".opted-out");
      if (inBtn.classList.contains("active")) states[perm] = true;
      else if (outBtn.classList.contains("active")) states[perm] = false;
    });
  return states;
}

// CUSTOM FIELDS
function openCustomFieldsDialog() {
  const frozen = document
    .getElementById("tokenFormFields")
    .classList.contains("frozen");
  renderCustomFieldsGrid(frozen);
  const modal = document.getElementById("customFieldsModal");
  const addRow = modal.querySelector(".custom-fields-add-row");
  if (addRow) addRow.hidden = frozen;
  openModal("customFieldsModal");
}

function closeCustomFieldsDialog() {
  closeModal("customFieldsModal");
  renderTokenCustomFieldsDisplay();
}

function addCustomField(context) {
  const isEdit = context === "edit";
  const isNl = context === "nl";
  const keyInput = document.getElementById(
    isNl ? "nlFieldKey" : isEdit ? "editFieldKey" : "newFieldKey",
  );
  const valueInput = document.getElementById(
    isNl ? "nlFieldValue" : isEdit ? "editFieldValue" : "newFieldValue",
  );
  const key = keyInput.value.trim();
  const value = valueInput.value.trim();

  if (!key) {
    notify("warning", "Key required", "Enter a field key.");
    return;
  }

  const fields = isNl
    ? nlCustomFields
    : isEdit
      ? editCustomFields
      : tokenCustomFields;
  fields[key] = value;
  keyInput.value = "";
  valueInput.value = "";
  keyInput.focus();

  if (isNl) {
    renderNlCustomFieldsList();
  } else if (isEdit) {
    renderEditCustomFieldsList();
  } else {
    renderCustomFieldsGrid();
    renderTokenCustomFieldsDisplay();
  }
}

function removeCustomField(key, context) {
  const isEdit = context === "edit";
  const isNl = context === "nl";
  const fields = isNl
    ? nlCustomFields
    : isEdit
      ? editCustomFields
      : tokenCustomFields;
  delete fields[key];

  if (isNl) {
    renderNlCustomFieldsList();
  } else if (isEdit) {
    renderEditCustomFieldsList();
  } else {
    renderCustomFieldsGrid();
    renderTokenCustomFieldsDisplay();
  }
}

function customFieldRows(fields, context, readonly = false) {
  return html`${Object.entries(fields).map(
    ([k, v]) =>
      html` <div class="custom-field-row">
        <span class="custom-field-key">${k}</span>
        <span class="custom-field-value"
          >${context === "nl" && /^\{\{.+\}\}$/.test(v)
            ? html`<code>${v}</code>`
            : v}</span
        >
        ${readonly
          ? nothing
          : html`<button
              type="button"
              class="btn-remove"
              @click=${() => removeCustomField(k, context)}
              title="Remove"
              aria-label="Remove"
            >
              &times;
            </button>`}
      </div>`,
  )}`;
}

function renderCustomFieldsGrid(readonly = false) {
  paint(
    document.getElementById("customFieldsGrid"),
    customFieldRows(tokenCustomFields, "token", readonly),
  );
}

function renderTokenCustomFieldsDisplay() {
  const group = document.getElementById("tokenCustomFieldsGroup");
  group.hidden = !(Object.keys(tokenCustomFields).length);
  paint(
    document.getElementById("tokenCustomFieldsList"),
    customFieldRows(tokenCustomFields, "token"),
  );
}

function renderEditCustomFieldsList() {
  document.getElementById("editCustomFieldsGroup").hidden = false;
  paint(
    document.getElementById("editCustomFieldsList"),
    customFieldRows(editCustomFields, "edit"),
  );
}

function insertNlFieldVariable(variable) {
  document.getElementById("nlFieldValue").value = variable;
  document.getElementById("nlFieldKey").focus();
}

function renderNlCustomFieldsList() {
  paint(
    document.getElementById("nlCustomFieldsList"),
    customFieldRows(nlCustomFields, "nl"),
  );
}
