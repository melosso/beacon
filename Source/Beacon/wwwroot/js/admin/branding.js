// ── Personalisation / Brand Identities ────────────────────────────────────

const NON_EMAIL_SAFE_FONTS = new Set(["inter", "manrope", "system"]);
const IDENTITY_FONTS = ["Arial", "Helvetica", "Georgia", "Tahoma", "Verdana", "Trebuchet MS", "Courier New", "Inter", "Manrope"];

let _brandIdentities = [];

// Colour values are interpolated into style attributes; anything but a hex colour falls back.
function safeColor(value, fallback = "#6366f1") {
  return /^#[0-9a-f]{3,8}$/i.test(value || "") ? value : fallback;
}

function getBrandIdentityForBucket(bucket) {
  return (
    _brandIdentities.find(
      (i) => !i.isDefault && (i.buckets || []).includes(bucket),
    ) || null
  );
}

async function loadBrandIdentities() {
  const list = document.getElementById("brand-identities-list");
  if (!list) return;
  const res = await apiRequest("/api/admin/brand-identities");
  if (!res.ok) {
    paint(list, html`<div style="padding:1rem;color:hsl(var(--destructive));font-size:0.875rem">Failed to load identities.</div>`);
    return;
  }
  _brandIdentities = res.data || [];
  renderBrandIdentities();
}

function renderBrandIdentities() {
  const list = document.getElementById("brand-identities-list");
  if (!list) return;
  paint(list, _brandIdentities.length
    ? html`${_brandIdentities.map(identityCard)}`
    : html`<div style="padding:1rem 0;font-size:0.875rem;color:hsl(var(--muted-foreground))">No identities found.</div>`);
}

let _editingIdentityId = null;

const BEACON_AVATAR = html`<div class="brand-avatar" style="width:36px;height:36px;min-width:36px;flex-shrink:0" aria-hidden="true">
  <svg viewBox="0 0 20 20" fill="none" xmlns="http://www.w3.org/2000/svg" class="brand-avatar-bg">
    <g clip-path="url(#beacon-avatar-clip)">
      <rect width="20" height="20" fill="#000" rx="5.5"/>
      <rect width="20" height="20" fill="url(#beacon-avatar-gradient)" fill-opacity="0.2" rx="5.5"/>
      <g filter="url(#beacon-avatar-blur-1)" opacity="0.3"><circle cx="16" cy="17" r="6" fill="#FF64B4" fill-opacity="0.671"/></g>
      <g filter="url(#beacon-avatar-blur-2)" opacity="0.1"><circle cx="16" cy="16" r="6" fill="#FF64B4" fill-opacity="0.671"/></g>
      <g filter="url(#beacon-avatar-blur-3)" opacity="0.4"><circle cx="17" cy="19" r="6" fill="#FF64B4" fill-opacity="0.671"/></g>
      <rect width="20" height="20" fill="#FF64B4" fill-opacity="0.15" rx="5.5"/>
      <g style="mix-blend-mode:hard-light"><rect width="20" height="20" fill="#6A62FF" fill-opacity="0.1" rx="5.5"/></g>
    </g>
    <rect width="19" height="19" x="0.5" y="0.5" stroke="#FDFDFD" stroke-opacity="0.1" rx="5"/>
  </svg>
  <svg class="brand-avatar-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="#FDFDFD" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
    <path d="M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5"/>
  </svg>
</div>`;

const GEAR_ICON = html`<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="3"></circle><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"></path></svg>`;

function logoSrc(logo) {
  if (!logo) return null;
  return logo.type === "base64" ? logo.data : logo.url;
}

function identityCard(identity) {
  const buckets = identity.buckets || [];
  const s = identity.settings || {};
  const src = logoSrc(s.logo);
  const accent = s.primaryAccent ? safeColor(s.primaryAccent) : null;
  const avatar = src
    ? html`<img src=${src} alt="" style="width:36px;height:36px;border-radius:6px;object-fit:cover;flex-shrink:0;border:1px solid hsl(var(--border)/0.5)">`
    : identity.isDefault
      ? BEACON_AVATAR
      : html`<div style="width:36px;height:36px;border-radius:6px;flex-shrink:0;background:${accent
          ? `linear-gradient(135deg,${accent},${accent}55)`
          : "linear-gradient(135deg,hsl(var(--primary)/0.7),hsl(var(--primary)/0.25))"}"></div>`;
  const desc = identity.isDefault
    ? "All unassigned buckets"
    : buckets.length
      ? `${buckets.length} bucket${buckets.length !== 1 ? "s" : ""}`
      : "No buckets assigned";
  return html`
    <div class="settings-item-card settings-item-card--split" id="identity-card-${identity.id}">
      <div style="flex:1;padding:1rem;display:flex;align-items:center;gap:1rem">
        ${avatar}
        <div class="settings-item-info" style="flex:1">
          <span class="settings-item-title select-none">${identity.name}${identity.isDefault ? html`<span class="identity-default-badge" style="margin-left:0.5rem">Default</span>` : nothing}</span>
          <p class="settings-item-desc">${desc}</p>
        </div>
      </div>
      <button class="btn-settings-gear" @click=${() => openEditBrandIdentityModal(identity.id)} aria-label="Edit identity">${GEAR_ICON}</button>
    </div>`;
}

function identityEditBody(identity) {
  const id = identity.id;
  const s = identity.settings || {};
  const isDefault = identity.isDefault;
  const accent = safeColor(s.primaryAccent);
  const surface = safeColor(s.surfaceColour, "#ffffff");
  const refresh = () => updateIdentityPreview(id);
  const mockBtnStyle = isDefault ? "" : `background:${accent};color:${contrastFg(accent)}`;

  const preview = html`
    <div class="personalisation-preview">
      <p class="personalisation-preview-hd">Preview</p>
      <div class="personalisation-preview-grid">
        <div class="preview-frame">
          <div class="preview-frame-label">Email</div>
          <div class="preview-frame-body">
            <div class="preview-email-mock" id="bi-preview-email-${id}">
              <div id="bi-preview-logo-email-${id}"></div>
              <div class="preview-mock-title" id="bi-preview-email-title-${id}">One more step to complete your sign-up</div>
              <div class="preview-mock-body" id="bi-preview-email-body-${id}">Click the button below to confirm.</div>
              <div class="preview-mock-btn" id="bi-preview-email-btn-${id}" style=${mockBtnStyle}>Yes, sign me up</div>
              <div class="preview-mock-footer" id="bi-preview-email-footer-${id}"></div>
            </div>
          </div>
        </div>
        <div class="preview-frame">
          <div class="preview-frame-label">Opt-out page</div>
          <div class="preview-frame-body">
            <div class="preview-page-mock" id="bi-preview-page-${id}">
              <div id="bi-preview-logo-page-${id}"></div>
              <div class="preview-mock-title" id="bi-preview-page-title-${id}">Email preferences</div>
              <div class="preview-mock-body" id="bi-preview-page-body-${id}">You're receiving these emails because you previously opted in.</div>
              <div class="preview-mock-btn" id="bi-preview-page-btn-${id}" style=${mockBtnStyle}>Save preferences</div>
              <div class="preview-mock-footer" id="bi-preview-page-footer-${id}"></div>
            </div>
          </div>
        </div>
      </div>
    </div>`;

  const logoField = html`
    <div class="identity-field-row" style="align-items:flex-start">
      <div class="settings-item-info">
        <span class="identity-field-label">Logo</span>
        <p class="identity-field-desc">Shown above the heading in emails and on the opt-out page.</p>
      </div>
      <div class="identity-field-control" style="min-width:240px">
        <div class="logo-upload-area" id="bi-logo-area-${id}" @click=${() => triggerLogoUpload(id)}>
          ${logoPreview(s.logo)}
          <span class="logo-upload-text"><strong>Upload image</strong><br>or paste a URL below</span>
          ${s.logo ? html`<button class="logo-remove-btn" type="button" @click=${(e) => { e.stopPropagation(); removeLogo(id); }}>Remove</button>` : nothing}
        </div>
        <input type="file" id="bi-logo-file-${id}" accept="image/*" style="display:none" aria-label="Upload logo"
          @change=${(e) => handleLogoFileChange(id, e.target)} />
        <input type="text" class="identity-input" id="bi-logo-url-${id}" aria-label="Logo URL"
          .value=${s.logo?.type === "url" ? s.logo.url || "" : ""}
          placeholder="https://example.com/logo.png"
          @input=${(e) => handleLogoUrlInput(id, e.target.value)} />
      </div>
    </div>`;

  const themeField = html`
    <div class="identity-field-row" style="align-items:flex-start">
      <div class="settings-item-info">
        <span class="identity-field-label">Theme</span>
        <p class="identity-field-desc">Colour scheme for emails and the opt-out page.</p>
      </div>
      <div class="identity-field-control">
        <div class="theme-radio-group">
          ${["light", "dark", "system"].map((th) => html`
            <label class="theme-option" for="bi-theme-${id}-${th}">
              <input type="radio" name="bi-theme-${id}" id="bi-theme-${id}-${th}" value=${th}
                ?checked=${(s.theme || "system") === th} @change=${refresh} />
              <div class="theme-card">
                <div class="theme-preview-inner theme-preview--${th}">
                  ${th === "system" ? html`<div class="tp-half tp-half--light"></div><div class="tp-half tp-half--dark"></div>` : nothing}
                </div>
              </div>
              <span class="theme-option-label">${th.charAt(0).toUpperCase() + th.slice(1)}</span>
            </label>`)}
        </div>
      </div>
    </div>`;

  const fontField = html`
    <div class="identity-field-row" style="align-items:center">
      <div class="settings-item-info">
        <span class="identity-field-label">Font</span>
        <p class="identity-field-desc">Applied to emails and the opt-out page.</p>
      </div>
      <div class="identity-field-control">
        <select class="appearance-select" id="bi-font-${id}" aria-label="Font" @change=${() => onIdentityFontChange(id)} style="min-width:160px">
          <option value="">System default</option>
          ${IDENTITY_FONTS.map((f) => html`<option value=${f} ?selected=${s.font === f}>${f}</option>`)}
        </select>
        <span class="font-warning-badge" id="bi-font-warn-${id}" style=${NON_EMAIL_SAFE_FONTS.has((s.font || "").toLowerCase()) ? "" : "display:none"}>
          &#9888; May not render in all email clients
        </span>
      </div>
    </div>`;

  if (isDefault) {
    return html`
      <p style="font-size:0.8125rem;color:hsl(var(--muted-foreground));margin:0;padding:0.625rem 0.875rem;background:hsl(var(--muted)/0.4);border-radius:var(--radius);user-select: none;">The default identity styles all unassigned buckets. It is a system fallback: name, colours, text and footer are locked.</p>
      <div>
        <p class="identity-section-hd">Appearance</p>
        ${logoField}
        ${themeField}
        ${fontField}
      </div>
      <div>
        <p class="identity-section-hd">Buckets</p>
        <p class="identity-field-desc" style="margin:0">Applies to all unassigned buckets.</p>
      </div>
      ${preview}`;
  }

  const colourRow = (type, label, desc, value) => html`
    <div class="identity-field-row" style="margin-bottom:0.75rem">
      <div class="settings-item-info">
        <span class="identity-field-label">${label}</span>
        <p class="identity-field-desc">${desc}</p>
      </div>
      <div class="identity-field-control">
        <div class="colour-picker-row">
          <input type="color" id="bi-${type}-picker-${id}" aria-label="${label} colour" .value=${value}
            @input=${() => syncColourInput(id, type)} />
          <input type="text" class="colour-hex-input" id="bi-${type}-hex-${id}" aria-label="${label} hex value" .value=${value}
            maxlength="7" placeholder=${value}
            @input=${() => { syncColourPicker(id, type); refresh(); }} />
        </div>
      </div>
    </div>`;

  const field = (key, label, value, placeholder, multiline, live = true) => html`
    <div>
      <label class="identity-field-label" for="bi-${key}-${id}">${label}</label>
      ${multiline
        ? html`<textarea class="identity-textarea" id="bi-${key}-${id}" rows="2" placeholder=${placeholder} .value=${value || ""} @input=${live ? refresh : null}></textarea>`
        : html`<input type="text" class="identity-input" id="bi-${key}-${id}" placeholder=${placeholder} .value=${value || ""} @input=${live ? refresh : null} />`}
    </div>`;

  const copySection = (section, label, unlocked, fields, divider = true) => html`
    <div style="${divider ? "border-top:1px solid hsl(var(--border)/0.5);padding-top:0.75rem;margin-bottom:0.75rem" : "border-top:1px solid hsl(var(--border)/0.5);padding-top:0.75rem"}">
      <div style="display:flex;align-items:baseline;justify-content:space-between;gap:0.75rem;margin-bottom:0.375rem">
        <span style="font-size:0.8125rem;font-weight:500;color:hsl(var(--foreground))">${label}</span>
        <div id="bi-copy-${section}-gate-${id}" style=${unlocked ? "display:none" : ""}>
          <button class="btn btn-outline" style="font-size:0.8125rem;padding:0.25rem 0.75rem" @click=${() => unlockCopySection(id, section)}>Customise</button>
        </div>
      </div>
      <div id="bi-copy-${section}-fields-${id}" style="display:${unlocked ? "flex" : "none"};flex-direction:column;gap:0.625rem">
        <div style="display:flex;justify-content:flex-end">
          <button class="btn btn-outline" style="font-size:0.8125rem;padding:0.25rem 0.75rem" @click=${() => resetCopySection(id, section)}>Reset</button>
        </div>
        ${fields}
      </div>
    </div>`;

  return html`
    <div class="identity-field-row" style="align-items:center">
      <div class="settings-item-info" style="flex:1">
        <label class="identity-field-label" for="bi-name-${id}">Identity name</label>
      </div>
      <div class="identity-field-control">
        <input type="text" class="identity-input" style="min-width:200px" id="bi-name-${id}" .value=${identity.name} @input=${refresh} />
      </div>
    </div>

    <div>
      <p class="identity-section-hd">Appearance</p>
      ${colourRow("accent", "Accent", "Buttons and interactive elements.", accent)}
      ${colourRow("surface", "Surface", "Page and email background.", surface)}
      ${logoField}
      ${themeField}
      ${fontField}
    </div>

    <div>
      <p class="identity-section-hd">Copy</p>
      <p class="identity-field-desc" style="margin:0 0 0.875rem">Custom copy overrides automatic locale translations for that section.</p>
      ${copySection("page", "Opt-out page", !!(s.pageTitle || s.pageBody || s.browserTitle), html`
        ${field("browser-title", "Browser tab title", s.browserTitle, "Email Preferences")}
        ${field("page-title", "Page heading", s.pageTitle, "Email preferences")}
        ${field("page-body", "Body text", s.pageBody, "You're receiving these emails because you previously opted in.", true)}`)}
      ${copySection("email", "Email", !!(s.emailTitle || s.emailBody), html`
        ${field("email-title", "Heading", s.emailTitle, "One more step to complete your sign-up")}
        ${field("email-body", "Body text", s.emailBody, "Click the button below to confirm.", true)}`)}
      ${copySection("confirm", "Confirmation", !!(s.confirmTitle || s.confirmMsg), html`
        ${field("confirm-title", "Heading", s.confirmTitle, "Subscription confirmed", false, false)}
        ${field("confirm-msg", "Message", s.confirmMsg, "Your subscription has been confirmed. You're now opted in.", true, false)}`, false)}
    </div>

    <div>
      <p class="identity-section-hd">Footer</p>
      <p class="identity-field-desc" style="margin:0 0 0.5rem">Shown at the bottom of emails and the opt-out page.</p>
      <textarea class="identity-textarea" id="bi-footer-${id}" rows="2" aria-label="Footer" placeholder="© 2025 Your Company" .value=${s.footer || ""} @input=${refresh}></textarea>
    </div>

    <div>
      <p class="identity-section-hd">Buckets</p>
      <p class="identity-field-desc" style="margin:0 0 0.5rem">Selected buckets use this identity. Unselected buckets use the default.</p>
      <div class="bucket-pill-list" id="bi-buckets-${id}">
        <span style="font-size:0.8125rem;color:hsl(var(--muted-foreground))">Loading...</span>
      </div>
    </div>

    ${preview}`;
}

function openEditBrandIdentityModal(id) {
  const identity = _brandIdentities.find((i) => i.id === id);
  if (!identity) return;
  _editingIdentityId = id;
  document.getElementById("editBrandIdentityTitle").textContent = `Edit — ${identity.name}`;
  paint(document.getElementById("editBrandIdentityBody"), identityEditBody(identity));
  document.getElementById("editBrandIdentityDeleteBtn").style.display = identity.isDefault ? "none" : "";
  document.getElementById("editBrandIdentityModal").style.display = "flex";
  renderBucketAssignment(id);
  updateIdentityPreview(id);
}

function closeEditBrandIdentityModal() {
  document.getElementById("editBrandIdentityModal").style.display = "none";
  _editingIdentityId = null;
}

async function saveEditBrandIdentity() {
  if (_editingIdentityId === null) return;
  const btn = document.getElementById("editBrandIdentityBtn");
  btn.classList.add("is-loading");
  btn.disabled = true;
  try {
    const ok = await saveBrandIdentity(_editingIdentityId);
    if (ok) closeEditBrandIdentityModal();
  } finally {
    btn.classList.remove("is-loading");
    btn.disabled = false;
  }
}

function deleteEditingBrandIdentity() {
  if (_editingIdentityId === null) return;
  const id = _editingIdentityId;
  closeEditBrandIdentityModal();
  deleteBrandIdentity(id);
}

function logoPreview(logo) {
  const src = logoSrc(logo);
  return src
    ? html`<img class="logo-thumbnail" src=${src} alt="Logo" />`
    : html`<div class="logo-upload-placeholder"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true"><rect width="18" height="18" x="3" y="3" rx="2"/><path d="m9 9 3-3 3 3"/><path d="M12 6v12"/></svg></div>`;
}

async function renderBucketAssignment(identityId) {
  const container = document.getElementById(`bi-buckets-${identityId}`);
  if (!container) return;
  const res = await apiRequest("/api/admin/buckets");
  if (!res.ok) {
    paint(container, html`<span style="font-size:0.8125rem;color:hsl(var(--destructive))">Failed to load buckets.</span>`);
    return;
  }
  const allBuckets = (res.data || []).map((b) => (typeof b === "string" ? b : b.bucket || b.name || String(b)));
  const identity = _brandIdentities.find((i) => i.id === identityId);
  const assigned = new Set(identity?.buckets || []);
  paint(container, allBuckets.length
    ? html`${allBuckets.map((bucket) => html`
        <button type="button" class="bucket-pill${assigned.has(bucket) ? " is-assigned" : ""}"
          aria-pressed=${assigned.has(bucket)} data-bucket=${bucket}
          @click=${(e) => toggleBucketPill(e.currentTarget)}>${bucket}</button>`)}`
    : html`<span style="font-size:0.8125rem;color:hsl(var(--muted-foreground))">No buckets configured.</span>`);
}

function toggleBucketPill(btn) {
  const isAssigned = btn.classList.toggle("is-assigned");
  btn.setAttribute("aria-pressed", String(isAssigned));
}

function syncColourInput(id, type) {
  const isAccent = type === "accent";
  const picker = document.getElementById(
    `bi-${isAccent ? "accent-picker" : "surface-picker"}-${id}`,
  );
  const hex = document.getElementById(
    `bi-${isAccent ? "accent-hex" : "surface-hex"}-${id}`,
  );
  if (picker && hex) hex.value = picker.value;
  updateIdentityPreview(id);
}

function syncColourPicker(id, type) {
  const isAccent = type === "accent";
  const picker = document.getElementById(
    `bi-${isAccent ? "accent-picker" : "surface-picker"}-${id}`,
  );
  const hex = document.getElementById(
    `bi-${isAccent ? "accent-hex" : "surface-hex"}-${id}`,
  );
  if (picker && hex && /^#[0-9a-fA-F]{6}$/.test(hex.value))
    picker.value = hex.value;
}

function onIdentityFontChange(id) {
  const select = document.getElementById(`bi-font-${id}`);
  const warn = document.getElementById(`bi-font-warn-${id}`);
  if (select && warn)
    warn.style.display = NON_EMAIL_SAFE_FONTS.has(select.value.toLowerCase())
      ? ""
      : "none";
  updateIdentityPreview(id);
}

function triggerLogoUpload(id) {
  document.getElementById(`bi-logo-file-${id}`)?.click();
}

async function handleLogoFileChange(id, input) {
  const file = input.files && input.files[0];
  if (!file) return;
  if (file.size > 2 * 1024 * 1024) {
    notify("error", "File too large", "Maximum logo size is 2 MB.");
    return;
  }

  const reader = new FileReader();
  reader.onload = async (e) => {
    const base64Data = e.target.result;
    const formData = new FormData();
    formData.append("base64", base64Data);
    if (file) formData.append("file", file);
    try {
      const res = await fetch("/api/admin/assets/logo", {
        method: "POST",
        headers: { "X-Requested-With": "XMLHttpRequest" },
        body: formData,
      });
      if (!res.ok) throw new Error(await res.text());
      const asset = await res.json();
      applyLogoAsset(id, asset);
    } catch (err) {
      notify("error", "Upload failed", String(err));
    }
  };
  reader.readAsDataURL(file);
}

function handleLogoUrlInput(id, url) {
  if (!url) {
    applyLogoAsset(id, null);
    return;
  }
  if (/^https?:\/\/.+/.test(url)) applyLogoAsset(id, { type: "url", url });
}

function applyLogoAsset(id, asset) {
  const identity = _brandIdentities.find((i) => i.id === id);
  if (identity) {
    if (!identity.settings) identity.settings = {};
    identity.settings.logo = asset;
  }
  const area = document.getElementById(`bi-logo-area-${id}`);
  if (!area) return;
  const existing = area.querySelector(
    ".logo-thumbnail, .logo-upload-placeholder",
  );
  if (existing) existing.remove();
  const removeBtn = area.querySelector(".logo-remove-btn");
  if (!asset) {
    if (removeBtn) removeBtn.remove();
    return;
  }
  const img = document.createElement("img");
  img.className = "logo-thumbnail";
  img.src = asset.type === "base64" ? asset.data : asset.url;
  img.alt = "Logo";
  area.prepend(img);
  if (!removeBtn) {
    const btn = document.createElement("button");
    btn.className = "logo-remove-btn";
    btn.type = "button";
    btn.textContent = "Remove";
    btn.onclick = (ev) => {
      ev.stopPropagation();
      removeLogo(id);
    };
    area.append(btn);
  }
  updateIdentityPreview(id);
}

function removeLogo(id) {
  const urlInput = document.getElementById(`bi-logo-url-${id}`);
  if (urlInput) urlInput.value = "";
  applyLogoAsset(id, null);
}

function updateIdentityPreview(id) {
  const accent =
    (document.getElementById(`bi-accent-hex-${id}`) || {}).value || "#6366f1";
  const surface =
    (document.getElementById(`bi-surface-hex-${id}`) || {}).value || "#ffffff";
  const emailTitle =
    (document.getElementById(`bi-email-title-${id}`) || {}).value ||
    "One more step to complete your sign-up";
  const emailBody =
    (document.getElementById(`bi-email-body-${id}`) || {}).value ||
    "Click the button below to confirm your email address.";
  const pageTitle =
    (document.getElementById(`bi-page-title-${id}`) || {}).value ||
    "Email preferences";
  const pageBody =
    (document.getElementById(`bi-page-body-${id}`) || {}).value ||
    "You're receiving these emails because you previously opted in.";
  const footer = (document.getElementById(`bi-footer-${id}`) || {}).value || "";

  const themeInput = document.querySelector(
    `input[name="bi-theme-${id}"]:checked`,
  );
  const theme = themeInput ? themeInput.value : "system";
  const isDark = theme === "dark";
  const bg = isDark ? (surface !== "#ffffff" ? surface : "#0f0f0f") : surface;
  const fg = isDark ? "#e7e7e7" : contrastFg(bg);

  const identity = _brandIdentities.find((i) => i.id === id);
  const src = logoSrc(identity?.settings?.logo);
  const logoImg = src
    ? html`<img class="preview-mock-logo" src=${src} alt="Logo" style="max-width:80px;max-height:32px;display:block;margin:0 0 0.5rem;object-fit:contain" />`
    : nothing;

  const emailMock = document.getElementById(`bi-preview-email-${id}`);
  if (emailMock) {
    emailMock.style.background = bg;
    emailMock.style.color = fg;
    paint(document.getElementById(`bi-preview-logo-email-${id}`), logoImg);
    document.getElementById(`bi-preview-email-title-${id}`).textContent =
      emailTitle;
    document.getElementById(`bi-preview-email-body-${id}`).textContent =
      emailBody;
    const btn = document.getElementById(`bi-preview-email-btn-${id}`);
    if (btn) {
      const c = identity?.isDefault ? fg : accent;
      btn.style.background = c;
      btn.style.color = contrastFg(c);
    }
    document.getElementById(`bi-preview-email-footer-${id}`).textContent =
      footer;
  }

  const pageMock = document.getElementById(`bi-preview-page-${id}`);
  if (pageMock) {
    pageMock.style.background = bg;
    pageMock.style.color = fg;
    paint(document.getElementById(`bi-preview-logo-page-${id}`), logoImg);
    document.getElementById(`bi-preview-page-title-${id}`).textContent =
      pageTitle;
    document.getElementById(`bi-preview-page-body-${id}`).textContent =
      pageBody;
    const btn2 = document.getElementById(`bi-preview-page-btn-${id}`);
    if (btn2) {
      const c = identity?.isDefault ? fg : accent;
      btn2.style.background = c;
      btn2.style.color = contrastFg(c);
    }
    document.getElementById(`bi-preview-page-footer-${id}`).textContent =
      footer;
  }
}

function contrastFg(hexBg) {
  try {
    const hex = hexBg.replace("#", "");
    if (hex.length < 6) return "#111111";
    const r = parseInt(hex.slice(0, 2), 16);
    const g = parseInt(hex.slice(2, 4), 16);
    const b = parseInt(hex.slice(4, 6), 16);
    return (0.299 * r + 0.587 * g + 0.114 * b) / 255 > 0.5
      ? "#111111"
      : "#ffffff";
  } catch (e) {
    return "#111111";
  }
}

function collectIdentitySettings(id) {
  const identity = _brandIdentities.find((i) => i.id === id);
  const themeInput = document.querySelector(
    `input[name="bi-theme-${id}"]:checked`,
  );
  return {
    primaryAccent:
      (document.getElementById(`bi-accent-hex-${id}`) || {}).value || null,
    surfaceColour:
      (document.getElementById(`bi-surface-hex-${id}`) || {}).value || null,
    theme: themeInput ? themeInput.value : "system",
    logo: identity && identity.settings ? identity.settings.logo || null : null,
    emailTitle:
      (document.getElementById(`bi-email-title-${id}`) || {}).value || null,
    emailBody:
      (document.getElementById(`bi-email-body-${id}`) || {}).value || null,
    pageTitle:
      (document.getElementById(`bi-page-title-${id}`) || {}).value || null,
    pageBody:
      (document.getElementById(`bi-page-body-${id}`) || {}).value || null,
    confirmTitle:
      (document.getElementById(`bi-confirm-title-${id}`) || {}).value || null,
    confirmMsg:
      (document.getElementById(`bi-confirm-msg-${id}`) || {}).value || null,
    footer: (document.getElementById(`bi-footer-${id}`) || {}).value || null,
    browserTitle:
      (document.getElementById(`bi-browser-title-${id}`) || {}).value || null,
    font: (document.getElementById(`bi-font-${id}`) || {}).value || null,
  };
}

function collectAssignedBuckets(id) {
  const container = document.getElementById(`bi-buckets-${id}`);
  if (!container) return [];
  return Array.from(container.querySelectorAll(".bucket-pill.is-assigned")).map(
    (btn) => btn.dataset.bucket,
  );
}

async function saveBrandIdentity(id) {
  const identity = _brandIdentities.find((i) => i.id === id);
  if (!identity) return false;
  const nameEl = document.getElementById(`bi-name-${id}`);
  const name = nameEl ? (nameEl.value || "").trim() : identity.name;
  const settings = collectIdentitySettings(id);
  const buckets = collectAssignedBuckets(id);

  const r1 = await apiRequest(`/api/admin/brand-identities/${id}`, {
    method: "PUT",
    body: { name, settings },
  });
  if (!r1.ok) return false;
  const r2 = await apiRequest(`/api/admin/brand-identities/${id}/buckets`, {
    method: "PUT",
    body: { buckets },
  });
  if (!r2.ok) return false;
  notify("success", "Saved", `Brand identity "${name}" updated.`);
  await loadBrandIdentities();
  return true;
}

function unlockCopySection(id, section) {
  document.getElementById(`bi-copy-${section}-gate-${id}`).style.display =
    "none";
  document.getElementById(`bi-copy-${section}-fields-${id}`).style.display =
    "flex";
  const first = document.querySelector(
    `#bi-copy-${section}-fields-${id} input, #bi-copy-${section}-fields-${id} textarea`,
  );
  if (first) first.focus();
}

function resetCopySection(id, section) {
  const fields = document.getElementById(`bi-copy-${section}-fields-${id}`);
  fields.querySelectorAll("input, textarea").forEach((el) => {
    el.value = "";
  });
  fields.style.display = "none";
  document.getElementById(`bi-copy-${section}-gate-${id}`).style.display = "";
  updateIdentityPreview(id);
}

function showCreateBrandIdentityModal() {
  const input = document.getElementById("newBrandIdentityName");
  const err = document.getElementById("newBrandIdentityError");
  input.value = "";
  err.style.display = "none";
  document.getElementById("createBrandIdentityModal").style.display = "flex";
  setTimeout(() => input.focus(), 50);
}

function closeCreateBrandIdentityModal() {
  document.getElementById("createBrandIdentityModal").style.display = "none";
}

function validateBrandIdentityName() {
  const val = (
    document.getElementById("newBrandIdentityName").value || ""
  ).trim();
  const err = document.getElementById("newBrandIdentityError");
  if (!val) {
    err.textContent = "Name is required.";
    err.style.display = "block";
    return false;
  }
  if (val.length > 100) {
    err.textContent = "Name must be 100 characters or fewer.";
    err.style.display = "block";
    return false;
  }
  err.style.display = "none";
  return true;
}

async function confirmCreateBrandIdentity() {
  if (!validateBrandIdentityName()) return;
  const name = document.getElementById("newBrandIdentityName").value.trim();
  const btn = document.getElementById("createBrandIdentityBtn");
  btn.classList.add("is-loading");
  btn.disabled = true;
  try {
    const res = await apiRequest("/api/admin/brand-identities", {
      method: "POST",
      body: { name },
    });
    if (!res.ok) return;
    closeCreateBrandIdentityModal();
    await loadBrandIdentities();
    notify("success", "Created", `Brand identity "${name}" created.`);
  } finally {
    btn.classList.remove("is-loading");
    btn.disabled = false;
  }
}

function createBrandIdentity() {
  showCreateBrandIdentityModal();
}

async function deleteBrandIdentity(id) {
  const identity = _brandIdentities.find((i) => i.id === id);
  if (!identity) return;
  openConfirmModal(
    "Delete Identity",
    html`Delete <strong>${identity.name}</strong>? Buckets assigned to it will revert to Default.`,
    "Delete",
    async () => {
      const result = await apiRequest(`/api/admin/brand-identities/${id}`, {
        method: "DELETE",
      });
      if (result.ok) {
        await loadBrandIdentities();
        notify("success", "Deleted", "Brand identity deleted.");
      }
    },
  );
}

// ── End Personalisation ───────────────────────────────────────────────────

init();
