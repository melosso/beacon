    // SUBMISSION FORMS
    let nlOrigins = [];
    let nlEditId = null;
    let nlCurrentFormId = null;
    let nlCustomFields = {};

    function toggleNlMenu(event, idx) {
      event.stopPropagation();
      toggleMenu(`nlMenu-${idx}`, event.currentTarget);
    }

    async function loadSubmissionForms() {
      const result = await apiRequest('/api/admin/submissions');
      if (!result.ok) return;
      const forms = result.data || [];
      const body = document.getElementById('submissionBody');
      if (forms.length === 0) {
        body.innerHTML = '<tr><td colspan="7" style="text-align:center;padding:3rem;color:hsl(var(--muted-foreground))">No submission forms yet! Use the button above to create your first embeddable form.</td></tr>';
        return;
      }
      body.innerHTML = forms.map((f, idx) => `
        <tr>
          <td><strong>${sanitize(f.name)}</strong></td>
          <td><span class="tooltip-wrapper select-none" style="cursor:pointer" onclick="showBucket('${sanitize(f.bucket)}')"><code style="font-size:0.8rem">${sanitize(f.bucket)}</code><span class="tooltip">${sanitize(f.bucket)}</span></span></td>
          <td><span class="tooltip-wrapper select-none" style="cursor:pointer" onclick="copyTextNow('${sanitize(f.permission)}')"><span class="status-badge">${sanitize(formatPermission(f.permission))}</span><span class="tooltip">${sanitize(f.permission)}</span></span></td>
          <td>${(f.allowedOrigins || []).length}</td>
          <td>${f.submissionCount}</td>
          <td>
            <label class="checkbox-toggle" style="margin:0;border:none">
              <input type="checkbox" ${f.isEnabled ? 'checked' : ''} onchange="toggleSubmissionEnabled('${f.id}', this.checked)">
              <span class="toggle-track"></span>
            </label>
          </td>
          <td class="col-actions">
            <div class="row-actions">
              <span class="row-status-icons">
                ${buckets.some(b => b.name === f.bucket && b.isArchived) ? `<span class="tooltip-wrapper">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="hsl(var(--destructive))" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="vertical-align:middle">
                    <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path>
                    <line x1="12" y1="9" x2="12" y2="13"></line>
                    <line x1="12" y1="17" x2="12.01" y2="17"></line>
                  </svg>
                  <span class="tooltip tooltip-above tooltip-right">Bucket is archived, submissions will be rejected</span>
                </span>` : ''}
              </span>
              <span class="tooltip-wrapper">
                <button class="btn-actions" onclick="toggleNlMenu(event, ${idx})">:</button>
                <span class="tooltip tooltip-above tooltip-right">Actions</span>
              </span>
              <div class="dropdown-menu" id="nlMenu-${idx}">
                <button class="dropdown-item" onclick="editSubmissionForm('${f.id}')">Settings</button>
                <button class="dropdown-item" onclick="showEmbedCode('${f.id}')">View & Share <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="margin-left:auto;flex-shrink:0"><circle cx="18" cy="5" r="3"></circle><circle cx="6" cy="12" r="3"></circle><circle cx="18" cy="19" r="3"></circle><line x1="8.6" y1="13.5" x2="15.4" y2="17.5"></line><line x1="15.4" y1="6.5" x2="8.6" y2="10.5"></line></svg></button>
                <button class="dropdown-item" onclick="deleteSubmissionForm('${f.id}', '${sanitize(f.name)}')">Remove Form <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="margin-left:auto;flex-shrink:0"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path><line x1="10" y1="11" x2="10" y2="17"></line><line x1="14" y1="11" x2="14" y2="17"></line></svg></button>
              </div>
            </div>
          </td>
        </tr>
      `).join('');
    }

    function toggleRedirectFields() {
      const disabled = document.getElementById('nlDisableRedirects').checked;
      document.getElementById('nlRedirectSuccess').disabled = disabled;
      document.getElementById('nlRedirectError').disabled = disabled;
      document.getElementById('nlRedirectFormPost').disabled = disabled;
      document.getElementById('nlRedirectJsEmbed').disabled = disabled;
      const ids = ['nlRedirectSuccess', 'nlRedirectError', 'nlRedirectFormPost', 'nlRedirectJsEmbed'];
      ids.forEach(id => {
        const el = document.getElementById(id);
        const wrapper = el.closest('.form-group') || el.closest('.checkbox-toggle');
        if (wrapper) { wrapper.style.opacity = disabled ? '0.45' : ''; wrapper.style.pointerEvents = disabled ? 'none' : ''; }
        const track = el.closest('.checkbox-toggle')?.querySelector('.toggle-track');
        if (track) track.style.filter = disabled ? 'saturate(0)' : '';
      });
    }

    function initSubmissionWizard(editData) {
      nlOrigins = [];
      nlEditId = null;

      // Reset all fields to defaults
      document.getElementById('nlName').value = '';
      document.getElementById('nlBucket').value = '';
      document.getElementById('nlPermission').value = '';
      document.getElementById('nlTitle').value = 'Subscribe to our newsletter';
      document.getElementById('nlDescription').value = 'Get updates delivered to your inbox.';
      document.getElementById('nlButtonText').value = 'Subscribe';
      document.getElementById('nlSuccessMessage').value = 'Thanks for subscribing!';
      document.getElementById('nlPrimaryColor').value = '#2563eb';
      document.getElementById('nlBgColor').value = '#ffffff';
      document.getElementById('nlTextColor').value = '#111111';
      document.getElementById('nlBorderRadius').value = '8px';
      document.getElementById('nlRedirectSuccess').value = '';
      document.getElementById('nlRedirectError').value = '';
      document.getElementById('nlRedirectFormPost').checked = true;
      document.getElementById('nlRedirectJsEmbed').checked = false;
      document.getElementById('nlDisableRedirects').checked = false;
      toggleRedirectFields();
      document.getElementById('nlLanguage').value = appSettings.uiLanguage || 'en';
      document.getElementById('nlIsEnabled').checked = true;
      document.getElementById('nlConsentRequired').checked = true;
      document.getElementById('nlConsentText').value = '';
      document.getElementById('nlPrivacyPolicyUrl').value = '';
      nlCustomFields = {};
      document.getElementById('nlCustomFieldsList').innerHTML = '';
      document.getElementById('nlApiTokenDisplay').style.display = 'none';
      document.getElementById('nlBucketArchivedWarning').style.display = 'none';

      if (editData) {
        nlEditId = editData.id;
        document.getElementById('submissionWizardTitle').textContent = 'Edit Submission Form';
        document.getElementById('nlIntroTitle').textContent = 'Edit Form';
        document.getElementById('nlIntroDesc').textContent = 'Update the settings for this submission form. Changes take effect immediately for new submissions.';
        document.getElementById('nlSaveBtn').textContent = 'Save Changes';
        document.getElementById('nlName').value = editData.name || '';
        document.getElementById('nlBucket').value = editData.bucket || '';
        checkNlBucketArchived();
        document.getElementById('nlPermission').value = editData.permission || '';
        nlOrigins = [...(editData.allowedOrigins || [])];
        document.getElementById('nlRedirectSuccess').value = editData.redirectSuccess || '';
        document.getElementById('nlRedirectError').value = editData.redirectError || '';
        document.getElementById('nlRedirectFormPost').checked = editData.redirectFormPost !== false;
        document.getElementById('nlRedirectJsEmbed').checked = !!editData.redirectJsEmbed;
        document.getElementById('nlDisableRedirects').checked = !!editData.disableRedirects;
        toggleRedirectFields();
        document.getElementById('nlLanguage').value = editData.language || appSettings.uiLanguage || 'en';
        document.getElementById('nlIsEnabled').checked = editData.isEnabled !== false;
        document.getElementById('nlConsentRequired').checked = editData.consentRequired !== false;
        document.getElementById('nlConsentText').value = editData.consentText || '';
        document.getElementById('nlPrivacyPolicyUrl').value = editData.privacyPolicyUrl || '';
        nlCustomFields = { ...(editData.customFields || {}) };
        renderNlCustomFieldsList();
        if (editData.formConfig) {
          const c = editData.formConfig;
          if (c.title) document.getElementById('nlTitle').value = c.title;
          if (c.description) document.getElementById('nlDescription').value = c.description;
          if (c.buttonText) document.getElementById('nlButtonText').value = c.buttonText;
          if (c.successMessage) document.getElementById('nlSuccessMessage').value = c.successMessage;
          if (c.primaryColor) document.getElementById('nlPrimaryColor').value = c.primaryColor;
          if (c.backgroundColor) document.getElementById('nlBgColor').value = c.backgroundColor;
          if (c.textColor) document.getElementById('nlTextColor').value = c.textColor;
          if (c.borderRadius) document.getElementById('nlBorderRadius').value = c.borderRadius;
        }
      } else {
        document.getElementById('submissionWizardTitle').textContent = 'Create Submission Form';
        document.getElementById('nlIntroTitle').textContent = 'Create Form';
        document.getElementById('nlIntroDesc').textContent = "You're about to create a new submission form. This form can be safely embedded on an external website and will submit permission states directly to Beacon.";
        document.getElementById('nlSaveBtn').textContent = 'Create Form';
      }
      renderNlOrigins();
      submissionWizardShowStep(1);
    }

    function submissionWizardShowStep(step) {
      for (let i = 1; i <= 5; i++) {
        document.getElementById(`nlStep${i}`).style.display = i === step ? '' : 'none';
        const ind = document.getElementById(`nlStep${i}Indicator`);
        ind.style.opacity = i === step ? '1' : (i < step ? '0.6' : '0.4');
        if (i === 5) ind.style.display = step === 5 ? '' : 'none';
      }
    }

    function submissionWizardNext(step) {
      if (step === 2) {
        const name = document.getElementById('nlName').value.trim();
        const bucket = document.getElementById('nlBucket').value.trim();
        const permission = document.getElementById('nlPermission').value.trim();
        if (!name) { notify('warning', 'Missing Field', 'Please enter a form name'); return; }
        if (!bucket) { notify('warning', 'Missing Field', 'Please enter a bucket name'); return; }
        if (!permission) { notify('warning', 'Missing Field', 'Please enter at least one permission'); return; }
      }
      if (step === 3) {
        if (nlOrigins.length === 0) { notify('warning', 'Missing Field', 'Please add at least one allowed origin'); return; }
      }
      submissionWizardShowStep(step);
    }

    function addSubmissionOrigin() {
      const input = document.getElementById('nlNewOrigin');
      const origin = input.value.trim().replace(/\/$/, '');
      if (!origin) return;
      try {
        const url = new URL(origin);
        if (url.protocol !== 'http:' && url.protocol !== 'https:') {
          notify('warning', 'Invalid Origin', 'Origin must use http or https'); return;
        }
        if (url.pathname !== '/' || url.search || url.hash) {
          notify('warning', 'Invalid Origin', 'Origin must not contain a path, query, or fragment'); return;
        }
        const clean = `${url.protocol}//${url.host}`;
        if (nlOrigins.includes(clean)) {
          notify('warning', 'Duplicate', 'This origin has already been added'); return;
        }
        nlOrigins.push(clean);
        input.value = '';
        renderNlOrigins();
      } catch {
        notify('warning', 'Invalid Origin', 'Please enter a valid URL (e.g. https://example.com)');
      }
    }

    function removeSubmissionOrigin(i) {
      nlOrigins.splice(i, 1);
      renderNlOrigins();
    }

    function renderNlOrigins() {
      const container = document.getElementById('nlOriginsList');
      if (nlOrigins.length === 0) {
        container.innerHTML = '<p style="color:hsl(var(--muted-foreground));font-size:0.9rem">No origins added yet.</p>';
        return;
      }
      container.innerHTML = nlOrigins.map((o, i) => `
        <div style="display:flex;align-items:center;gap:0.5rem;padding:0.5rem 0;border-bottom:1px solid hsl(var(--border))">
          <code style="flex:1;font-size:0.85rem">${sanitize(o)}</code>
          <button type="button" class="btn-remove" onclick="removeSubmissionOrigin(${i})" title="Remove">&times;</button>
        </div>
      `).join('');
    }

    function showNlBucketSuggestions() {
      const input = document.getElementById('nlBucket');
      const dropdown = document.getElementById('nlBucketAutocomplete');
      const query = input.value.toLowerCase();
      const matches = buckets.filter(b => b.name.toLowerCase().includes(query));
      if (matches.length === 0 || (matches.length === 1 && matches[0].name.toLowerCase() === query)) {
        dropdown.style.display = 'none';
        return;
      }
      dropdown.innerHTML = matches.map(b =>
        `<div class="autocomplete-item" onclick="document.getElementById('nlBucket').value='${sanitize(b.name)}';document.getElementById('nlBucketAutocomplete').style.display='none';checkNlBucketArchived()">${sanitize(b.name)}</div>`
      ).join('');
      positionDropdown(input, dropdown);
      dropdown.style.display = 'block';
    }

    function checkNlBucketArchived() {
      const bucket = document.getElementById('nlBucket').value.trim().toLowerCase();
      const isArchived = buckets.some(b => b.name === bucket && b.isArchived);
      document.getElementById('nlBucketArchivedWarning').style.display = isArchived ? '' : 'none';
    }

    function showNlPermissionSuggestions() {
      const input = document.getElementById('nlPermission');
      const dropdown = document.getElementById('nlPermissionAutocomplete');
      const raw = input.value;
      const parts = raw.split(',');
      const currentPart = parts[parts.length - 1].trim().toLowerCase();
      const alreadySelected = new Set(parts.slice(0, -1).map(s => s.trim().toLowerCase()));
      const allPerms = new Set();
      buckets.forEach(b => (b.permissions || []).forEach(p => allPerms.add(p)));
      const permList = [...allPerms].sort();
      const matches = permList.filter(p => !alreadySelected.has(p.toLowerCase()) && p.toLowerCase().includes(currentPart));
      if (matches.length === 0 || (matches.length === 1 && matches[0].toLowerCase() === currentPart)) {
        dropdown.style.display = 'none';
        return;
      }
      dropdown.innerHTML = matches.map(p => {
        const prefix = parts.slice(0, -1).map(s => s.trim()).filter(Boolean).join(', ');
        const newVal = prefix ? `${prefix}, ${p}` : p;
        return `<div class="autocomplete-item" onclick="document.getElementById('nlPermission').value='${sanitize(newVal)}';document.getElementById('nlPermissionAutocomplete').style.display='none'">${sanitize(formatPermission(p))} <span style="opacity:0.5;font-size:0.8em">${sanitize(p)}</span></div>`;
      }).join('');
      positionDropdown(input, dropdown);
      dropdown.style.display = 'block';
    }

    async function saveSubmissionForm() {
      const data = {
        name: document.getElementById('nlName').value.trim(),
        bucket: document.getElementById('nlBucket').value.trim(),
        permission: document.getElementById('nlPermission').value.trim(),
        allowedOrigins: nlOrigins,
        redirectSuccess: document.getElementById('nlRedirectSuccess').value.trim() || null,
        redirectError: document.getElementById('nlRedirectError').value.trim() || null,
        redirectFormPost: document.getElementById('nlRedirectFormPost').checked,
        redirectJsEmbed: document.getElementById('nlRedirectJsEmbed').checked,
        disableRedirects: document.getElementById('nlDisableRedirects').checked,
        language: document.getElementById('nlLanguage').value,
        isEnabled: document.getElementById('nlIsEnabled').checked,
        consentRequired: document.getElementById('nlConsentRequired').checked,
        consentText: document.getElementById('nlConsentText').value.trim() || null,
        privacyPolicyUrl: document.getElementById('nlPrivacyPolicyUrl').value.trim() || null,
        customFields: Object.keys(nlCustomFields).length > 0 ? nlCustomFields : null,
        formConfig: {
          title: document.getElementById('nlTitle').value.trim() || null,
          description: document.getElementById('nlDescription').value.trim() || null,
          buttonText: document.getElementById('nlButtonText').value.trim() || null,
          successMessage: document.getElementById('nlSuccessMessage').value.trim() || null,
          primaryColor: document.getElementById('nlPrimaryColor').value,
          backgroundColor: document.getElementById('nlBgColor').value,
          textColor: document.getElementById('nlTextColor').value,
          borderRadius: document.getElementById('nlBorderRadius').value.trim() || '8px'
        }
      };

      let result;
      if (nlEditId) {
        result = await apiRequest(`/api/admin/submissions/${nlEditId}`, {
          method: 'PUT',
          body: data
        });
      } else {
        result = await apiRequest('/api/admin/submissions', {
          method: 'POST',
          body: data
        });
      }

      if (!result.ok) {
        notify('error', 'Save Failed', result.data?.error || 'Failed to save form');
        return;
      }

      const formId = result.data.id;
      nlCurrentFormId = formId;
      const apiBase = typeof PUBLIC_URL !== 'undefined' && PUBLIC_URL ? PUBLIC_URL : API_BASE;

      // Show embed code
      document.getElementById('nlIframeCode').textContent =
        `<iframe src="${apiBase}/api/submission/${formId}/embed" style="border:none;width:100%;max-width:480px;min-height:235px;" loading="lazy" title="Submission form"></iframe>`;
      document.getElementById('nlJsCode').textContent =
        `<div id="beacon-nl-${formId}"></div>\n<script src="${apiBase}/api/submission/${formId}/embed.js"><\/script>`;
      document.getElementById('nlFormCode').textContent = buildFormPostSnippet(apiBase, formId);
      document.getElementById('nlApiCode').textContent = buildApiSnippet(apiBase, formId);

      if (result.data.apiToken) {
        document.getElementById('nlApiTokenDisplay').style.display = '';
        document.getElementById('nlApiToken').textContent = result.data.apiToken;
      }

      submissionWizardShowStep(5);

      // Warn if the form's bucket is archived
      const savedBucket = data.bucket;
      if (buckets.some(b => b.name === savedBucket && b.isArchived)) {
        notify('warning', 'Bucket Archived', `Bucket "${savedBucket}" is archived. Submissions will be rejected until it's unarchived.`);
      }
    }

    async function editSubmissionForm(id, pushState = true) {
      const result = await apiRequest(`/api/admin/submissions/${id}`);
      if (!result.ok) return;
      showView('submission-edit', false);
      if (pushState) updateUrl({ view: 'submission-edit', id });
      initSubmissionWizard(result.data);
    }

    async function showEmbedCode(id) {
      nlEditId = id;
      nlCurrentFormId = id;
      showView('submission-edit', false);
      updateUrl({ view: 'submission-embed', id });

      // Set title immediately so it never flashes "Edit" or "Create"
      document.getElementById('submissionWizardTitle').textContent = 'View & Share';
      document.getElementById('nlIntroTitle').textContent = 'Share Form';
      document.getElementById('nlIntroDesc').textContent = 'Use the embed code snippets below to add this submission form to your website, or share the direct link to the form itself.';
      document.getElementById('nlApiTokenDisplay').style.display = 'none';
      submissionWizardShowStep(5);

      const apiBase = typeof PUBLIC_URL !== 'undefined' && PUBLIC_URL ? PUBLIC_URL : API_BASE;

      // Fetch form data so redirect fields are available for the snippet
      const result = await apiRequest(`/api/admin/submissions/${id}`);
      if (result.ok) {
        document.getElementById('nlRedirectSuccess').value = result.data.redirectSuccess || '';
        document.getElementById('nlRedirectError').value = result.data.redirectError || '';
        document.getElementById('nlRedirectFormPost').checked = result.data.redirectFormPost !== false;
        document.getElementById('nlRedirectJsEmbed').checked = !!result.data.redirectJsEmbed;
        document.getElementById('nlDisableRedirects').checked = !!result.data.disableRedirects;
        toggleRedirectFields();
        document.getElementById('nlConsentRequired').checked = result.data.consentRequired !== false;
        document.getElementById('nlConsentText').value = result.data.consentText || '';
        document.getElementById('nlPrivacyPolicyUrl').value = result.data.privacyPolicyUrl || '';
        nlCustomFields = { ...(result.data.customFields || {}) };
      }

      document.getElementById('nlIframeCode').textContent =
        `<iframe src="${apiBase}/api/submission/${id}/embed" style="border:none;width:100%;max-width:480px;min-height:235px;" loading="lazy" title="Submission form"></iframe>`;
      document.getElementById('nlJsCode').textContent =
        `<div id="beacon-nl-${id}"></div>\n<script src="${apiBase}/api/submission/${id}/embed.js"><\/script>`;
      document.getElementById('nlFormCode').textContent = buildFormPostSnippet(apiBase, id);
      document.getElementById('nlApiCode').textContent = buildApiSnippet(apiBase, id);
    }

    let submissionToRemove = null;
    let submissionPassphrase = '';

    function deleteSubmissionForm(id, name) {
      submissionToRemove = { id, name };
      const lexicon = [
        'PHOTON', 'CHIRP', 'JITTER', 'PARITY', 'LUMEN',
        'GOSSIP', 'LATENCY', 'QUORUM', 'UPSTREAM', 'PACKET',
        'PULSAR', 'QUASAR', 'ENTROPY', 'NONCE', 'CIPHER',
        'MANTISSA', 'MODULO', 'KERNEL', 'SOCKET', 'BINARY',
        'REEF', 'PORT', 'DOCK', 'VOYAGE', 'CROWNEST',
        'AHOY', 'BILGE', 'SCALLYWAG', 'CUTLASS', 'STARBOARD',
        'PORT', 'KEELHAUL', 'LANDLUBBER', 'SEADOG', 'YOHOHO',
        'BRIG', 'CAPSTAN', 'GALLEON', 'JOLLYROGER', 'MAROONED',
        'PLUNDER', 'RIGGING', 'SWASHBUCKLE', 'ANCHOR', 'DEADRECKON'
      ];
      const code = [];
      for (let i = 0; i < 3; i++) {
        code.push(lexicon[Math.floor(Math.random() * lexicon.length)]);
      }
      submissionPassphrase = code.join(' ');
      document.getElementById('submissionPassphraseDisplay').textContent = submissionPassphrase;
      document.getElementById('submissionPassphraseInput').value = '';
      document.getElementById('confirmSubmissionRemoveBtn').classList.remove('active');
      document.getElementById('submissionRemoveModal').style.display = 'flex';
      closeAllMenus();
    }

    function verifySubmissionPassphrase() {
      const input = document.getElementById('submissionPassphraseInput').value.toUpperCase().trim();
      const btn = document.getElementById('confirmSubmissionRemoveBtn');
      if (input === submissionPassphrase) {
        btn.classList.add('active');
      } else {
        btn.classList.remove('active');
      }
    }

    async function confirmSubmissionRemoval() {
      if (!submissionToRemove) return;
      const input = document.getElementById('submissionPassphraseInput').value.toUpperCase().trim();
      if (input !== submissionPassphrase) return;
      const result = await apiRequest(`/api/admin/submissions/${submissionToRemove.id}`, { method: 'DELETE' });
      if (result.ok) {
        notify('success', 'Form Removed', `Successfully deleted submission form "${submissionToRemove.name}"`);
        closeSubmissionRemoveModal();
        loadSubmissionForms();
      } else { notify('error', 'Delete Failed', result.data?.error || 'Failed to delete submission form.'); }
    }

    function closeSubmissionRemoveModal() {
      document.getElementById('submissionRemoveModal').style.display = 'none';
      submissionPassphrase = '';
      submissionToRemove = null;
    }

    async function toggleSubmissionEnabled(id, enabled) {
      const result = await apiRequest(`/api/admin/submissions/${id}`, {
        method: 'PUT',
        body: { isEnabled: enabled }
      });
      if (!result.ok) { notify('error', 'Update Failed', result.data?.error || 'Failed to update submission form.'); loadSubmissionForms(); }
    }

    function switchEmbedTab(tab) {
      document.querySelectorAll('#embedTabs .embed-tab').forEach(b => b.classList.remove('active'));
      document.querySelectorAll('.embed-tab-panel').forEach(p => p.classList.remove('active'));
      document.querySelector(`#embedTabs .embed-tab[onclick*="'${tab}'"]`).classList.add('active');
      document.getElementById(`embedPanel-${tab}`).classList.add('active');
    }

    function copyEmbedCode(type) {
      const ids = { iframe: 'nlIframeCode', js: 'nlJsCode', form: 'nlFormCode', api: 'nlApiCode' };
      const el = document.getElementById(ids[type] || ids.iframe);
      navigator.clipboard.writeText(el.textContent);
      notify('success', 'Copied', 'Embed code copied to clipboard');
    }

    function buildFormPostSnippet(apiBase, formId) {
      const rs = document.getElementById('nlRedirectSuccess').value.trim();
      const re = document.getElementById('nlRedirectError').value.trim();
      const consentRequired = document.getElementById('nlConsentRequired').checked;
      const consentText = document.getElementById('nlConsentText').value.trim() || 'I agree to receive emails and understand I can unsubscribe at any time.';
      const privacyUrl = document.getElementById('nlPrivacyPolicyUrl').value.trim();
      const disableRedirects = document.getElementById('nlDisableRedirects').checked;
      const hiddenFields = (disableRedirects || rs || re) ? '' :
        `\n  <input type="hidden" name="redirect_success" value="https://yoursite.com/thank-you" />` +
        `\n  <input type="hidden" name="redirect_error" value="https://yoursite.com/error" />`;
      const consentField = consentRequired ?
        `\n  <label style="display:flex;align-items:flex-start;gap:8px;margin-top:10px;font-size:0.85rem;line-height:1.4">` +
        `\n    <input type="checkbox" name="consent" value="true" required style="margin-top:2px;flex-shrink:0" />` +
        `\n    <span>${consentText}</span>` +
        `\n  </label>` : '';
      const privacyField = privacyUrl ?
        `\n  <p style="margin-top:6px;font-size:0.8rem;opacity:0.65"><a href="${privacyUrl}" target="_blank" rel="noopener noreferrer">Privacy Policy</a></p>` : '';
      return `<form method="POST" action="${apiBase}/api/submission/${formId}/subscribe">
  <input type="email" name="email" placeholder="you@example.com" required />${hiddenFields}${consentField}${privacyField}
  <button type="submit">Subscribe</button>
</form>`;
    }

    function buildApiSnippet(apiBase, formId) {
      const consentRequired = document.getElementById('nlConsentRequired').checked;
      const url = `${apiBase}/api/submission/${formId}/subscribe`;
      const bodyFields = [`  email: 'user@example.com'`];
      if (consentRequired) bodyFields.push(`  consent: 'true'`);
      const bodyStr = bodyFields.join(',\n');
      return `// POST ${url}
// Content-Type: application/json
//
// Request body:
// {
${bodyFields.map(f => '//   ' + f.trim()).join('\n')}
// }
//
// Success (200): { "message": "Thanks for subscribing!" }
// Error (400):   { "error": "..." }${consentRequired ? '\n// Error (400):   { "error": "Consent is required to subscribe" }' : ''}

fetch('${url}', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({
${bodyStr}
  })
})
.then(r => r.json().then(d => ({ ok: r.ok, data: d })))
.then(({ ok, data }) => {
  if (ok) {
    console.log(data.message);
  } else {
    console.error(data.error);
  }
})
.catch(() => console.error('Network error'));`;
    }

    function copyNlApiToken() {
      navigator.clipboard.writeText(document.getElementById('nlApiToken').textContent);
      notify('success', 'Copied', 'API token copied to clipboard');
    }

    function downloadNlApiToken() {
      const token = document.getElementById('nlApiToken').textContent;
      if (!token) return;
      const d = new Date();
      const stamp = `${d.getFullYear()}${String(d.getMonth()+1).padStart(2,'0')}${String(d.getDate()).padStart(2,'0')}`;
      const blob = new Blob([token], { type: 'text/plain' });
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = `token_${stamp}.txt`;
      a.click();
      URL.revokeObjectURL(a.href);
      notify('success', 'Downloaded', `Saved as token_${stamp}.txt`);
    }

    var previewFormId = null;

    function testSubmissionForm(mode) {
      if (!nlCurrentFormId) return;
      previewFormId = nlCurrentFormId;
      showPreview(nlCurrentFormId, mode);
    }

    function showPreview(id, mode) {
      previewFormId = id;
      const apiBase = typeof PUBLIC_URL !== 'undefined' && PUBLIC_URL ? PUBLIC_URL : API_BASE;
      const badge = document.getElementById('previewBadge');
      const container = document.getElementById('previewContent');
      const labels = { iframe: 'iframe', js: 'JavaScript', form: 'HTML Form', api: 'API' };
      badge.textContent = labels[mode] || mode;
      container.innerHTML = '';

      if (mode === 'iframe') {
        const iframe = document.createElement('iframe');
        iframe.src = `${apiBase}/api/submission/${id}/embed`;
        iframe.style.cssText = 'border:none;width:100%;max-width:480px;min-height:235px;border-radius:var(--radius);box-shadow:0 1px 3px rgba(0,0,0,.08)';
        iframe.onload = function() {
          try { const h = iframe.contentDocument.documentElement.scrollHeight; if (h) iframe.style.height = h + 'px'; }
          catch(e) { /* cross-origin, keep min-height */ }
        };
        container.appendChild(iframe);
      } else if (mode === 'js') {
        const wrapper = document.createElement('div');
        wrapper.id = `beacon-nl-${id}`;
        const card = buildPreviewCard(wrapper);
        container.appendChild(card);
        const script = document.createElement('script');
        script.src = `${apiBase}/api/submission/${id}/embed.js`;
        card.appendChild(script);
      } else if (mode === 'form') {
        const actionUrl = `${apiBase}/api/submission/${id}/subscribe`;
        const consentRequired = document.getElementById('nlConsentRequired')?.checked;
        const consentText = document.getElementById('nlConsentText')?.value.trim() || 'I agree to receive emails and understand I can unsubscribe at any time.';
        const privacyUrl = document.getElementById('nlPrivacyPolicyUrl')?.value.trim();
        const disableRedirects = document.getElementById('nlDisableRedirects')?.checked;

        const form = document.createElement('form');
        form.method = 'POST';
        form.action = actionUrl;
        if (!disableRedirects) form.target = '_self';
        form.innerHTML = `
          <input type="email" name="email" placeholder="you@example.com" required style="width:100%;padding:0.5rem 0.75rem;border:1px solid hsl(var(--border));border-radius:var(--radius);font-size:0.9rem;margin-bottom:0.75rem;background:hsl(var(--background));color:hsl(var(--foreground))">
          ${!disableRedirects ? '<input type="hidden" name="redirect_success" value="about:blank#success"><input type="hidden" name="redirect_error" value="about:blank#error">' : ''}
          ${consentRequired ? `<label style="display:flex;align-items:flex-start;gap:8px;margin:0 0 0.75rem;font-size:0.85rem;line-height:1.4;color:hsl(var(--foreground))"><input type="checkbox" name="consent" value="true" required style="margin-top:2px;flex-shrink:0"><span>${consentText}</span></label>` : ''}
          ${privacyUrl ? `<p style="margin-bottom:0.75rem;font-size:0.8rem;opacity:0.65"><a href="${privacyUrl}" target="_self" rel="noopener noreferrer" style="color:hsl(var(--foreground))">Privacy Policy</a></p>` : ''}
          <button type="submit" class="btn btn-primary" style="width:100%;justify-content:center">Subscribe</button>
        `;
        if (disableRedirects) {
          const msg = document.createElement('div');
          msg.style.cssText = 'margin-top:0.75rem;font-size:0.9rem;min-height:1.4em';
          form.addEventListener('submit', async (e) => {
            e.preventDefault();
            const btn = form.querySelector('button');
            btn.disabled = true;
            msg.textContent = ''; msg.style.color = '';
            try {
              const body = { email: form.querySelector('input[name="email"]').value };
              const cb = form.querySelector('input[name="consent"]');
              if (cb) body.consent = cb.checked ? 'true' : 'false';
              const res = await fetch(actionUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                body: JSON.stringify(body)
              });
              const data = await res.json();
              if (res.ok) {
                msg.textContent = data.message || 'Success';
                msg.style.color = '#16a34a';
                form.reset();
              } else {
                msg.textContent = data.error || 'Something went wrong.';
                msg.style.color = '#dc2626';
              }
            } catch {
              msg.textContent = 'Network error. Please try again.';
              msg.style.color = '#dc2626';
            }
            btn.disabled = false;
          });
          form.appendChild(msg);
        }
        const card = buildPreviewCard(form);
        container.appendChild(card);
      } else if (mode === 'api') {
        const subscribeUrl = `${apiBase}/api/submission/${id}/subscribe`;
        const pre = document.createElement('pre');
        pre.style.cssText = 'background:hsl(var(--muted));color:hsl(var(--foreground));padding:1rem;border-radius:var(--radius);font-size:0.8rem;overflow-x:auto;line-height:1.6;margin:0;white-space:pre-wrap;word-break:break-all';
        pre.textContent = `fetch('${subscribeUrl}', {\n  method: 'POST',\n  headers: { 'Content-Type': 'application/json' },\n  body: JSON.stringify({ email: 'user@example.com' })\n})\n.then(r => r.json())\n.then(data => console.log(data));`;
        const card = buildPreviewCard(pre);
        container.appendChild(card);
      }

      showView('submission-preview', false);
      updateUrl({ view: 'submission-preview', id, mode });
    }

    function buildPreviewCard(content) {
      const card = document.createElement('div');
      card.style.cssText = 'background:hsl(var(--background));border:1px solid hsl(var(--border));border-radius:var(--radius);padding:1.5rem;width:100%;max-width:520px;box-shadow:0 1px 3px rgba(0,0,0,.08)';
      card.appendChild(content);
      return card;
    }

