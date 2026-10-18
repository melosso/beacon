// ACCOUNT VIEW
async function loadAccount() {
  const result = await apiRequest("/api/admin/users/me");
  if (!result.ok) return;
  const me = result.data;
  const usernameEl = document.getElementById("accountUsername");
  if (usernameEl) usernameEl.textContent = me.username || "";
  paint(document.getElementById("accountRoleBadge"),
    html`<span class="status-badge ${me.role === "admin" ? "" : "status-badge-muted"}">${me.role || "admin"}</span>`);
  sessionStorage.setItem("beacon_user_id", me.id || "");
  sessionStorage.setItem("beacon_user_role", me.role || "");
  const hasDbRecord = !!me.id;
  const itemsEl = document.getElementById("accountSecurityItems");
  const noticeEl = document.getElementById("accountGlobalAdminNotice");
  if (itemsEl) itemsEl.hidden = !hasDbRecord;
  if (noticeEl) noticeEl.hidden = hasDbRecord;
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
        openModal("userApiKeyRevealModal");
      }
    },
  );
}
