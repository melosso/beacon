// TOKEN GENERATION
function handleGenerateToken() {
  const btn = document.getElementById("generateTokenBtn");
  if (btn.textContent === "Create new token") {
    clearTokenForm();
  } else {
    generateToken().catch((err) => {
      console.error("Token generation failed:", err);
      notify(
        "error",
        "Token generation failed",
        "Check the server logs and retry.",
      );
      document.getElementById("generateTokenBtn").disabled = false;
      document.getElementById("generateTokenBtn").textContent =
        "Generate token";
    });
  }
}

function newTokenForBucket(bucket = currentBucket) {
  clearTokenForm();
  showView("new-token");
  selectBucket(bucket);
}

function clearTokenForm() {
  document.getElementById("tokenBucket").value = "";
  document.getElementById("tokenEmail").value = "";
  document.getElementById("tokenName").value = "";
  document.getElementById("tokenExpiry").value = DEFAULT_EXPIRY_DAYS;
  document.getElementById("tokenAllowReplay").checked = true;
  document.getElementById("tokenLanguage").value =
    appSettings.uiLanguage || "en";
  document.getElementById("tokenOutputWrapper").hidden = true;
  document.getElementById("tokenOutput").textContent = "";
  document.getElementById("generateTokenBtn").textContent = "Generate token";
  document.getElementById("viewTokenBucketBtn").hidden = true;
  document.getElementById("tokenDoubleOptInNotice").hidden = true;
  document.getElementById("tokenSkipEmailGroup").hidden = true;
  document.getElementById("tokenSendConfirmation").checked = true;
  document.getElementById("tokenFormFields").classList.remove("frozen");

  tokenPermissions.clear();
  removedPermissions.clear();
  tokenDefaultOptOut = false;
  renderPermissionsGrid();

  tokenCustomFields = {};
  renderTokenCustomFieldsDisplay();
}

async function generateToken() {
  const bucket = document
    .getElementById("tokenBucket")
    .value.trim()
    .toLowerCase()
    .replace(/\s+/g, "_")
    .replace(/[^a-z0-9_-]/g, "");
  if (!bucket) {
    notify("warning", "Bucket name required", "Enter a bucket name.");
    return;
  }

  const email = document.getElementById("tokenEmail").value.trim();
  if (!email) {
    notify("warning", "Email required", "Enter an email address.");
    return;
  }

  const permissions = getPermissionStates();
  if (Object.keys(permissions).length === 0) {
    notify(
      "warning",
      "Permission required",
      "Set at least one permission to In or Out.",
    );
    return;
  }

  const btn = document.getElementById("generateTokenBtn");
  btn.disabled = true;

  try {
    // Check if email already exists in this bucket
    const checkResult = await apiRequest(
      `/api/admin/buckets/${encodeURIComponent(bucket)}/check-email`,
      {
        method: "POST",
        body: { email },
      },
    );

    if (checkResult.ok && checkResult.data?.exists) {
      notify(
        "error",
        "Duplicate email",
        `The email address already exists in ${bucket}.`,
      );
      return;
    }

    const expiryDays =
      parseInt(document.getElementById("tokenExpiry").value) ||
      DEFAULT_EXPIRY_DAYS;
    const allowReplay = document.getElementById("tokenAllowReplay").checked;
    const language = document.getElementById("tokenLanguage").value;
    const customFields =
      Object.keys(tokenCustomFields).length > 0 ? tokenCustomFields : undefined;
    const skipConfirmationEmail = !document.getElementById(
      "tokenSendConfirmation",
    ).checked;

    const nameVal =
      document.getElementById("tokenName").value.trim() || undefined;
    const result = await apiRequest("/api/tokens/generate", {
      method: "POST",
      body: [
        {
          bucket,
          email,
          name: nameVal,
          permissions,
          expiryDays,
          allowReplay,
          language,
          customFields,
          skipConfirmationEmail,
        },
      ],
    });

    if (result.ok) {
      const tokenUrl = `${PUBLIC_URL || API_BASE}/u/${result.data[0].token}`;
      document.getElementById("tokenOutput").textContent = tokenUrl;
      document.getElementById("tokenOutputWrapper").hidden = false;
      document.getElementById("tokenOutput").hidden = false;
      document.getElementById("viewTokenBucketBtn").hidden = false;
      document
        .getElementById("tokenOutputWrapper")
        .scrollIntoView({ behavior: "smooth", block: "nearest" });
      btn.textContent = "Create new token";

      // Use the server-confirmed doubleOptIn flag to determinee whether to show the confirmation email notice, not the client-side bucket setting which may be stalee
      const doubleOptInConfirmed =
        result.data[0].doubleOptIn ?? tokenDefaultOptOut;
      tokenDefaultOptOut = doubleOptInConfirmed;
      const hasOptIn = Object.values(permissions).some((v) => v === true);
      document.getElementById("tokenDoubleOptInNotice").hidden = !(doubleOptInConfirmed && hasOptIn);
      document.getElementById("tokenSkipEmailGroup").hidden = true;

      const toastMsg =
        doubleOptInConfirmed && hasOptIn
          ? "Token generated and confirmation email queued."
          : "Token created.";
      notify("success", "Token generated", toastMsg);

      // Freeze the form fields until the user starts a new token
      document.getElementById("tokenFormFields").classList.add("frozen");

      loadBuckets();
    }
  } finally {
    btn.disabled = false;
  }
}

function openTokenBucketView() {
  const bucket = document
    .getElementById("tokenBucket")
    .value.trim()
    .toLowerCase()
    .replace(/\s+/g, "_")
    .replace(/[^a-z0-9_-]/g, "");
  const email = document.getElementById("tokenEmail").value.trim();
  if (!bucket) {
    notify("warning", "Bucket required", "Enter a bucket name first.");
    return;
  }
  const params = new URLSearchParams({ view: "bucket", bucket });
  if (email) {
    params.set("q", email);
    params.set("qtype", "email");
  }
  window.location.href = `${window.location.pathname}?${params}`;
}

function copyTokenLink() {
  const text = document.getElementById("tokenOutput").textContent;
  clipboardWrite(text);
  notify("success", "Token URL copied");
}

let copyClickTimer = null;

function copyLabel(text) {
  return text.length > 24 ? text.slice(0, 16) + "…" : text;
}

function copyText(text) {
  clearTimeout(copyClickTimer);
  copyClickTimer = setTimeout(() => {
    clipboardWrite(text);
    notify("success", "Copied", copyLabel(text));
  }, 250);
}

function copyTextNow(text) {
  clearTimeout(copyClickTimer);
  clipboardWrite(text);
  notify("success", "Copied", copyLabel(text));
}

function copyInput(id) {
  clipboardWrite(document.getElementById(id).value);
  notify("success", "Copied");
}

// HELPERS
async function clipboardWrite(text) {
  if (navigator.clipboard && navigator.clipboard.writeText) {
    return navigator.clipboard.writeText(text);
  }
  const ta = document.createElement("textarea");
  ta.value = text;
  ta.style.position = "fixed";
  ta.style.left = "-9999px";
  document.body.appendChild(ta);
  ta.select();
  document.execCommand("copy");
  document.body.removeChild(ta);
}

function formatPermission(p) {
  if (!p) return "";
  // Convert snake_case or kebab-case to Title Case
  return p.replace(/[-_]/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());
}

function formatDate(dateStr) {
  if (!dateStr) return "N/A";
  // Server stores UTC but may serialize without Z suffix to ensure UTC parsing
  if (
    !dateStr.endsWith("Z") &&
    !dateStr.includes("+") &&
    !dateStr.includes("-", 10)
  )
    dateStr += "Z";
  const date = new Date(dateStr);
  if (isNaN(date.getTime())) return "N/A";
  return date.toLocaleString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

// REMOVE BUCKET
let generatedPassphrase = "";
let bucketToRemove = "";

// ARCHIVE
let archiveTargetBucket = "";

function showArchiveModal(bucket) {
  archiveTargetBucket = bucket;
  document.getElementById("archiveBucketName").textContent = bucket;
  openModal("archiveModal");
  closeAllMenus();
}

function closeArchiveModal() {
  closeModal("archiveModal");
  archiveTargetBucket = "";
}

async function confirmArchive() {
  if (!archiveTargetBucket) return;
  const bucket = archiveTargetBucket;
  const result = await apiRequest(
    `/api/admin/buckets/${encodeURIComponent(bucket)}/archive`,
    { method: "POST" },
  );
  closeArchiveModal();
  if (result.ok) {
    notify("success", "Bucket archived", `${bucket} is read-only.`);
    if (currentBucket === bucket) {
      currentBucketArchived = true;
      loadBucket(currentBucket);
    }
    await loadOverview(true);
  }
}

function showUnarchiveModal(bucket) {
  archiveTargetBucket = bucket;
  document.getElementById("unarchiveBucketName").textContent = bucket;
  openModal("unarchiveModal");
  closeAllMenus();
}

function closeUnarchiveModal() {
  closeModal("unarchiveModal");
  archiveTargetBucket = "";
}

async function confirmUnarchive() {
  if (!archiveTargetBucket) return;
  const bucket = archiveTargetBucket;
  const result = await apiRequest(
    `/api/admin/buckets/${encodeURIComponent(bucket)}/unarchive`,
    { method: "POST" },
  );
  closeUnarchiveModal();
  if (result.ok) {
    notify("success", "Bucket unarchived", `${bucket} accepts changes again.`);
    if (currentBucket === bucket) {
      currentBucketArchived = false;
      loadBucket(currentBucket);
    }
    await loadOverview(true);
  }
}

function toggleArchiveFromOptions() {
  closeOptionsModal();
  if (currentBucketArchived) {
    showUnarchiveModal(currentBucket);
  } else {
    showArchiveModal(currentBucket);
  }
}

function initiateRemoval() {
  if (!currentBucket) return;
  if (!currentBucketArchived) {
    notify(
      "error",
      "Archive first",
      "Archive the bucket in Options before removing it.",
    );
    return;
  }
  bucketToRemove = currentBucket;
  showRemoveModal();
}

function initiateBucketRemoval(bucket) {
  bucketToRemove = bucket;
  showRemoveModal();
  closeAllMenus();
}

function showRemoveModal() {
  generatedPassphrase = makePassphrase();

  document.getElementById("passphraseDisplay").textContent =
    generatedPassphrase;
  document.getElementById("passphraseInput").value = "";
  document.getElementById("confirmRemoveBtn").classList.remove("active");
  openModal("removeModal");
}

function verifyPassphrase() {
  const input = document
    .getElementById("passphraseInput")
    .value.toUpperCase()
    .trim();
  const btn = document.getElementById("confirmRemoveBtn");

  if (input === generatedPassphrase) {
    btn.classList.add("active");
  } else {
    btn.classList.remove("active");
  }
}

async function confirmRemoval() {
  if (!bucketToRemove) return;

  const result = await apiRequest(
    `/api/admin/buckets/${encodeURIComponent(bucketToRemove)}`,
    {
      method: "DELETE",
    },
  );

  if (result.ok) {
    notify(
      "success",
      "Bucket removed",
      `${bucketToRemove} is deleted.`,
    );
    closeRemoveModal();
    await loadBuckets();
    showView("overview");
  }
}

function closeRemoveModal() {
  closeModal("removeModal");
  generatedPassphrase = "";
  bucketToRemove = "";
}

const PASSPHRASE_WORDS = [
  "PHOTON", "CHIRP", "JITTER", "PARITY", "LUMEN", "GOSSIP", "LATENCY", "QUORUM", "UPSTREAM", "PACKET",
  "PULSAR", "QUASAR", "ENTROPY", "NONCE", "CIPHER", "MANTISSA", "MODULO", "KERNEL", "SOCKET", "BINARY",
  "REEF", "PORT", "DOCK", "VOYAGE", "CROWNEST", "AHOY", "BILGE", "SCALLYWAG", "CUTLASS", "STARBOARD",
  "KEELHAUL", "LANDLUBBER", "SEADOG", "YOHOHO", "BRIG", "CAPSTAN", "GALLEON", "JOLLYROGER", "MAROONED",
  "PLUNDER", "RIGGING", "SWASHBUCKLE", "ANCHOR", "DEADRECKON",
];

// Typed confirmation for destructive actions; not a secret.
function makePassphrase() {
  return Array.from({ length: 3 }, () => PASSPHRASE_WORDS[Math.floor(Math.random() * PASSPHRASE_WORDS.length)]).join(" ");
}

// BUCKET PERMISSIONS MANAGEMENT
let bucketPermsData = [];
let permToRemove = "";
let permRemovePassphrase = "";

function renderBucketPermsGrid() {
  const container = document.getElementById("bucketPermsGrid");
  const badge = document.getElementById("bucketPermsBadge");
  badge.textContent = bucketPermsData.length;
  if (bucketPermsData.length > 0) {
    badge.classList.add("active");
  } else {
    badge.classList.remove("active");
  }

  paint(container, bucketPermsData.length === 0
    ? html`<p class="list-empty">No permissions. Add one below, or generate a token to create them.</p>`
    : html`${bucketPermsData.map((p) => html`
        <div class="permission-row" data-perm=${p.permission}>
          <span class="permission-name">${formatPermission(p.permission)}</span>
          <span class="permission-count">${p.optedIn} in / ${p.optedOut} out</span>
          <button type="button" class="btn-remove" @click=${() => showPermRemoveModal(p.permission)} title="Remove" aria-label="Remove">&times;</button>
        </div>`)}`);
}

async function loadBucketPerms() {
  if (!currentBucket) return;
  const result = await apiRequest(
    `/api/admin/buckets/${encodeURIComponent(currentBucket)}`,
  );
  if (result.ok) {
    bucketPermsData = (result.data.stats || []).map((s) => ({
      permission: s.permission,
      optedIn: s.optedIn,
      optedOut: s.optedOut,
    }));
    renderBucketPermsGrid();
  }
}

async function addBucketPermission() {
  const input = document.getElementById("newBucketPermInput");
  const perm = input.value
    .trim()
    .toLowerCase()
    .replace(/\s+/g, "_")
    .replace(/[^a-z0-9_-]/g, "");

  if (!perm) {
    notify("warning", "Permission name required", "Enter a permission name.");
    return;
  }

  if (bucketPermsData.some((p) => p.permission === perm)) {
    notify("warning", "Duplicate permission", "The bucket already has this permission.");
    return;
  }

  const result = await apiRequest(
    `/api/admin/buckets/${encodeURIComponent(currentBucket)}/permissions`,
    {
      method: "POST",
      body: { permission: perm },
    },
  );

  if (result.ok) {
    input.value = "";
    notify(
      "success",
      "Permission added",
      `${formatPermission(perm)} is added to the bucket.`,
    );
    await loadBucketPerms();
    await loadBucket(currentBucket);
  }
}

function showPermRemoveModal(perm) {
  permToRemove = perm;
  document.getElementById("permRemoveName").textContent =
    formatPermission(perm);

  permRemovePassphrase = makePassphrase();

  document.getElementById("permPassphraseDisplay").textContent =
    permRemovePassphrase;
  document.getElementById("permPassphraseInput").value = "";
  document.getElementById("confirmPermRemoveBtn").classList.remove("active");
  openModal("permissionRemoveModal");
}

function verifyPermPassphrase() {
  const input = document
    .getElementById("permPassphraseInput")
    .value.toUpperCase()
    .trim();
  const btn = document.getElementById("confirmPermRemoveBtn");

  if (input === permRemovePassphrase) {
    btn.classList.add("active");
  } else {
    btn.classList.remove("active");
  }
}

async function confirmPermRemoval() {
  if (!permToRemove || !currentBucket) return;

  const result = await apiRequest(
    `/api/admin/buckets/${encodeURIComponent(currentBucket)}/permissions/${encodeURIComponent(permToRemove)}`,
    {
      method: "DELETE",
    },
  );

  if (result.ok) {
    notify(
      "success",
      "Permission removed",
      `${formatPermission(permToRemove)} and its records are deleted.`,
    );
    closePermRemoveModal();
    await loadBucketPerms();
    // Refresh the main bucket view
    await loadBucket(currentBucket);
  }
}

function closePermRemoveModal() {
  closeModal("permissionRemoveModal");
  permRemovePassphrase = "";
  permToRemove = "";
}
