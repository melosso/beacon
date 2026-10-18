// Subset of Markdown (bold, italic, links) mirrored from LoginFooterParser; returns lit parts, never raw HTML.
const PROMO_TOKEN = /\*\*(.+?)\*\*|__(.+?)__|\*(.+?)\*|_(.+?)_|\[(.*?)\]\(([^)]*)\)/;

function promoMarkdown(text) {
  const parts = [];
  while (text) {
    const m = PROMO_TOKEN.exec(text);
    if (!m) {
      parts.push(text);
      break;
    }
    const [token, bold1, bold2, em1, em2, linkText, url] = m;
    parts.push(text.slice(0, m.index));
    if (bold1 ?? bold2) parts.push(html`<strong>${promoMarkdown(bold1 ?? bold2)}</strong>`);
    else if (em1 ?? em2) parts.push(html`<em>${promoMarkdown(em1 ?? em2)}</em>`);
    else parts.push(promoLink(linkText, url));
    text = text.slice(m.index + token.length);
  }
  return parts;
}

function promoLink(text, url) {
  if (/^(https?:\/\/|\/\/)/i.test(url)) return html`<a href=${url} target="_blank" rel="noopener noreferrer">${text}</a>`;
  if (/^(\/|#|mailto:|tel:)/i.test(url)) return html`<a href=${url}>${text}</a>`;
  return text;
}

function initPromoBar() {
  updatePromoBar();
}

function updatePromoBar() {
  let bar = document.getElementById("promoBar");
  if (!appSettings.promoBarEnabled || !appSettings.promoBar.trim()) {
    if (bar) bar.classList.remove("is-visible");
    return;
  }
  if (!bar) {
    bar = document.createElement("div");
    bar.id = "promoBar";
    bar.className = "promo-bar";
    bar.setAttribute("role", "region");
    bar.setAttribute("aria-label", "Announcement");
    document.body.insertBefore(bar, document.querySelector(".app-shell"));
  }
  paint(bar, html`<div class="promo-bar-inner"><div class="promo-bar-content">${promoMarkdown(appSettings.promoBar)}</div>${(appSettings.promoBarDismissable ?? true)
    ? html`<button type="button" class="promo-bar-close" aria-label="Dismiss announcement" @click=${dismissPromoBar}><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M18 6L6 18M6 6l12 12"/></svg></button>`
    : nothing}</div>`);
  requestAnimationFrame(showPromoBar);
}

async function onPromoBarToggle(checkbox) {
  if (checkbox.checked && !appSettings.promoBar.trim()) {
    checkbox.checked = false;
    notify(
      "error",
      "No announcement content",
      "Add announcement text in the announcement settings first.",
    );
    return;
  }
  saveSetting("promoBarEnabled", checkbox.checked);
  await saveCurrentSettings();
  updatePromoBar();
}

function openPromoBarModal() {
  const el = document.getElementById("modal-promoBar");
  el.value = appSettings.promoBar;
  updateCharCount(el);
  document.getElementById("modal-promoBarDismissable").checked =
    appSettings.promoBarDismissable ?? true;
  document.getElementById("modal-promoBarShowOnLogin").checked =
    appSettings.promoBarShowOnLogin ?? false;
  openModal("promoBarModal");
}

function closePromoBarModal() {
  closeModal("promoBarModal");
}

async function savePromoBarSettings() {
  const val = document.getElementById("modal-promoBar").value;
  appSettings.promoBar = val;
  appSettings.promoBarDismissable = document.getElementById(
    "modal-promoBarDismissable",
  ).checked;
  appSettings.promoBarShowOnLogin = document.getElementById(
    "modal-promoBarShowOnLogin",
  ).checked;
  if (!val.trim()) appSettings.promoBarEnabled = false;
  const toggle = document.getElementById("setting-promoBarEnabled");
  if (toggle) toggle.checked = appSettings.promoBarEnabled;
  await saveCurrentSettings();
  updatePromoBar();
  closePromoBarModal();
}

async function onLoginFooterToggle(checkbox) {
  if (checkbox.checked && !appSettings.loginFooter.trim()) {
    checkbox.checked = false;
    notify(
      "error",
      "No footer content",
      "Add footer text in the footer settings first.",
    );
    return;
  }
  saveSetting("loginFooterEnabled", checkbox.checked);
  await saveCurrentSettings();
}

function updateCharCount(textarea) {
  document.getElementById(`${textarea.id}-count`).textContent = `${textarea.value.length} / 500`;
}

function savePerPermissionEmail(value) {
  saveSetting("perPermissionEmail", value === "true");
}

function openLoginFooterModal() {
  const el = document.getElementById("modal-loginFooter");
  el.value = appSettings.loginFooter;
  updateCharCount(el);
  openModal("loginFooterModal");
}

function closeLoginFooterModal() {
  closeModal("loginFooterModal");
}

async function saveLoginFooterSettings() {
  const val = document.getElementById("modal-loginFooter").value;
  appSettings.loginFooter = val;
  if (!val.trim()) appSettings.loginFooterEnabled = false;
  const toggle = document.getElementById("setting-loginFooterEnabled");
  if (toggle) toggle.checked = appSettings.loginFooterEnabled;
  await saveCurrentSettings();
  closeLoginFooterModal();
}

function openCacheSettingsModal() {
  document.getElementById("setting-cacheTtlSeconds").value =
    appSettings.cacheTtlSeconds ?? 300;
  document.getElementById("setting-cacheConsentRecords").checked =
    appSettings.cacheConsentRecords ?? true;
  document.getElementById("setting-cacheBucketData").checked =
    appSettings.cacheBucketData ?? true;
  document.getElementById("cacheStatsResult").textContent = "";
  openModal("cacheSettingsModal");
  loadCacheStats();
}

function closeCacheSettingsModal() {
  closeModal("cacheSettingsModal");
}

async function saveCacheSettings() {
  appSettings.cacheTtlSeconds =
    parseInt(document.getElementById("setting-cacheTtlSeconds").value, 10) ||
    300;
  appSettings.cacheConsentRecords = document.getElementById(
    "setting-cacheConsentRecords",
  ).checked;
  appSettings.cacheBucketData = document.getElementById(
    "setting-cacheBucketData",
  ).checked;
  await saveCurrentSettings();
  closeCacheSettingsModal();
}

async function flushCache() {
  const btn = document.getElementById("btnFlushCache");
  if (btn) btn.disabled = true;
  try {
    const res = await apiRequest("/api/admin/cache", { method: "DELETE" });
    if (res.ok) {
      notify(
        "success",
        "Cache flushed",
      );
      loadCacheStats();
    } else {
      notify("error", "Flush failed", "The cache was not flushed. Retry.");
    }
  } finally {
    if (btn) btn.disabled = false;
  }
}

async function loadCacheStats() {
  const el = document.getElementById("cacheStatsResult");
  if (!el) return;
  const res = await apiRequest("/api/admin/cache/stats");
  if (res.ok && res.data) {
    const { enabled, provider, approximateKeyCount } = res.data;
    el.textContent = enabled
      ? `Provider: ${provider} — ~${approximateKeyCount} key${approximateKeyCount !== 1 ? "s" : ""} tracked`
      : "Caching is disabled.";
  }
}

// EMAIL SETTINGS MODAL
function openEmailSettingsModal() {
  if (
    typeof DISABLE_EMAIL_NOTIFICATIONS !== "undefined" &&
    DISABLE_EMAIL_NOTIFICATIONS
  )
    return;
  document.getElementById("emailProvider").value =
    appSettings.emailProvider || "none";
  document.getElementById("emailResendApiKey").value =
    appSettings.emailResendApiKey || "";
  document.getElementById("emailFromAddress").value =
    appSettings.emailFromAddress || "";
  document.getElementById("emailFromName").value =
    appSettings.emailFromName || "";
  document.getElementById("emailSmtpHost").value =
    appSettings.emailSmtpHost || "";
  document.getElementById("emailSmtpPort").value =
    appSettings.emailSmtpPort || 587;
  document.getElementById("emailSmtpUsername").value =
    appSettings.emailSmtpUsername || "";
  document.getElementById("emailSmtpPassword").value =
    appSettings.emailSmtpPassword || "";
  document.getElementById("emailSmtpUseTls").checked =
    appSettings.emailSmtpUseTls !== false;
  document.getElementById("emailQueueEnabled").checked =
    !!appSettings.emailQueueEnabled;
  showEmailProviderFields(appSettings.emailProvider || "none");
  openModal("emailSettingsModal");
}

function closeEmailSettingsModal() {
  closeModal("emailSettingsModal");
}

function showEmailProviderFields(provider) {
  const isConfigured = provider === "resend" || provider === "smtp";
  document.getElementById("email-fields-resend").hidden = !(provider === "resend");
  document.getElementById("email-fields-smtp").hidden = !(provider === "smtp");
  document.getElementById("email-fields-sender").hidden = !isConfigured;
  document.getElementById("email-fields-queue").hidden = !isConfigured;
}

function validateEmailConfig(provider, fields) {
  if (provider === "resend") {
    if (!fields.emailResendApiKey?.trim()) return "Resend API key is required.";
    if (!fields.emailFromAddress?.trim()) return "From address is required.";
  }
  if (provider === "smtp") {
    if (!fields.emailSmtpHost?.trim()) return "SMTP host is required.";
    if (!fields.emailSmtpPort) return "SMTP port is required.";
    if (!fields.emailSmtpUsername?.trim()) return "SMTP username is required.";
    if (!fields.emailSmtpPassword?.trim()) return "SMTP password is required.";
    if (!fields.emailFromAddress?.trim()) return "From address is required.";
  }
  return null;
}

async function onEmailNotificationsToggle(checkbox) {
  if (
    typeof DISABLE_EMAIL_NOTIFICATIONS !== "undefined" &&
    DISABLE_EMAIL_NOTIFICATIONS
  ) {
    checkbox.checked = false;
    return;
  }
  if (!checkbox.checked) {
    saveSetting("emailNotifications", false);
    return;
  }
  const provider = appSettings.emailProvider || "none";
  if (provider === "none") {
    checkbox.checked = false;
    notify(
      "error",
      "No provider configured",
      "Select a provider in the email settings first.",
    );
    return;
  }
  const error = validateEmailConfig(provider, appSettings);
  if (error) {
    checkbox.checked = false;
    notify("error", "Incomplete configuration", error);
    return;
  }
  saveSetting("emailNotifications", true);
}

async function saveEmailSettings() {
  const draft = {
    emailProvider: document.getElementById("emailProvider").value,
    emailResendApiKey: document.getElementById("emailResendApiKey").value,
    emailFromAddress: document.getElementById("emailFromAddress").value,
    emailFromName: document.getElementById("emailFromName").value,
    emailSmtpHost: document.getElementById("emailSmtpHost").value,
    emailSmtpPort:
      parseInt(document.getElementById("emailSmtpPort").value) || 587,
    emailSmtpUsername: document.getElementById("emailSmtpUsername").value,
    emailSmtpPassword: document.getElementById("emailSmtpPassword").value,
    emailSmtpUseTls: document.getElementById("emailSmtpUseTls").checked,
    emailQueueEnabled: document.getElementById("emailQueueEnabled").checked,
  };

  if (appSettings.emailNotifications && draft.emailProvider !== "none") {
    const error = validateEmailConfig(draft.emailProvider, draft);
    if (error) {
      notify("error", "Incomplete configuration", error);
      return;
    }
  }

  Object.assign(appSettings, draft);
  await saveCurrentSettings();
  closeEmailSettingsModal();
}

function openDoubleOptInSettingsModal() {
  const perPerm = appSettings.perPermissionEmail ?? false;
  document.getElementById("setting-perPermissionEmail").value = String(perPerm);
  openModal("doubleOptInSettingsModal");
}

function closeDoubleOptInSettingsModal() {
  closeModal("doubleOptInSettingsModal");
}

function toggleReveal(inputId, btn) {
  const input = document.getElementById(inputId);
  const isHidden = input.type === "password";
  input.type = isHidden ? "text" : "password";
  btn.classList.toggle("active", isHidden);
}

function openObjectStorageModal() {
  document.getElementById("objectStorageProvider").value =
    appSettings.objectStorageProvider || "none";
  document.getElementById("objectStorageBucket").value =
    appSettings.objectStorageBucket || "";
  document.getElementById("objectStorageRegion").value =
    appSettings.objectStorageRegion || "us-east-1";
  document.getElementById("objectStorageEndpoint").value =
    appSettings.objectStorageEndpoint || "";
  document.getElementById("objectStorageAccessKey").value =
    appSettings.objectStorageAccessKey || "";
  document.getElementById("objectStorageSecretKey").value =
    appSettings.objectStorageSecretKey || "";
  document.getElementById("objectStoragePublicUrl").value =
    appSettings.objectStoragePublicUrl || "";
  document.getElementById("objectStorageTestResult").textContent = "";
  showObjectStorageProviderFields(appSettings.objectStorageProvider || "none");
  openModal("objectStorageModal");
}

function closeObjectStorageModal() {
  closeModal("objectStorageModal");
}

function showObjectStorageProviderFields(provider) {
  const endpointRow = document.getElementById("objectStorageEndpointRow");
  const regionRow = document.getElementById("objectStorageRegionRow");
  if (endpointRow)
    endpointRow.hidden = provider === "s3" || provider === "none";
  if (regionRow)
    regionRow.hidden = provider === "r2" || provider === "none";
}

async function testObjectStorageConnection() {
  const btn = document.getElementById("btnTestObjectStorage");
  const result = document.getElementById("objectStorageTestResult");
  if (btn) btn.disabled = true;
  result.textContent = "Testing…";
  result.className = "";
  try {
    const res = await apiRequest("/api/admin/settings/object-storage/test", {
      method: "POST",
      body: {
        provider: document.getElementById("objectStorageProvider").value,
        endpoint: document.getElementById("objectStorageEndpoint").value,
        bucket: document.getElementById("objectStorageBucket").value,
        region: document.getElementById("objectStorageRegion").value,
        accessKey: document.getElementById("objectStorageAccessKey").value,
        secretKey: document.getElementById("objectStorageSecretKey").value,
      },
    });
    if (res.ok && res.data) {
      result.textContent = res.data.success
        ? `Connected (${res.data.latencyMs}ms)`
        : res.data.message;
      result.className = res.data.success
        ? "test-result-ok"
        : "test-result-fail";
    }
  } catch {
    result.textContent = "Request failed.";
    result.className = "test-result-fail";
  } finally {
    if (btn) btn.disabled = false;
  }
}

async function saveObjectStorageSettings() {
  const provider = document.getElementById("objectStorageProvider").value;
  const bucket = document.getElementById("objectStorageBucket").value.trim();
  if (appSettings.objectStorage && provider !== "none" && !bucket) {
    notify("error", "Bucket required", "Enter a bucket name before saving.");
    return;
  }
  appSettings.objectStorageProvider = provider;
  appSettings.objectStorageBucket = bucket;
  appSettings.objectStorageRegion = document
    .getElementById("objectStorageRegion")
    .value.trim();
  appSettings.objectStorageEndpoint = document
    .getElementById("objectStorageEndpoint")
    .value.trim();
  appSettings.objectStorageAccessKey = document.getElementById(
    "objectStorageAccessKey",
  ).value;
  appSettings.objectStorageSecretKey = document.getElementById(
    "objectStorageSecretKey",
  ).value;
  appSettings.objectStoragePublicUrl = document
    .getElementById("objectStoragePublicUrl")
    .value.trim();
  await saveCurrentSettings();
  closeObjectStorageModal();
}

function openSubmissionFormsSettingsModal() {
  document.getElementById("setting-submissionDefaultRateLimitPerMinute").value =
    appSettings.submissionDefaultRateLimitPerMinute ?? 10;
  document.getElementById("setting-submissionDefaultHoneypotEnabled").checked =
    appSettings.submissionDefaultHoneypotEnabled ?? true;
  document.getElementById("setting-submissionDefaultConsentRequired").checked =
    appSettings.submissionDefaultConsentRequired ?? true;
  openModal("submissionFormsSettingsModal");
}

function closeSubmissionFormsSettingsModal() {
  closeModal("submissionFormsSettingsModal");
}

async function saveSubmissionFormsSettings() {
  appSettings.submissionDefaultRateLimitPerMinute =
    parseInt(
      document.getElementById("setting-submissionDefaultRateLimitPerMinute")
        .value,
      10,
    ) || 10;
  appSettings.submissionDefaultHoneypotEnabled = document.getElementById(
    "setting-submissionDefaultHoneypotEnabled",
  ).checked;
  appSettings.submissionDefaultConsentRequired = document.getElementById(
    "setting-submissionDefaultConsentRequired",
  ).checked;
  await saveCurrentSettings();
  closeSubmissionFormsSettingsModal();
}

function updateSidebarUser() {
  const name = currentUsername || "Admin";
  const nameEl = document.getElementById("sidebarUserName");
  const avatarEl = document.getElementById("sidebarUserAvatar");
  if (nameEl) nameEl.textContent = name.charAt(0).toUpperCase() + name.slice(1);
  if (avatarEl) avatarEl.textContent = name.charAt(0).toUpperCase();
}

function setupUserAuthNav(role) {
  const mode = typeof USER_AUTH_METHOD !== "undefined" ? USER_AUTH_METHOD : "";
  const isAdmin = !mode || role === "admin";
  const settingsNavAccount = document.getElementById("settingsNavAccount");
  const settingsNavDivider = document.getElementById("settingsNavDivider");
  const settingsNavUsers = document.getElementById("settingsNavUsers");
  if (settingsNavAccount) settingsNavAccount.hidden = false;
  if (isAdmin && settingsNavDivider) settingsNavDivider.hidden = false;
  if (isAdmin && settingsNavUsers) settingsNavUsers.hidden = false;
  const settingsNavApiKeys = document.getElementById("settingsNavApiKeys");
  if (isAdmin && settingsNavApiKeys) settingsNavApiKeys.hidden = false;
  const authNotice = document.getElementById("users-auth-disabled-notice");
  if (authNotice) authNotice.hidden = mode;
  const navWorkflow = document.getElementById("navWorkflow");
  if (navWorkflow) navWorkflow.hidden = !isAdmin;
  if (!isAdmin) {
    for (const id of [
      "settingsNavGeneral",
      "settingsNavModules",
      "settingsNavDataPolicies",
      "settingsNavIntegration",
      "settingsNavSystem",
      "settingsNavSystemDivider",
      "settingsNavConnectors",
      "settingsNavPersonalisation",
    ]) {
      const el = document.getElementById(id);
      if (el) el.hidden = true;
    }
  }
}
