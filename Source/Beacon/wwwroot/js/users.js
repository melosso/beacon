// USERS VIEW
let _usersAll = [],
  _usersPage = 1,
  _usersPageSize = 25;

async function loadUsers() {
  const result = await apiRequest("/api/admin/users");
  if (!result.ok) return;
  _usersAll = result.data || [];
  _usersPage = 1;
  renderUsersPage();
  loadOidcProviders();
}

let _oidcProviders = [];
let _editingOidcId = null;

async function loadOidcProviders() {
  const result = await apiRequest("/api/admin/oidc/providers");
  if (!result.ok) return;
  _oidcProviders = result.data || [];
  const el = document.getElementById("oidcProvidersList");
  if (!_oidcProviders.length) {
    paint(
      el,
      html`<p class="settings-empty-state">No sign-in providers. Add one to let users sign in with single sign-on.</p>`,
    );
    return;
  }
  paint(
    el,
    html`<div class="table-wrapper">
      <table class="table">
        <thead><tr><th>Provider</th><th>Issuer</th><th>Status</th></tr></thead>
        <tbody>
          ${_oidcProviders.map(
            (p) => html`<tr class="cursor-pointer" @click=${() => openOidcProviderSheet(p.id)}>
              <td><strong>${p.name}</strong><span class="cell-sub">${p.slug}</span></td>
              <td class="text-sm break-all">${p.authority}</td>
              <td><span class="status-chip ${p.isEnabled ? "opted-in" : "pending"}">${p.isEnabled ? "Enabled" : "Disabled"}</span></td>
            </tr>`,
          )}
        </tbody>
      </table>
    </div>`,
  );
}

function updateOidcRedirectUri() {
  const slug = document.getElementById("oidcSlug").value.trim();
  document.getElementById("oidcRedirectUri").value = slug
    ? `${location.origin}/api/admin/auth/oidc/${slug}/callback`
    : "";
}

function openOidcProviderSheet(id) {
  const p = typeof id === "string" ? _oidcProviders.find((x) => x.id === id) : null;
  _editingOidcId = p ? p.id : null;
  const set = (field, value) => (document.getElementById(field).value = value ?? "");
  document.getElementById("oidcProviderTitle").textContent = p ? "Edit provider" : "Add provider";
  set("oidcName", p?.name);
  set("oidcSlug", p?.slug);
  set("oidcAuthority", p?.authority);
  set("oidcClientId", p?.clientId);
  set("oidcClientSecret", "");
  set("oidcScopes", p?.scopes);
  set("oidcUsernameClaim", p?.usernameClaim);
  set("oidcEmailClaim", p?.emailClaim);
  set("oidcCreatedRole", p?.createdRole || "user");
  document.getElementById("oidcEnabled").checked = p ? p.isEnabled : true;
  document.getElementById("oidcCreateAccounts").checked = !!p?.createAccounts;
  document.getElementById("oidcSecretHint").textContent = p?.hasClientSecret
    ? "A secret is saved. Leave empty to keep it."
    : "Leave empty for a public client.";
  document.getElementById("oidcDeleteBtn").hidden = !p;
  updateOidcRedirectUri();
  openModal("oidcProviderSheet");
}

function closeOidcProviderSheet(force) {
  closeModal("oidcProviderSheet", force);
}

async function saveOidcProvider() {
  const value = (field) => document.getElementById(field).value.trim();
  const secret = document.getElementById("oidcClientSecret").value;
  const body = {
    name: value("oidcName"),
    slug: value("oidcSlug"),
    authority: value("oidcAuthority"),
    clientId: value("oidcClientId"),
    clientSecret: secret || (_editingOidcId ? undefined : ""),
    scopes: value("oidcScopes"),
    usernameClaim: value("oidcUsernameClaim"),
    emailClaim: value("oidcEmailClaim"),
    isEnabled: document.getElementById("oidcEnabled").checked,
    createAccounts: document.getElementById("oidcCreateAccounts").checked,
    createdRole: value("oidcCreatedRole"),
  };
  const result = await apiRequest(
    _editingOidcId
      ? `/api/admin/oidc/providers/${_editingOidcId}`
      : "/api/admin/oidc/providers",
    { method: _editingOidcId ? "PUT" : "POST", body },
  );
  if (!result.ok) return;
  closeOidcProviderSheet(true);
  notify("success", "Provider saved");
  loadOidcProviders();
}

function deleteOidcProvider() {
  const id = _editingOidcId;
  openConfirmModal(
    "Delete provider?",
    "Users who signed in through it are unlinked. Accounts it created have no usable password until an admin sets one.",
    "Delete provider",
    async () => {
      const result = await apiRequest(`/api/admin/oidc/providers/${id}`, {
        method: "DELETE",
      });
      if (!result.ok) return;
      closeOidcProviderSheet(true);
      notify("success", "Provider deleted");
      loadOidcProviders();
    },
  );
}

function renderUsersPage() {
  const start = (_usersPage - 1) * _usersPageSize;
  renderUsersTable(_usersAll.slice(start, start + _usersPageSize));
  renderUsersPagination();
}

function renderUsersPagination() {
  const total = _usersAll.length;
  const totalPages = Math.ceil(total / _usersPageSize) || 1;
  const start = total === 0 ? 0 : (_usersPage - 1) * _usersPageSize + 1;
  const end = Math.min(_usersPage * _usersPageSize, total);
  document.getElementById("usersPaginationInfo").textContent = pageRangeText(
    start,
    end,
    total,
  );
  document.getElementById("usersPrevBtn").disabled = _usersPage <= 1;
  document.getElementById("usersNextBtn").disabled = _usersPage >= totalPages;
  document.getElementById("usersPaginationBar").hidden = !(
    total > _usersPageSize
  );
}

function changeUsersPage(dir) {
  const totalPages = Math.ceil(_usersAll.length / _usersPageSize) || 1;
  _usersPage = Math.max(1, Math.min(totalPages, _usersPage + dir));
  renderUsersPage();
}

function updateUsersPageSize() {
  _usersPageSize = parseInt(document.getElementById("usersPageSize").value);
  _usersPage = 1;
  renderUsersPage();
}

function formatRelativeTime(isoString) {
  const date = new Date(isoString);
  const diff = Date.now() - date.getTime();
  const abs = Math.abs(diff);
  const full = date.toLocaleString(UI_LOCALE, {
    dateStyle: "medium",
    timeStyle: "short",
  });
  let rel;
  if (abs < 60_000) rel = "Just now";
  else if (abs < 3_600_000) rel = `${Math.floor(abs / 60_000)}m ago`;
  else if (abs < 86_400_000) rel = `${Math.floor(abs / 3_600_000)}h ago`;
  else if (abs < 7 * 86_400_000) rel = `${Math.floor(abs / 86_400_000)}d ago`;
  else rel = date.toLocaleDateString(UI_LOCALE);
  return html`<span class="tooltip-wrapper cursor-default">${rel}<span class="tooltip tooltip-above">${full}</span></span>`;
}

const KEBAB_ICON = html`<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><circle cx="12" cy="5" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="12" cy="19" r="1"/></svg>`;

// Row menu: kebab button plus dropdown items as [label, onClick, destructive?].
function rowMenu(menuId, items) {
  return html`
    <div class="row-actions">
      <button class="btn-actions" aria-label="Actions" @click=${(e) => {
        e.stopPropagation();
        toggleMenu(menuId, e.currentTarget);
      }}>${KEBAB_ICON}</button>
      <div class="dropdown-menu" id=${menuId}>
        ${items.map(([label, onClick, destructive]) => html`<button class="dropdown-item${destructive ? " dropdown-item--destructive" : ""}" @click=${onClick}>${label}</button>`)}
      </div>
    </div>`;
}

// Staggers the row entrance animation.
function staggerRows(tbody) {
  tbody.querySelectorAll("tr").forEach((row, i) => {
    row.style.animationDelay = `${Math.min(i * 30, 150)}ms`;
  });
}

function renderUsersTable(users) {
  const tbody = document.getElementById("usersBody");
  if (!users.length) {
    paint(
      tbody,
      html`<tr><td colspan="6"><div class="table-empty-state">
          <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>
          <p class="empty-title">No users</p>
          <p class="empty-sub">Add a user to grant sign-in access.</p>
        </div></td></tr>`,
    );
    return;
  }
  paint(
    tbody,
    html`${users.map(
      (u) => html`
        <tr>
          <td><strong>${u.username}</strong></td>
          <td><span class="status-badge ${u.role === "admin" ? "" : "status-badge-muted"}">${u.role}</span></td>
          <td>${u.isEnabled ? html`<span class="text-success">Enabled</span>` : html`<span class="text-muted">Disabled</span>`}</td>
          <td>${u.createdAt ? new Date(u.createdAt).toLocaleDateString(UI_LOCALE) : "-"}</td>
          <td>${u.lastLoginAt ? formatRelativeTime(u.lastLoginAt) : html`<span class="text-muted">Never</span>`}</td>
          <td class="col-actions">${rowMenu(`userDropdown-${u.id}`, [
            ["Rename", () => openRenameUserModal(u.id, u.username)],
            ["Change password", () => openUserPasswordModal(u.id, u.username)],
            ["Change role", () => openUserRoleModal(u.id, u.username, u.role)],
            [
              "Regenerate API key",
              () => regenerateUserApiKey(u.id, u.username),
            ],
            ["Delete", () => deleteUser(u.id, u.username), true],
          ])}</td>
        </tr>`,
    )}`,
  );
  staggerRows(tbody);
}

let userActionTargetId = null;

function openUserPasswordModal(id, username) {
  if (username === currentUsername) {
    notify(
      "warning",
      "Use account settings",
      "Change your own password in Settings → Account.",
    );
    return;
  }
  userActionTargetId = id;
  document.getElementById("userPasswordModalDesc").textContent =
    `Set a new password for ${username}.`;
  document.getElementById("userPasswordInput").value = "";
  openModal("userPasswordModal");
  setTimeout(() => document.getElementById("userPasswordInput").focus(), 50);
}

function closeUserPasswordModal() {
  closeModal("userPasswordModal");
}

async function saveUserPassword() {
  const pwd = document.getElementById("userPasswordInput").value;
  if (pwd.length < 12) {
    notify("error", "Password too short", "Use at least 12 characters.");
    return;
  }
  const btn = document.getElementById("userPasswordSaveBtn");
  btn.classList.add("is-loading");
  btn.disabled = true;
  const result = await apiRequest(
    `/api/admin/users/${userActionTargetId}/password`,
    { method: "PATCH", body: { newPassword: pwd } },
  );
  btn.classList.remove("is-loading");
  btn.disabled = false;
  if (result.ok) {
    closeUserPasswordModal();
    notify("success", "Password updated");
  }
}

function openUserRoleModal(id, username, currentRole) {
  userActionTargetId = id;
  document.getElementById("userRoleModalDesc").textContent =
    `Change role for ${username}.`;
  document.getElementById("userRoleSelect").value = currentRole;
  openModal("userRoleModal");
}

function closeUserRoleModal() {
  closeModal("userRoleModal");
}

async function saveUserRole() {
  const role = document.getElementById("userRoleSelect").value;
  const btn = document.getElementById("userRoleSaveBtn");
  btn.classList.add("is-loading");
  btn.disabled = true;
  const result = await apiRequest(
    `/api/admin/users/${userActionTargetId}/role`,
    { method: "PATCH", body: { role } },
  );
  btn.classList.remove("is-loading");
  btn.disabled = false;
  if (result.ok) {
    closeUserRoleModal();
    notify("success", "Role updated");
    loadUsers();
  }
}

function openRenameUserModal(id, username) {
  userActionTargetId = id;
  document.getElementById("renameUserModalDesc").textContent =
    `Current username: ${username}`;
  document.getElementById("renameUserInput").value = username;
  openModal("renameUserModal");
  setTimeout(() => document.getElementById("renameUserInput").focus(), 50);
}

function closeRenameUserModal() {
  closeModal("renameUserModal");
}

async function saveRenameUser() {
  const username = document.getElementById("renameUserInput").value.trim();
  if (!username) {
    notify("error", "Username required", "Enter a username.");
    return;
  }
  const btn = document.getElementById("renameUserSaveBtn");
  btn.classList.add("is-loading");
  btn.disabled = true;
  const result = await apiRequest(
    `/api/admin/users/${userActionTargetId}/username`,
    { method: "PATCH", body: { username } },
  );
  btn.classList.remove("is-loading");
  btn.disabled = false;
  if (result.ok) {
    closeRenameUserModal();
    notify("success", "User renamed");
    loadUsers();
  }
}

async function deleteUser(id, username) {
  openConfirmModal(
    "Delete user",
    `Delete "${username}"? This cannot be undone.`,
    "Delete",
    async () => {
      const result = await apiRequest(`/api/admin/users/${id}`, {
        method: "DELETE",
      });
      if (result.ok) {
        notify("success", "User deleted", `${username} is deleted.`);
        loadUsers();
      }
    },
  );
}

async function regenerateUserApiKey(id, username) {
  openConfirmModal(
    "Regenerate API Key",
    `Regenerate the API key for "${username}"? The old key will stop working immediately.`,
    "Regenerate",
    async () => {
      const result = await apiRequest(`/api/admin/users/${id}/api-key`, {
        method: "POST",
      });
      if (result.ok) {
        document.getElementById("userApiKeyRevealDesc").textContent =
          `New API key for ${username} — copy it now, it will not be shown again.`;
        document.getElementById("userApiKeyRevealOutput").textContent =
          result.data.apiKey;
        document
          .getElementById("userApiKeyRevealOutput")
          .setAttribute("data-uncopied", "");
        openModal("userApiKeyRevealModal");
      }
    },
  );
}

function closeUserApiKeyRevealModal(force) {
  closeModal("userApiKeyRevealModal", force);
}

function copyUserApiKey() {
  const key = document.getElementById("userApiKeyRevealOutput").textContent;
  if (!key) return;
  document
    .getElementById("userApiKeyRevealOutput")
    .removeAttribute("data-uncopied");
  clipboardWrite(key).then(() => notify("success", "API key copied"));
}

// CONFIRM MODAL
let _confirmAction = null;

function openConfirmModal(title, message, actionLabel, action) {
  _confirmAction = action;
  document.getElementById("confirmModalTitle").textContent = title;
  paint(document.getElementById("confirmModalMessage"), message);
  document.querySelector("#confirmModalBtn .btn-label").textContent =
    actionLabel;
  openModal("confirmModal");
}

function closeConfirmModal() {
  closeModal("confirmModal");
  _confirmAction = null;
}

async function executeConfirmAction() {
  if (_confirmAction) {
    const action = _confirmAction;
    closeConfirmModal();
    await action();
  }
}

// ADD USER MODAL
let _newUserApiKey = "";

function openAddUserModal() {
  document.getElementById("newUserUsername").value = "";
  document.getElementById("newUserPassword").value = "";
  document.getElementById("newUserConfirmPassword").value = "";
  document.getElementById("newUserRole").value = "user";
  document.getElementById("newUserApiKeySection").hidden = true;
  document.getElementById("newUserApiKeyOutput").textContent = "";
  _newUserApiKey = "";
  const addBtn = document.getElementById("addUserBtn");
  addBtn.disabled = false;
  addBtn.hidden = false;
  document.querySelector(
    "#addUserModal .modal-actions .btn-outline",
  ).textContent = "Cancel";
  openModal("addUserModal");
}

function closeAddUserModal(force) {
  closeModal("addUserModal", force);
}

async function createUser() {
  const username = document.getElementById("newUserUsername").value.trim();
  const password = document.getElementById("newUserPassword").value;
  const confirm = document.getElementById("newUserConfirmPassword").value;
  const role = document.getElementById("newUserRole").value;

  if (!username) {
    notify("error", "Username required", "Enter a username.");
    return;
  }
  if (password !== confirm) {
    notify("error", "Passwords differ", "Enter the same password twice.");
    return;
  }

  const btn = document.getElementById("addUserBtn");
  btn.classList.add("is-loading");
  btn.disabled = true;

  const result = await apiRequest("/api/admin/users", {
    method: "POST",
    body: { username, password, role },
  });

  btn.classList.remove("is-loading");
  btn.disabled = false;

  if (result.ok) {
    _newUserApiKey = result.data.apiKey;
    document.getElementById("newUserApiKeyOutput").textContent = _newUserApiKey;
    document.getElementById("newUserApiKeySection").hidden = false;
    document.getElementById("newUserApiKeySection").setAttribute("data-uncopied", "");
    markClean("addUserModal");
    document.querySelector(
      "#addUserModal .modal-actions .btn-outline",
    ).textContent = "Done";
    document.getElementById("addUserBtn").hidden = true;
    notify(
      "success",
      "User created",
      `${username} has been created. Copy the API key now.`,
    );
    loadUsers();
  }
}

function copyNewUserApiKey() {
  if (!_newUserApiKey) return;
  document.getElementById("newUserApiKeySection").removeAttribute("data-uncopied");
  clipboardWrite(_newUserApiKey).then(() =>
    notify("success", "API key copied"),
  );
}
