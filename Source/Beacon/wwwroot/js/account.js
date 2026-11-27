// ACCOUNT VIEW
async function loadAccount() {
  const result = await apiRequest("/api/admin/users/me");
  if (!result.ok) return;
  const me = result.data;
  const usernameEl = document.getElementById("accountUsername");
  if (usernameEl) usernameEl.textContent = me.username || "";
  paint(
    document.getElementById("accountRoleBadge"),
    html`<span class="status-badge ${me.role === "admin" ? "" : "status-badge-muted"}">${me.role || "admin"}</span>`,
  );
  sessionStorage.setItem("beacon_user_id", me.id || "");
  sessionStorage.setItem("beacon_user_role", me.role || "");
  const hasDbRecord = !!me.id;
  const itemsEl = document.getElementById("accountSecurityItems");
  const noticeEl = document.getElementById("accountGlobalAdminNotice");
  if (itemsEl) itemsEl.hidden = !hasDbRecord;
  if (noticeEl) noticeEl.hidden = hasDbRecord;
  if (hasDbRecord) loadAccountSso();
}

const SSO_LINK_RESULTS = {
  linked: ["success", "Provider linked", "You can now sign in with this provider."],
  not_linked: ["error", "Link failed", "This sign-in already belongs to another account, or the account is disabled."],
};

let _ssoPasswordAction = null;

async function loadAccountSso() {
  const url = new URL(window.location);
  const result = SSO_LINK_RESULTS[url.searchParams.get("sso")];
  if (result) {
    notify(...result);
    url.searchParams.delete("sso");
    history.replaceState(null, "", url);
  }
  const res = await apiRequest("/api/admin/oidc/link");
  const item = document.getElementById("accountSsoItem");
  const data = res.ok ? res.data : null;
  item.hidden = !data?.available || (!data.linked && !data.providers.length);
  if (item.hidden) return;
  document.getElementById("accountSsoDesc").textContent = data.linked
    ? `Linked to ${data.linked}.`
    : "Link your account to a sign-in provider.";
  paint(
    document.getElementById("accountSsoActions"),
    data.linked
      ? html`<button class="btn btn-outline" @click=${() => askSsoPassword(`Unlink ${data.linked} from your account.`, unlinkSso)}>Unlink</button>`
      : html`${data.providers.map(
          (p) => html`<button class="btn btn-outline" @click=${() => askSsoPassword(`Link ${p.name} to your account. You sign in at ${p.name} next.`, (pw) => linkSso(p.slug, pw))}>Link ${p.name}</button>`,
        )}`,
  );
}

function askSsoPassword(description, action) {
  _ssoPasswordAction = action;
  document.getElementById("ssoPasswordDesc").textContent = description;
  document.getElementById("ssoPasswordInput").value = "";
  openModal("ssoPasswordModal");
  document.getElementById("ssoPasswordInput").focus();
}

function closeSsoPasswordModal() {
  closeModal("ssoPasswordModal");
  _ssoPasswordAction = null;
}

async function confirmSsoPassword() {
  const action = _ssoPasswordAction;
  if (!action) return;
  const btn = document.getElementById("ssoPasswordBtn");
  btn.disabled = true;
  try {
    await action(document.getElementById("ssoPasswordInput").value);
  } finally {
    btn.disabled = false;
  }
}

async function linkSso(slug, currentPassword) {
  const result = await apiRequest(`/api/admin/oidc/link/${encodeURIComponent(slug)}`, {
    method: "POST",
    body: { currentPassword },
  });
  if (result.ok) window.location.href = result.data.authorizeUrl;
}

async function unlinkSso(currentPassword) {
  const result = await apiRequest("/api/admin/oidc/link", {
    method: "DELETE",
    body: { currentPassword },
  });
  if (!result.ok) return;
  closeSsoPasswordModal();
  notify("success", "Provider unlinked");
  loadAccountSso();
}

function openAccountPasswordModal() {
  document.getElementById("accountCurrentPassword").value = "";
  document.getElementById("accountNewPassword").value = "";
  document.getElementById("accountConfirmPassword").value = "";
  openModal("accountPasswordModal");
  setTimeout(
    () => document.getElementById("accountCurrentPassword")?.focus(),
    50,
  );
}

function closeAccountPasswordModal() {
  closeModal("accountPasswordModal");
}

async function saveAccountPassword() {
  const currentPwd = document.getElementById("accountCurrentPassword").value;
  const newPwd = document.getElementById("accountNewPassword").value;
  const confirmPwd = document.getElementById("accountConfirmPassword").value;

  if (newPwd !== confirmPwd) {
    notify("error", "Passwords differ", "Enter the same password twice.");
    return;
  }
  if (newPwd.length < 12) {
    notify("error", "Password too short", "Use at least 12 characters.");
    return;
  }

  const userId = sessionStorage.getItem("beacon_user_id");
  if (!userId) {
    notify(
      "error",
      "Session error",
      "User ID unavailable. Sign out and sign in again.",
    );
    return;
  }

  const body = { newPassword: newPwd };
  if (currentPwd) body.currentPassword = currentPwd;

  const btn = document.getElementById("accountPasswordSaveBtn");
  btn.classList.add("is-loading");
  btn.disabled = true;
  const result = await apiRequest(`/api/admin/users/${userId}/password`, {
    method: "PATCH",
    body,
  });
  btn.classList.remove("is-loading");
  btn.disabled = false;
  if (result.ok) {
    closeAccountPasswordModal();
    notify("success", "Password updated");
  }
}

async function regenerateOwnApiKey() {
  const userId = sessionStorage.getItem("beacon_user_id");
  if (!userId) {
    notify(
      "error",
      "Session error",
      "User ID unavailable. Sign out and sign in again.",
    );
    return;
  }
  openConfirmModal(
    "Regenerate API Key",
    "Are you sure you want to regenerate this? Your current key will be invalidated right away.",
    "Regenerate",
    async () => {
      const result = await apiRequest(`/api/admin/users/${userId}/api-key`, {
        method: "POST",
      });
      if (result.ok) {
        document.getElementById("userApiKeyRevealDesc").textContent =
          "Copy your new API key now, it won't be shown again. Your old key is now revoked.";
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
