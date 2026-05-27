    // ── Personalisation / Brand Identities ────────────────────────────────────

    function escHtml(str) {
      return String(str || '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
    }

    const NON_EMAIL_SAFE_FONTS = new Set(['inter', 'manrope', 'system']);

    let _brandIdentities = [];

    function getBrandIdentityForBucket(bucket) {
      return _brandIdentities.find(i => !i.isDefault && (i.buckets || []).includes(bucket)) || null;
    }

    async function loadBrandIdentities() {
      const list = document.getElementById('brand-identities-list');
      if (!list) return;
      try {
        const res = await apiRequest('/api/admin/brand-identities');
        _brandIdentities = res.data || [];
        renderBrandIdentities();
      } catch (e) {
        list.innerHTML = '<div style="padding:1rem;color:hsl(var(--destructive));font-size:0.875rem">Failed to load identities.</div>';
      }
    }

    function renderBrandIdentities() {
      const list = document.getElementById('brand-identities-list');
      if (!list) return;
      if (!_brandIdentities.length) {
        list.innerHTML = '<div style="padding:1rem 0;font-size:0.875rem;color:hsl(var(--muted-foreground))">No identities found.</div>';
        return;
      }
      list.innerHTML = _brandIdentities.map(identity => buildIdentityCardHtml(identity)).join('');
    }

    let _editingIdentityId = null;

    const _beaconAvatarHtml = `<div class="brand-avatar" style="width:36px;height:36px;min-width:36px;flex-shrink:0" aria-hidden="true">
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

    function buildIdentityCardHtml(identity) {
      const id = identity.id;
      const buckets = identity.buckets || [];
      const isDefault = identity.isDefault;
      const s = identity.settings || {};
      const logo = s.logo;
      const accent = s.primaryAccent;
      let avatarHtml;
      if (logo?.type === 'base64' && logo.data) {
        avatarHtml = `<img src="${escHtml(logo.data)}" alt="" style="width:36px;height:36px;border-radius:6px;object-fit:cover;flex-shrink:0;border:1px solid hsl(var(--border)/0.5)">`;
      } else if ((logo?.type === 'url' || logo?.type === 'objectStorage') && logo.url) {
        avatarHtml = `<img src="${escHtml(logo.url)}" alt="" style="width:36px;height:36px;border-radius:6px;object-fit:cover;flex-shrink:0;border:1px solid hsl(var(--border)/0.5)">`;
      } else if (isDefault) {
        avatarHtml = _beaconAvatarHtml;
      } else {
        const bg = accent
          ? `linear-gradient(135deg,${escHtml(accent)},${escHtml(accent)}55)`
          : `linear-gradient(135deg,hsl(var(--primary)/0.7),hsl(var(--primary)/0.25))`;
        avatarHtml = `<div style="width:36px;height:36px;border-radius:6px;flex-shrink:0;background:${bg}"></div>`;
      }
      return `
        <div class="settings-item-card settings-item-card--split" id="identity-card-${id}">
          <div style="flex:1;padding:1rem;display:flex;align-items:center;gap:1rem">
            ${avatarHtml}
            <div class="settings-item-info" style="flex:1">
              <span class="settings-item-title select-none">${escHtml(identity.name)}${isDefault ? '<span class="identity-default-badge" style="margin-left:0.5rem">Default</span>' : ''}</span>
              <p class="settings-item-desc">${isDefault ? 'All unassigned buckets' : buckets.length ? `${buckets.length} bucket${buckets.length !== 1 ? 's' : ''}` : 'No buckets assigned'}</p>
            </div>
          </div>
          <button class="btn-settings-gear" onclick="openEditBrandIdentityModal(${id})" aria-label="Edit identity">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="3"></circle><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"></path></svg>
          </button>
        </div>`;
    }

    function buildIdentityEditBodyHtml(identity) {
      const id = identity.id;
      const s = identity.settings || {};
      const isDefault = identity.isDefault;

      const previewHtml = `
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
                  <div class="preview-mock-btn" id="bi-preview-email-btn-${id}" ${isDefault ? '' : `style="background:${s.primaryAccent||'#6366f1'};color:${contrastFg(s.primaryAccent||'#6366f1')}"`}>Yes, sign me up</div>
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
                  <div class="preview-mock-btn" id="bi-preview-page-btn-${id}" ${isDefault ? '' : `style="background:${s.primaryAccent||'#6366f1'};color:${contrastFg(s.primaryAccent||'#6366f1')}"`}>Save preferences</div>
                  <div class="preview-mock-footer" id="bi-preview-page-footer-${id}"></div>
                </div>
              </div>
            </div>
          </div>
        </div>`;

      const logoFieldHtml = `
        <div class="identity-field-row" style="align-items:flex-start">
          <div class="settings-item-info">
            <span class="identity-field-label">Logo</span>
            <p class="identity-field-desc">Shown above the heading in emails and on the opt-out page.</p>
          </div>
          <div class="identity-field-control" style="min-width:240px">
            <div class="logo-upload-area" onclick="triggerLogoUpload(${id})" id="bi-logo-area-${id}">
              ${buildLogoPreviewHtml(id, s.logo)}
              <span class="logo-upload-text"><strong>Upload image</strong><br>or paste a URL below</span>
              ${s.logo ? `<button class="logo-remove-btn" type="button" onclick="event.stopPropagation();removeLogo(${id})">Remove</button>` : ''}
            </div>
            <input type="file" id="bi-logo-file-${id}" accept="image/*" style="display:none"
              onchange="handleLogoFileChange(${id}, this)" />
            <input type="text" class="identity-input" id="bi-logo-url-${id}"
              value="${s.logo && s.logo.type === 'url' ? escHtml(s.logo.url || '') : ''}"
              placeholder="https://example.com/logo.png"
              oninput="handleLogoUrlInput(${id}, this.value)" />
          </div>
        </div>`;

      const themeFieldHtml = `
        <div class="identity-field-row" style="align-items:flex-start">
          <div class="settings-item-info">
            <span class="identity-field-label">Theme</span>
            <p class="identity-field-desc">Colour scheme for emails and the opt-out page.</p>
          </div>
          <div class="identity-field-control">
            <div class="theme-radio-group">
              ${['light','dark','system'].map(th => `
                <label class="theme-option" for="bi-theme-${id}-${th}">
                  <input type="radio" name="bi-theme-${id}" id="bi-theme-${id}-${th}" value="${th}"
                    ${(s.theme || 'system') === th ? 'checked' : ''}
                    onchange="updateIdentityPreview(${id})" />
                  <div class="theme-card">
                    <div class="theme-preview-inner ${th === 'system' ? 'theme-preview--system' : `theme-preview--${th}`}">
                      ${th === 'system' ? '<div class="tp-half tp-half--light"></div><div class="tp-half tp-half--dark"></div>' : ''}
                    </div>
                  </div>
                  <span class="theme-option-label">${th.charAt(0).toUpperCase() + th.slice(1)}</span>
                </label>`).join('')}
            </div>
          </div>
        </div>`;

      const fontFieldHtml = `
        <div class="identity-field-row" style="align-items:center">
          <div class="settings-item-info">
            <span class="identity-field-label">Font</span>
            <p class="identity-field-desc">Applied to emails and the opt-out page.</p>
          </div>
          <div class="identity-field-control">
            <select class="appearance-select" id="bi-font-${id}" onchange="onIdentityFontChange(${id})" style="min-width:160px">
              <option value="">System default</option>
              <option value="Arial" ${s.font === 'Arial' ? 'selected' : ''}>Arial</option>
              <option value="Helvetica" ${s.font === 'Helvetica' ? 'selected' : ''}>Helvetica</option>
              <option value="Georgia" ${s.font === 'Georgia' ? 'selected' : ''}>Georgia</option>
              <option value="Tahoma" ${s.font === 'Tahoma' ? 'selected' : ''}>Tahoma</option>
              <option value="Verdana" ${s.font === 'Verdana' ? 'selected' : ''}>Verdana</option>
              <option value="Trebuchet MS" ${s.font === 'Trebuchet MS' ? 'selected' : ''}>Trebuchet MS</option>
              <option value="Courier New" ${s.font === 'Courier New' ? 'selected' : ''}>Courier New</option>
              <option value="Inter" ${s.font === 'Inter' ? 'selected' : ''}>Inter</option>
              <option value="Manrope" ${s.font === 'Manrope' ? 'selected' : ''}>Manrope</option>
            </select>
            <span class="font-warning-badge" id="bi-font-warn-${id}" ${NON_EMAIL_SAFE_FONTS.has((s.font || '').toLowerCase()) ? '' : 'style="display:none"'}>
              &#9888; May not render in all email clients
            </span>
          </div>
        </div>`;

      if (isDefault) {
        return `
          <p style="font-size:0.8125rem;color:hsl(var(--muted-foreground));margin:0;padding:0.625rem 0.875rem;background:hsl(var(--muted)/0.4);border-radius:var(--radius);user-select: none;">The default identity automatically styles all unassigned buckets. Because it is a system fallback, the name, colors, text, and footer remain locked against changes.</p>
          <div>
            <p class="identity-section-hd">Appearance</p>
            ${logoFieldHtml}
            ${themeFieldHtml}
            ${fontFieldHtml}
          </div>
          <div>
            <p class="identity-section-hd">Buckets</p>
            <p class="identity-field-desc" style="margin:0">Applies to all unassigned buckets.</p>
          </div>
          ${previewHtml}`;
      }

      const hasPageCopy = !!(s.pageTitle || s.pageBody || s.browserTitle);
      const hasEmailCopy = !!(s.emailTitle || s.emailBody);
      const hasConfirmCopy = !!(s.confirmTitle || s.confirmMsg);

      return `
        <div class="identity-field-row" style="align-items:center">
          <div class="settings-item-info" style="flex:1">
            <span class="identity-field-label">Identity name</span>
          </div>
          <div class="identity-field-control">
            <input type="text" class="identity-input" style="min-width:200px"
              id="bi-name-${id}" value="${escHtml(identity.name)}"
              oninput="updateIdentityPreview(${id})" />
          </div>
        </div>

        <div>
          <p class="identity-section-hd">Appearance</p>
          <div class="identity-field-row" style="margin-bottom:0.75rem">
            <div class="settings-item-info">
              <span class="identity-field-label">Accent</span>
              <p class="identity-field-desc">Buttons and interactive elements.</p>
            </div>
            <div class="identity-field-control">
              <div class="colour-picker-row">
                <input type="color" id="bi-accent-picker-${id}" value="${s.primaryAccent || '#6366f1'}"
                  oninput="syncColourInput(${id},'accent')" />
                <input type="text" class="colour-hex-input" id="bi-accent-hex-${id}" value="${s.primaryAccent || '#6366f1'}"
                  maxlength="7" placeholder="#6366f1"
                  oninput="syncColourPicker(${id},'accent');updateIdentityPreview(${id})" />
              </div>
            </div>
          </div>
          <div class="identity-field-row" style="margin-bottom:0.75rem">
            <div class="settings-item-info">
              <span class="identity-field-label">Surface</span>
              <p class="identity-field-desc">Page and email background.</p>
            </div>
            <div class="identity-field-control">
              <div class="colour-picker-row">
                <input type="color" id="bi-surface-picker-${id}" value="${s.surfaceColour || '#ffffff'}"
                  oninput="syncColourInput(${id},'surface')" />
                <input type="text" class="colour-hex-input" id="bi-surface-hex-${id}" value="${s.surfaceColour || '#ffffff'}"
                  maxlength="7" placeholder="#ffffff"
                  oninput="syncColourPicker(${id},'surface');updateIdentityPreview(${id})" />
              </div>
            </div>
          </div>
          ${logoFieldHtml}
          ${themeFieldHtml}
          ${fontFieldHtml}
        </div>

        <div>
          <p class="identity-section-hd">Copy</p>
          <p class="identity-field-desc" style="margin:0 0 0.875rem">Custom copy overrides automatic locale translations for that section.</p>

          <div style="border-top:1px solid hsl(var(--border)/0.5);padding-top:0.75rem;margin-bottom:0.75rem">
            <div style="display:flex;align-items:baseline;justify-content:space-between;gap:0.75rem;margin-bottom:0.375rem">
              <span style="font-size:0.8125rem;font-weight:500;color:hsl(var(--foreground))">Opt-out page</span>
              <div id="bi-copy-page-gate-${id}" style="${hasPageCopy ? 'display:none' : ''}">
                <button class="btn btn-outline" style="font-size:0.8125rem;padding:0.25rem 0.75rem" onclick="unlockCopySection(${id},'page')">Customise</button>
              </div>
            </div>
            <div id="bi-copy-page-fields-${id}" style="${hasPageCopy ? 'display:flex' : 'display:none'};flex-direction:column;gap:0.625rem">
              <div style="display:flex;justify-content:flex-end">
                <button class="btn btn-outline" style="font-size:0.8125rem;padding:0.25rem 0.75rem" onclick="resetCopySection(${id},'page')">Reset</button>
              </div>
              <div>
                <label class="identity-field-label" for="bi-browser-title-${id}">Browser tab title</label>
                <input type="text" class="identity-input" id="bi-browser-title-${id}"
                  value="${escHtml(s.browserTitle || '')}" placeholder="Email Preferences"
                  oninput="updateIdentityPreview(${id})" />
              </div>
              <div>
                <label class="identity-field-label" for="bi-page-title-${id}">Page heading</label>
                <input type="text" class="identity-input" id="bi-page-title-${id}"
                  value="${escHtml(s.pageTitle || '')}" placeholder="Email preferences"
                  oninput="updateIdentityPreview(${id})" />
              </div>
              <div>
                <label class="identity-field-label" for="bi-page-body-${id}">Body text</label>
                <textarea class="identity-textarea" id="bi-page-body-${id}" rows="2"
                  placeholder="You're receiving these emails because you previously opted in."
                  oninput="updateIdentityPreview(${id})">${escHtml(s.pageBody || '')}</textarea>
              </div>
            </div>
          </div>

          <div style="border-top:1px solid hsl(var(--border)/0.5);padding-top:0.75rem;margin-bottom:0.75rem">
            <div style="display:flex;align-items:baseline;justify-content:space-between;gap:0.75rem;margin-bottom:0.375rem">
              <span style="font-size:0.8125rem;font-weight:500;color:hsl(var(--foreground))">Email</span>
              <div id="bi-copy-email-gate-${id}" style="${hasEmailCopy ? 'display:none' : ''}">
                <button class="btn btn-outline" style="font-size:0.8125rem;padding:0.25rem 0.75rem" onclick="unlockCopySection(${id},'email')">Customise</button>
              </div>
            </div>
            <div id="bi-copy-email-fields-${id}" style="${hasEmailCopy ? 'display:flex' : 'display:none'};flex-direction:column;gap:0.625rem">
              <div style="display:flex;justify-content:flex-end">
                <button class="btn btn-outline" style="font-size:0.8125rem;padding:0.25rem 0.75rem" onclick="resetCopySection(${id},'email')">Reset</button>
              </div>
              <div>
                <label class="identity-field-label" for="bi-email-title-${id}">Heading</label>
                <input type="text" class="identity-input" id="bi-email-title-${id}"
                  value="${escHtml(s.emailTitle || '')}" placeholder="One more step to complete your sign-up"
                  oninput="updateIdentityPreview(${id})" />
              </div>
              <div>
                <label class="identity-field-label" for="bi-email-body-${id}">Body text</label>
                <textarea class="identity-textarea" id="bi-email-body-${id}" rows="2"
                  placeholder="Click the button below to confirm."
                  oninput="updateIdentityPreview(${id})">${escHtml(s.emailBody || '')}</textarea>
              </div>
            </div>
          </div>

          <div style="border-top:1px solid hsl(var(--border)/0.5);padding-top:0.75rem">
            <div style="display:flex;align-items:baseline;justify-content:space-between;gap:0.75rem;margin-bottom:0.375rem">
              <span style="font-size:0.8125rem;font-weight:500;color:hsl(var(--foreground))">Confirmation</span>
              <div id="bi-copy-confirm-gate-${id}" style="${hasConfirmCopy ? 'display:none' : ''}">
                <button class="btn btn-outline" style="font-size:0.8125rem;padding:0.25rem 0.75rem" onclick="unlockCopySection(${id},'confirm')">Customise</button>
              </div>
            </div>
            <div id="bi-copy-confirm-fields-${id}" style="${hasConfirmCopy ? 'display:flex' : 'display:none'};flex-direction:column;gap:0.625rem">
              <div style="display:flex;justify-content:flex-end">
                <button class="btn btn-outline" style="font-size:0.8125rem;padding:0.25rem 0.75rem" onclick="resetCopySection(${id},'confirm')">Reset</button>
              </div>
              <div>
                <label class="identity-field-label" for="bi-confirm-title-${id}">Heading</label>
                <input type="text" class="identity-input" id="bi-confirm-title-${id}"
                  value="${escHtml(s.confirmTitle || '')}" placeholder="Subscription confirmed" />
              </div>
              <div>
                <label class="identity-field-label" for="bi-confirm-msg-${id}">Message</label>
                <textarea class="identity-textarea" id="bi-confirm-msg-${id}" rows="2"
                  placeholder="Your subscription has been confirmed. You're now opted in.">${escHtml(s.confirmMsg || '')}</textarea>
              </div>
            </div>
          </div>
        </div>

        <div>
          <p class="identity-section-hd">Footer</p>
          <p class="identity-field-desc" style="margin:0 0 0.5rem">Shown at the bottom of emails and the opt-out page.</p>
          <textarea class="identity-textarea" id="bi-footer-${id}" rows="2"
            placeholder="&#169; 2025 Your Company"
            oninput="updateIdentityPreview(${id})">${escHtml(s.footer || '')}</textarea>
        </div>

        <div>
          <p class="identity-section-hd">Buckets</p>
          <p class="identity-field-desc" style="margin:0 0 0.5rem">Choose buckets for this identity, unselected buckets will keep the default.</p>
          <div class="bucket-pill-list" id="bi-buckets-${id}">
            <span style="font-size:0.8125rem;color:hsl(var(--muted-foreground))">Loading...</span>
          </div>
        </div>

        ${previewHtml}`;
    }

    function openEditBrandIdentityModal(id) {
      const identity = _brandIdentities.find(i => i.id === id);
      if (!identity) return;
      _editingIdentityId = id;
      document.getElementById('editBrandIdentityTitle').textContent = `Edit — ${identity.name}`;
      document.getElementById('editBrandIdentityBody').innerHTML = buildIdentityEditBodyHtml(identity);
      document.getElementById('editBrandIdentityDeleteBtn').style.display = identity.isDefault ? 'none' : '';
      document.getElementById('editBrandIdentityModal').style.display = 'flex';
      renderBucketAssignment(id);
      updateIdentityPreview(id);
    }

    function closeEditBrandIdentityModal() {
      document.getElementById('editBrandIdentityModal').style.display = 'none';
      _editingIdentityId = null;
    }

    async function saveEditBrandIdentity() {
      if (_editingIdentityId === null) return;
      const btn = document.getElementById('editBrandIdentityBtn');
      btn.classList.add('is-loading'); btn.disabled = true;
      try {
        const ok = await saveBrandIdentity(_editingIdentityId);
        if (ok) closeEditBrandIdentityModal();
      } finally {
        btn.classList.remove('is-loading'); btn.disabled = false;
      }
    }

    function deleteEditingBrandIdentity() {
      if (_editingIdentityId === null) return;
      const id = _editingIdentityId;
      closeEditBrandIdentityModal();
      deleteBrandIdentity(id);
    }

    function buildLogoPreviewHtml(id, logo) {
      if (!logo) {
        return `<div class="logo-upload-placeholder"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true"><rect width="18" height="18" x="3" y="3" rx="2"/><path d="m9 9 3-3 3 3"/><path d="M12 6v12"/></svg></div>`;
      }
      const src = logo.type === 'base64' ? logo.data : logo.url;
      return `<img class="logo-thumbnail" src="${escHtml(src || '')}" alt="Logo" />`;
    }

    async function renderBucketAssignment(identityId) {
      const container = document.getElementById(`bi-buckets-${identityId}`);
      if (!container) return;
      try {
        const res = await apiRequest('/api/admin/buckets');
        const allBuckets = (res.data || []).map(b => typeof b === 'string' ? b : (b.bucket || b.name || String(b)));
        const identity = _brandIdentities.find(i => i.id === identityId);
        const assigned = new Set(identity ? (identity.buckets || []) : []);
        if (!allBuckets.length) {
          container.innerHTML = '<span style="font-size:0.8125rem;color:hsl(var(--muted-foreground))">No buckets configured.</span>';
          return;
        }
        container.innerHTML = allBuckets.map(bucket => `
          <button type="button" class="bucket-pill${assigned.has(bucket) ? ' is-assigned' : ''}"
                  aria-pressed="${assigned.has(bucket)}"
                  data-bucket="${escHtml(bucket)}"
                  onclick="toggleBucketPill(this)">${escHtml(bucket)}</button>`).join('');
      } catch {
        container.innerHTML = '<span style="font-size:0.8125rem;color:hsl(var(--destructive))">Failed to load buckets.</span>';
      }
    }

    function toggleBucketPill(btn) {
      const isAssigned = btn.classList.toggle('is-assigned');
      btn.setAttribute('aria-pressed', String(isAssigned));
    }

    function syncColourInput(id, type) {
      const isAccent = type === 'accent';
      const picker = document.getElementById(`bi-${isAccent ? 'accent-picker' : 'surface-picker'}-${id}`);
      const hex = document.getElementById(`bi-${isAccent ? 'accent-hex' : 'surface-hex'}-${id}`);
      if (picker && hex) hex.value = picker.value;
      updateIdentityPreview(id);
    }

    function syncColourPicker(id, type) {
      const isAccent = type === 'accent';
      const picker = document.getElementById(`bi-${isAccent ? 'accent-picker' : 'surface-picker'}-${id}`);
      const hex = document.getElementById(`bi-${isAccent ? 'accent-hex' : 'surface-hex'}-${id}`);
      if (picker && hex && /^#[0-9a-fA-F]{6}$/.test(hex.value)) picker.value = hex.value;
    }

    function onIdentityFontChange(id) {
      const select = document.getElementById(`bi-font-${id}`);
      const warn = document.getElementById(`bi-font-warn-${id}`);
      if (select && warn) warn.style.display = NON_EMAIL_SAFE_FONTS.has(select.value.toLowerCase()) ? '' : 'none';
      updateIdentityPreview(id);
    }

    function triggerLogoUpload(id) {
      document.getElementById(`bi-logo-file-${id}`)?.click();
    }

    async function handleLogoFileChange(id, input) {
      const file = input.files && input.files[0];
      if (!file) return;
      if (file.size > 2 * 1024 * 1024) { notify('error', 'File too large', 'Maximum logo size is 2 MB.'); return; }

      const reader = new FileReader();
      reader.onload = async (e) => {
        const base64Data = e.target.result;
        const formData = new FormData();
        formData.append('base64', base64Data);
        if (file) formData.append('file', file);
        try {
          const res = await fetch('/api/admin/assets/logo', {
            method: 'POST',
            headers: { 'X-Requested-With': 'XMLHttpRequest' },
            body: formData
          });
          if (!res.ok) throw new Error(await res.text());
          const asset = await res.json();
          applyLogoAsset(id, asset);
        } catch (err) {
          notify('error', 'Upload failed', String(err));
        }
      };
      reader.readAsDataURL(file);
    }

    function handleLogoUrlInput(id, url) {
      if (!url) { applyLogoAsset(id, null); return; }
      if (/^https?:\/\/.+/.test(url)) applyLogoAsset(id, { type: 'url', url });
    }

    function applyLogoAsset(id, asset) {
      const identity = _brandIdentities.find(i => i.id === id);
      if (identity) {
        if (!identity.settings) identity.settings = {};
        identity.settings.logo = asset;
      }
      const area = document.getElementById(`bi-logo-area-${id}`);
      if (!area) return;
      const existing = area.querySelector('.logo-thumbnail, .logo-upload-placeholder');
      if (existing) existing.remove();
      const removeBtn = area.querySelector('.logo-remove-btn');
      if (!asset) { if (removeBtn) removeBtn.remove(); return; }
      const img = document.createElement('img');
      img.className = 'logo-thumbnail';
      img.src = asset.type === 'base64' ? asset.data : asset.url;
      img.alt = 'Logo';
      area.prepend(img);
      if (!removeBtn) {
        const btn = document.createElement('button');
        btn.className = 'logo-remove-btn';
        btn.type = 'button';
        btn.textContent = 'Remove';
        btn.onclick = (ev) => { ev.stopPropagation(); removeLogo(id); };
        area.append(btn);
      }
      updateIdentityPreview(id);
    }

    function removeLogo(id) {
      const urlInput = document.getElementById(`bi-logo-url-${id}`);
      if (urlInput) urlInput.value = '';
      applyLogoAsset(id, null);
    }

    function updateIdentityPreview(id) {
      const accent = (document.getElementById(`bi-accent-hex-${id}`) || {}).value || '#6366f1';
      const surface = (document.getElementById(`bi-surface-hex-${id}`) || {}).value || '#ffffff';
      const emailTitle = (document.getElementById(`bi-email-title-${id}`) || {}).value || 'One more step to complete your sign-up';
      const emailBody = (document.getElementById(`bi-email-body-${id}`) || {}).value || 'Click the button below to confirm.';
      const pageTitle = (document.getElementById(`bi-page-title-${id}`) || {}).value || 'Email preferences';
      const pageBody = (document.getElementById(`bi-page-body-${id}`) || {}).value || "You're receiving these emails because you previously opted in.";
      const footer = (document.getElementById(`bi-footer-${id}`) || {}).value || '';

      const themeInput = document.querySelector(`input[name="bi-theme-${id}"]:checked`);
      const theme = themeInput ? themeInput.value : 'system';
      const isDark = theme === 'dark';
      const bg = isDark ? (surface !== '#ffffff' ? surface : '#0f0f0f') : surface;
      const fg = isDark ? '#e7e7e7' : contrastFg(bg);

      const identity = _brandIdentities.find(i => i.id === id);
      const logo = identity && identity.settings ? identity.settings.logo : null;
      const logoSrc = logo ? (logo.type === 'base64' ? logo.data : logo.url) : null;
      const logoHtml = logoSrc ? `<img class="preview-mock-logo" src="${escHtml(logoSrc)}" alt="Logo" style="max-width:80px;max-height:32px;display:block;margin:0 0 0.5rem;object-fit:contain" />` : '';

      const emailMock = document.getElementById(`bi-preview-email-${id}`);
      if (emailMock) {
        emailMock.style.background = bg;
        emailMock.style.color = fg;
        document.getElementById(`bi-preview-logo-email-${id}`).innerHTML = logoHtml;
        document.getElementById(`bi-preview-email-title-${id}`).textContent = emailTitle;
        document.getElementById(`bi-preview-email-body-${id}`).textContent = emailBody;
        const btn = document.getElementById(`bi-preview-email-btn-${id}`);
        if (btn && !identity?.isDefault) { btn.style.background = accent; btn.style.color = contrastFg(accent); }
        document.getElementById(`bi-preview-email-footer-${id}`).textContent = footer;
      }

      const pageMock = document.getElementById(`bi-preview-page-${id}`);
      if (pageMock) {
        pageMock.style.background = bg;
        pageMock.style.color = fg;
        document.getElementById(`bi-preview-logo-page-${id}`).innerHTML = logoHtml;
        document.getElementById(`bi-preview-page-title-${id}`).textContent = pageTitle;
        document.getElementById(`bi-preview-page-body-${id}`).textContent = pageBody;
        const btn2 = document.getElementById(`bi-preview-page-btn-${id}`);
        if (btn2 && !identity?.isDefault) { btn2.style.background = accent; btn2.style.color = contrastFg(accent); }
        document.getElementById(`bi-preview-page-footer-${id}`).textContent = footer;
      }
    }

    function contrastFg(hexBg) {
      try {
        const hex = hexBg.replace('#', '');
        if (hex.length < 6) return '#111111';
        const r = parseInt(hex.slice(0, 2), 16);
        const g = parseInt(hex.slice(2, 4), 16);
        const b = parseInt(hex.slice(4, 6), 16);
        return (0.299 * r + 0.587 * g + 0.114 * b) / 255 > 0.5 ? '#111111' : '#ffffff';
      } catch (e) { return '#111111'; }
    }

    function collectIdentitySettings(id) {
      const identity = _brandIdentities.find(i => i.id === id);
      const themeInput = document.querySelector(`input[name="bi-theme-${id}"]:checked`);
      return {
        primaryAccent: (document.getElementById(`bi-accent-hex-${id}`) || {}).value || null,
        surfaceColour: (document.getElementById(`bi-surface-hex-${id}`) || {}).value || null,
        theme: themeInput ? themeInput.value : 'system',
        logo: identity && identity.settings ? (identity.settings.logo || null) : null,
        emailTitle: (document.getElementById(`bi-email-title-${id}`) || {}).value || null,
        emailBody: (document.getElementById(`bi-email-body-${id}`) || {}).value || null,
        pageTitle: (document.getElementById(`bi-page-title-${id}`) || {}).value || null,
        pageBody: (document.getElementById(`bi-page-body-${id}`) || {}).value || null,
        confirmTitle: (document.getElementById(`bi-confirm-title-${id}`) || {}).value || null,
        confirmMsg: (document.getElementById(`bi-confirm-msg-${id}`) || {}).value || null,
        footer: (document.getElementById(`bi-footer-${id}`) || {}).value || null,
        browserTitle: (document.getElementById(`bi-browser-title-${id}`) || {}).value || null,
        font: (document.getElementById(`bi-font-${id}`) || {}).value || null,
      };
    }

    function collectAssignedBuckets(id) {
      const container = document.getElementById(`bi-buckets-${id}`);
      if (!container) return [];
      return Array.from(container.querySelectorAll('.bucket-pill.is-assigned')).map(btn => btn.dataset.bucket);
    }

    async function saveBrandIdentity(id) {
      const identity = _brandIdentities.find(i => i.id === id);
      if (!identity) return false;
      const nameEl = document.getElementById(`bi-name-${id}`);
      const name = nameEl ? (nameEl.value || '').trim() : identity.name;
      const settings = collectIdentitySettings(id);
      const buckets = collectAssignedBuckets(id);

      const r1 = await apiRequest(`/api/admin/brand-identities/${id}`, {
        method: 'PUT',
        body: { name, settings }
      });
      if (!r1.ok) return false;
      const r2 = await apiRequest(`/api/admin/brand-identities/${id}/buckets`, {
        method: 'PUT',
        body: { buckets }
      });
      if (!r2.ok) return false;
      notify('success', 'Saved', `Brand identity "${name}" updated.`);
      await loadBrandIdentities();
      return true;
    }

    function unlockCopySection(id, section) {
      document.getElementById(`bi-copy-${section}-gate-${id}`).style.display = 'none';
      document.getElementById(`bi-copy-${section}-fields-${id}`).style.display = 'flex';
      const first = document.querySelector(`#bi-copy-${section}-fields-${id} input, #bi-copy-${section}-fields-${id} textarea`);
      if (first) first.focus();
    }

    function resetCopySection(id, section) {
      const fields = document.getElementById(`bi-copy-${section}-fields-${id}`);
      fields.querySelectorAll('input, textarea').forEach(el => { el.value = ''; });
      fields.style.display = 'none';
      document.getElementById(`bi-copy-${section}-gate-${id}`).style.display = '';
      updateIdentityPreview(id);
    }

    function showCreateBrandIdentityModal() {
      const input = document.getElementById('newBrandIdentityName');
      const err = document.getElementById('newBrandIdentityError');
      input.value = '';
      err.style.display = 'none';
      document.getElementById('createBrandIdentityModal').style.display = 'flex';
      setTimeout(() => input.focus(), 50);
    }

    function closeCreateBrandIdentityModal() {
      document.getElementById('createBrandIdentityModal').style.display = 'none';
    }

    function validateBrandIdentityName() {
      const val = (document.getElementById('newBrandIdentityName').value || '').trim();
      const err = document.getElementById('newBrandIdentityError');
      if (!val) {
        err.textContent = 'Name is required.';
        err.style.display = 'block';
        return false;
      }
      if (val.length > 100) {
        err.textContent = 'Name must be 100 characters or fewer.';
        err.style.display = 'block';
        return false;
      }
      err.style.display = 'none';
      return true;
    }

    async function confirmCreateBrandIdentity() {
      if (!validateBrandIdentityName()) return;
      const name = document.getElementById('newBrandIdentityName').value.trim();
      const btn = document.getElementById('createBrandIdentityBtn');
      btn.classList.add('is-loading'); btn.disabled = true;
      try {
        await apiRequest('/api/admin/brand-identities', {
          method: 'POST',
          body: { name }
        });
        closeCreateBrandIdentityModal();
        await loadBrandIdentities();
        notify('success', 'Created', `Brand identity "${name}" created.`);
      } catch (e) {
        notify('error', 'Create failed', String(e));
      } finally {
        btn.classList.remove('is-loading'); btn.disabled = false;
      }
    }

    function createBrandIdentity() {
      showCreateBrandIdentityModal();
    }

    async function deleteBrandIdentity(id) {
      const identity = _brandIdentities.find(i => i.id === id);
      if (!identity) return;
      openConfirmModal(
        'Delete Identity',
        `Delete <strong>${escHtml(identity.name)}</strong>? Buckets assigned to it will revert to Default.`,
        'Delete',
        async () => {
          const result = await apiRequest(`/api/admin/brand-identities/${id}`, { method: 'DELETE' });
          if (result.ok) {
            await loadBrandIdentities();
            notify('success', 'Deleted', 'Brand identity deleted.');
          } else {
            notify('error', 'Delete failed', result.data?.error || 'Failed to delete identity.');
          }
        }
      );
    }

    // ── End Personalisation ───────────────────────────────────────────────────

    init();
