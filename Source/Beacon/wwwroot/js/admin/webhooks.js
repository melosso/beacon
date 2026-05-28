    // WEBHOOK CONFIGURATION
    const WEBHOOK_DEFAULT_BODY = JSON.stringify({
      bucket: '{{bucket}}',
      emailHash: '{{emailHash}}',
      permissions: '{{permissions}}',
      timestamp: '{{timestamp}}'
    }, null, 2).replace('"{{permissions}}"', '{{permissions}}');

    let webhookHeaders = {};

    function insertWebhookVar(name) {
      const ta = document.getElementById('webhookBody');
      const start = ta.selectionStart;
      const end = ta.selectionEnd;
      const text = ta.value;
      const varText = `{{${name}}}`;
      ta.value = text.substring(0, start) + varText + text.substring(end);
      ta.selectionStart = ta.selectionEnd = start + varText.length;
      ta.focus();
    }

    function resetWebhookBodyTemplate() {
      document.getElementById('webhookBody').value = WEBHOOK_DEFAULT_BODY;
      checkWebhookEmailVar(WEBHOOK_DEFAULT_BODY);
    }

    function checkWebhookEmailVar(body) {
      const warning = document.getElementById('webhookEmailWarning');
      if (warning) warning.style.display = /\{\{\s*email\s*\}\}/i.test(body) ? '' : 'none';
    }

    function toggleWebhookTemplateMenu(trigger) {
      const menu = document.getElementById('webhookTemplateMenu');
      const isOpen = menu.classList.contains('open');
      closeAllMenus();
      if (!isOpen) {
        menu.classList.add('open');
        const rect = trigger.getBoundingClientRect();
        menu.style.top = `${rect.bottom + 4}px`;
        menu.style.left = `${rect.right - menu.offsetWidth}px`;
        openMenuId = 'webhookTemplateMenu';
      }
    }

    function closeWebhookTemplateMenu() {
      document.getElementById('webhookTemplateMenu')?.classList.remove('open');
      if (openMenuId === 'webhookTemplateMenu') openMenuId = null;
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
      header.classList.toggle('expanded');
      const body = header.nextElementSibling;
      body.classList.toggle('open');
    }

    function setWebhookBadge(configured) {
      const badge = document.getElementById('webhookStatusBadge');
      if (configured) {
        badge.textContent = 'Active';
        badge.classList.add('active');
      } else {
        badge.textContent = 'Not configured';
        badge.classList.remove('active');
      }
    }

    async function showOptionsModal(pushState = true) {
      if (!currentBucket) return;

      // Set bucket ID in basic section
      document.getElementById('optionsBucketId').value = currentBucket;

      // Update archive button state
      const archiveBtn = document.getElementById('optionsArchiveBtn');
      archiveBtn.title = currentBucketArchived ? 'Unarchive Bucket' : 'Archive Bucket';
      archiveBtn.style.color = currentBucketArchived ? 'hsl(var(--primary))' : 'hsl(var(--muted-foreground))';

      // Load bucket permissions
      loadBucketPerms();

      // Reset webhook fields
      webhookHeaders = {};
      document.getElementById('webhookUrl').value = '';
      document.getElementById('webhookMethod').value = 'POST';
      document.getElementById('webhookBody').value = WEBHOOK_DEFAULT_BODY;
      document.getElementById('webhookHeadersGrid').innerHTML = '';
      document.getElementById('deleteWebhookBtn').style.display = 'none';
      document.getElementById('webhookSecretSection').style.display = 'none';
      document.getElementById('webhookSecretValue').value = '';
      setWebhookBadge(false);

      // Expand basic section, collapse webhook by default
      const sections = document.querySelectorAll('#optionsModal .options-section-header');
      sections.forEach((h, i) => {
        const body = h.nextElementSibling;
        if (i === 0) {
          h.classList.add('expanded');
          body.classList.add('open');
        } else {
          h.classList.remove('expanded');
          body.classList.remove('open');
        }
      });

      // Load bucket options (email settings)
      loadBucketOptions();

      // Load existing webhook config if exists
      try {
        const result = await apiRequest(`/api/admin/buckets/${encodeURIComponent(currentBucket)}/webhook`);
        if (result.ok && result.data?.configured) {
          const config = result.data;
          document.getElementById('webhookUrl').value = config.url || '';
          document.getElementById('webhookMethod').value = config.method || 'POST';
          const webhookBodyVal = formatBodyTemplate(config.bodyTemplate) || WEBHOOK_DEFAULT_BODY;
          document.getElementById('webhookBody').value = webhookBodyVal;
          checkWebhookEmailVar(webhookBodyVal);
          webhookHeaders = config.headers || {};
          renderWebhookHeaders();
          document.getElementById('deleteWebhookBtn').style.display = 'block';
          setWebhookBadge(true);

          // Auto-expand webhook section if configured
          sections[1]?.classList.add('expanded');
          sections[1]?.nextElementSibling?.classList.add('open');
        }
      } catch (err) {
        // No webhook configured
      }

      // Populate brand identity section
      const biContent = document.getElementById('optionsBrandIdentityContent');
      const biBadge = document.getElementById('optionsBrandBadge');
      const identity = getBrandIdentityForBucket(currentBucket);
      if (biContent) {
        if (identity) {
          const accent = identity.settings?.primaryAccent || null;
          const swatchHtml = accent
            ? `<span style="display:inline-block;width:0.875rem;height:0.875rem;border-radius:50%;background:${escHtml(accent)};border:1px solid hsl(var(--border));vertical-align:middle;margin-right:0.375rem;flex-shrink:0"></span>`
            : '';
          biContent.innerHTML = `<div style="display:flex;align-items:center;padding:0.625rem 0.75rem;background:hsl(var(--muted));border-radius:var(--radius);font-size:0.875rem">${swatchHtml}<span>${escHtml(identity.name)}</span></div>`;
          if (biBadge) { biBadge.textContent = identity.name; biBadge.style.display = ''; }
        } else {
          biContent.innerHTML = `<p style="font-size:0.875rem;color:hsl(var(--muted-foreground));margin:0">Using default identity.</p>`;
          if (biBadge) biBadge.style.display = 'none';
        }
      }

      document.getElementById('optionsModal').style.display = 'flex';

      // Update URL to include modal state
      if (pushState) {
        const url = new URL(window.location);
        url.searchParams.set('modal', 'options');
        history.pushState(null, '', url);
      }
    }

    function closeOptionsModal() {
      document.getElementById('optionsModal').style.display = 'none';
      webhookHeaders = {};
      bucketPermsData = [];
      document.getElementById('newBucketPermInput').value = '';

      // Remove modal param from URL
      const url = new URL(window.location);
      url.searchParams.delete('modal');
      history.replaceState(null, '', url);
    }

    async function loadBucketOptions() {
      if (!currentBucket) return;
      const toggle = document.getElementById('bucketDoubleOptIn');
      const label = document.getElementById('bucketDoubleOptInLabel');
      const desc = document.getElementById('bucketDoubleOptInDesc');
      const emailDisabled = typeof DISABLE_EMAIL_NOTIFICATIONS !== 'undefined' && DISABLE_EMAIL_NOTIFICATIONS;
      const globalEnabled = !emailDisabled && appSettings.enableDoubleOptIn;

      // Default state: inherit global (true), grayed out when global feature is off or email is disabled
      if (toggle) toggle.checked = true;
      if (toggle) toggle.disabled = !globalEnabled;
      if (label) label.style.opacity = globalEnabled ? '1' : '0.5';
      if (desc) desc.textContent = globalEnabled
        ? 'Subscribers must confirm via email. Disable to opt this bucket out of the global setting.'
        : emailDisabled
          ? 'Email notifications are disabled by your administrator.'
          : 'Enable the global double opt-in setting first to configure this option.';

      if (!globalEnabled) return;

      try {
        const result = await apiRequest(`/api/admin/buckets/${encodeURIComponent(currentBucket)}/options`);
        if (result.ok && result.data) {
          if (toggle) toggle.checked = result.data.doubleOptIn ?? true;
          const utmEl = document.getElementById('bucketUtmCampaign');
          if (utmEl) utmEl.value = result.data.utmCampaign ?? '';
        }
      } catch {}
    }

    async function saveBucketUtmCampaign() {
      if (!currentBucket) return;
      const utmEl = document.getElementById('bucketUtmCampaign');
      const utmCampaign = utmEl?.value.trim() || null;
      const doubleOptInToggle = document.getElementById('bucketDoubleOptIn');
      await apiRequest(`/api/admin/buckets/${encodeURIComponent(currentBucket)}/options`, {
        method: 'PUT',
        body: { doubleOptIn: doubleOptInToggle?.checked ?? true, utmCampaign }
      });
    }

    async function saveBucketDoubleOptIn(value) {
      if (!currentBucket) return;
      const utmEl = document.getElementById('bucketUtmCampaign');
      const utmCampaign = utmEl?.value.trim() || null;
      const res = await apiRequest(`/api/admin/buckets/${encodeURIComponent(currentBucket)}/options`, {
        method: 'PUT',
        body: { doubleOptIn: value, utmCampaign }
      });
      if (res.ok) {
        notify('success', 'Saved', 'Bucket email settings updated.');
      } else {
        notify('error', 'Save Failed', 'Failed to save bucket email settings.');
        const toggle = document.getElementById('bucketDoubleOptIn');
        if (toggle) toggle.checked = !value;
      }
    }

    function addWebhookHeader() {
      const keyInput = document.getElementById('newHeaderKey');
      const valueInput = document.getElementById('newHeaderValue');
      const key = keyInput.value.trim();
      const value = valueInput.value.trim();

      if (!key || !value) return;

      webhookHeaders[key] = value;
      renderWebhookHeaders();
      
      keyInput.value = '';
      valueInput.value = '';
    }

    function removeWebhookHeader(key) {
      delete webhookHeaders[key];
      renderWebhookHeaders();
    }

    function renderWebhookHeaders() {
      const grid = document.getElementById('webhookHeadersGrid');
      const entries = Object.entries(webhookHeaders);
      
      if (entries.length === 0) {
        grid.innerHTML = '<div style="color:hsl(var(--muted-foreground));font-size:0.875rem">No headers configured</div>';
        return;
      }

      grid.innerHTML = entries.map(([key, value]) => `
        <div class="custom-field-row">
          <div class="custom-field-key">${sanitize(key)}</div>
          <div class="custom-field-value">${sanitize(value)}</div>
          <button type="button" class="btn-icon-small" onclick="removeWebhookHeader('${sanitize(key)}')" title="Remove">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="6" x2="6" y2="18"></line>
              <line x1="6" y1="6" x2="18" y2="18"></line>
            </svg>
          </button>
        </div>
      `).join('');
    }

    async function saveWebhook() {
      const url = document.getElementById('webhookUrl').value.trim();
      const method = document.getElementById('webhookMethod').value;
      const bodyTemplate = document.getElementById('webhookBody').value.trim();

      if (!url) {
        notify('error', 'Validation Error', 'Webhook URL is required');
        return;
      }

      const payload = {
        url,
        method,
        headers: Object.keys(webhookHeaders).length > 0 ? webhookHeaders : null,
        bodyTemplate: bodyTemplate || null
      };

      try {
        const result = await apiRequest(`/api/admin/buckets/${encodeURIComponent(currentBucket)}/webhook`, {
          method: 'POST',
          body: payload
        });

        if (result.ok) {
          if (result.data?.signingSecret) {
            document.getElementById('webhookSecretValue').value = result.data.signingSecret;
            document.getElementById('webhookSecretSection').style.display = 'block';
            document.getElementById('deleteWebhookBtn').style.display = 'block';
            setWebhookBadge(true);
            notify('success', 'Webhook Saved', 'Configuration saved. Copy the signing secret now. It won\'t be shown again.');
          } else {
            setWebhookBadge(true);
            notify('success', 'Webhook Saved', 'Webhook configuration updated successfully');
            closeOptionsModal();
          }
        } else {
          notify('error', 'Save Failed', result.data?.error || 'Failed to save webhook configuration');
        }
      } catch (err) {
        notify('error', 'Save Failed', 'An error occurred while saving webhook configuration');
      }
    }

    async function deleteWebhook() {
      if (!confirm('Delete webhook configuration? This cannot be undone.')) {
        return;
      }

      try {
        const result = await apiRequest(`/api/admin/buckets/${encodeURIComponent(currentBucket)}/webhook`, {
          method: 'DELETE'
        });

        if (result.ok) {
          notify('success', 'Webhook Deleted', 'Webhook configuration removed successfully');
          setWebhookBadge(false);
          closeOptionsModal();
        } else {
          notify('error', 'Delete Failed', 'Failed to delete webhook configuration');
        }
      } catch (err) {
        notify('error', 'Delete Failed', 'An error occurred while deleting webhook configuration');
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
        document.getElementById(openMenuId)?.classList.remove('open');
      }

      menu.classList.toggle('open');
      openMenuId = menu.classList.contains('open') ? menuId : null;

      if (menu.classList.contains('open') && trigger) {
        const rect = trigger.getBoundingClientRect();
        menu.style.top = `${rect.bottom + 4}px`;
        menu.style.left = `${rect.right - menu.offsetWidth}px`;
      }
    }

    function positionDropdown(input, dropdown) {
      const rect = input.getBoundingClientRect();
      dropdown.style.top = rect.bottom + 'px';
      dropdown.style.left = rect.left + 'px';
      dropdown.style.width = rect.width + 'px';
    }

    function closeAllMenus() {
      if (openMenuId) {
        document.getElementById(openMenuId)?.classList.remove('open');
        openMenuId = null;
      }
      document.getElementById('webhookErrorsDropdown')?.classList.remove('open');
      document.getElementById('overviewWebhookErrorsDropdown')?.classList.remove('open');
      document.querySelectorAll('.autocomplete-dropdown').forEach(d => { d.style.display = ''; d.classList.remove('open'); });
    }

    document.addEventListener('click', function(e) {
      if (e.target.closest('.autocomplete-wrapper')) return;
      closeAllMenus();
    });

    // WEBHOOK ERRORS
    let webhookErrorsCache = [];
    let overviewErrorsCache = []; // flat: [{bucket, ...errorFields}]
    let activeErrorDetail = null;

    async function loadWebhookErrors(bucket) {
      const trigger = document.getElementById('webhookErrorsTrigger');
      const dropdown = document.getElementById('webhookErrorsDropdown');

      trigger.classList.remove('has-errors');

      const errorsResult = await apiRequest(`/api/admin/buckets/${encodeURIComponent(bucket)}/webhook/errors`);
      const errors = errorsResult.ok && errorsResult.data ? errorsResult.data : [];
      webhookErrorsCache = errors;

      if (errors.length === 0) {
        trigger.classList.remove('has-errors');
        dropdown.innerHTML = `<div class="error-header" style="padding-bottom:0.8rem;">Notifications</div><div class="error-item" style="text-align:center;color:hsl(var(--muted-foreground));padding:1rem 0.875rem;cursor:default">Nothing here to report!</div>`;
        return;
      }

      trigger.classList.add('has-errors');
      dropdown.innerHTML = `<div class="error-header"><span>Recent Errors</span><button class="clear-all-btn" onclick="event.stopPropagation();clearAllWebhookErrors()">Clear All</button></div>` +
        errors.map((e, i) => {
          const time = formatDate(e.occurredAt);
          const msg = e.errorMessage.length > 120 ? e.errorMessage.substring(0, 120) + '...' : e.errorMessage;
          const badge = e.statusCode ? `<span class="error-status">${sanitize(String(e.statusCode))}</span>` : '';
          return `<div class="error-item" onclick="event.stopPropagation();showErrorDetailModal(${i})"><div class="error-time">${sanitize(time)}</div><div class="error-msg">${sanitize(msg)}${badge}</div></div>`;
        }).join('');
    }

    function showErrorDetailModal(index) {
      const error = webhookErrorsCache[index];
      if (!error) return;
      openErrorDetail(error);
    }

    function openOverviewErrorDetail(index) {
      const error = overviewErrorsCache[index];
      if (!error) return;
      currentBucket = error.bucket;
      showBucket(error.bucket);
      openErrorDetail(error);
      // Reflect in URL so the modal is directly linkable
      const url = new URL(window.location);
      url.searchParams.set('modal', 'error-detail');
      url.searchParams.set('errorId', error.id);
      history.replaceState({}, '', url);
    }

    function openErrorDetail(error) {
      activeErrorDetail = error;
      document.getElementById('errorDetailTime').textContent = new Date(error.occurredAt).toLocaleString();
      const badgeEl = document.getElementById('errorDetailBadge');
      badgeEl.innerHTML = error.statusCode ? `<span class="error-status">${sanitize(String(error.statusCode))}</span>` : '';

      // Request info
      const reqEl = document.getElementById('errorDetailRequest');
      const parts = [];
      if (error.requestMethod && error.requestUrl) {
        parts.push(`${sanitize(error.requestMethod)} ${sanitize(error.requestUrl)}`);
      }
      if (error.attemptCount > 0) {
        parts.push(`${error.attemptCount} attempt${error.attemptCount !== 1 ? 's' : ''}`);
      }
      reqEl.innerHTML = parts.length ? parts.join(' &middot; ') : '';

      document.getElementById('errorDetailMessage').textContent = error.errorMessage;

      // Stack trace
      const stackWrap = document.getElementById('errorDetailStackWrap');
      if (error.stackTrace) {
        document.getElementById('errorDetailStack').textContent = error.stackTrace;
        stackWrap.style.display = 'block';
      } else {
        stackWrap.style.display = 'none';
      }

      closeAllMenus();
      document.getElementById('errorDetailModal').style.display = 'flex';
    }

    function closeErrorDetailModal() {
      document.getElementById('errorDetailModal').style.display = 'none';
      activeErrorDetail = null;
      const url = new URL(window.location);
      if (url.searchParams.get('modal') === 'error-detail') {
        url.searchParams.delete('modal');
        url.searchParams.delete('errorId');
        history.replaceState({}, '', url);
      }
    }

    function copyErrorMessage() {
      if (!activeErrorDetail) return;
      const e = activeErrorDetail;
      let text = e.errorMessage;
      const meta = [];
      if (e.requestMethod && e.requestUrl) meta.push(`Request: ${e.requestMethod} ${e.requestUrl}`);
      if (e.statusCode) meta.push(`Status: ${e.statusCode}`);
      if (e.attemptCount > 0) meta.push(`Attempts: ${e.attemptCount}`);
      meta.push(`Time: ${new Date(e.occurredAt).toLocaleString()}`);
      if (meta.length) text = meta.join('\n') + '\n\n' + text;
      if (e.stackTrace) text += '\n\nStack Trace:\n' + e.stackTrace;
      navigator.clipboard.writeText(text).then(() => {
        notify('success', 'Copied to clipboard');
      });
    }

    async function removeWebhookError() {
      if (!activeErrorDetail) return;
      const result = await apiRequest(`/api/admin/buckets/${encodeURIComponent(currentBucket)}/webhook/errors/${activeErrorDetail.id}`, { method: 'DELETE' });
      if (result.ok) {
        closeErrorDetailModal();
        await loadWebhookErrors(currentBucket);
      } else { notify('error', 'Remove Failed', result.data?.error || 'Failed to remove error.'); }
    }

    async function clearAllWebhookErrors() {
      const result = await apiRequest(`/api/admin/buckets/${encodeURIComponent(currentBucket)}/webhook/errors`, { method: 'DELETE' });
      if (result.ok) {
        await loadWebhookErrors(currentBucket);
      } else { notify('error', 'Clear Failed', result.data?.error || 'Failed to clear errors.'); }
    }

    function toggleWebhookErrors(event) {
      event.stopPropagation();
      const dropdown = document.getElementById('webhookErrorsDropdown');
      const trigger = document.getElementById('webhookErrorsTrigger');
      const isOpen = dropdown.classList.contains('open');

      closeAllMenus();

      if (!isOpen) {
        dropdown.classList.add('open');
        const rect = trigger.getBoundingClientRect();
        dropdown.style.top = `${rect.bottom + 4}px`;
        dropdown.style.left = `${Math.max(0, rect.right - dropdown.offsetWidth)}px`;
      }
    }

    function toggleOverviewWebhookErrors(event) {
      event.stopPropagation();
      const dropdown = document.getElementById('overviewWebhookErrorsDropdown');
      const trigger = document.getElementById('overviewWebhookErrorsTrigger');
      const isOpen = dropdown.classList.contains('open');

      closeAllMenus();

      if (!isOpen) {
        renderOverviewErrorsDropdown();
        dropdown.classList.add('open');
        const rect = trigger.getBoundingClientRect();
        dropdown.style.top = `${rect.bottom + 4}px`;
        dropdown.style.left = `${Math.max(0, rect.right - dropdown.offsetWidth)}px`;
      }
    }

    function renderOverviewErrorsDropdown() {
      const dropdown = document.getElementById('overviewWebhookErrorsDropdown');
      if (overviewErrorsCache.length === 0) {
        dropdown.innerHTML = `<div class="error-header" style="padding-bottom:0.8rem;">Notifications</div><div class="error-item" style="text-align:center;color:hsl(var(--muted-foreground));padding:1rem 0.875rem;cursor:default">Nothing here to report!</div>`;
        return;
      }
      dropdown.innerHTML = `<div class="error-header"><span>Notifications</span></div>` +
        overviewErrorsCache.map((e, i) => {
          const time = formatDate(e.occurredAt);
          const msg = e.errorMessage.length > 100 ? e.errorMessage.substring(0, 100) + '…' : e.errorMessage;
          const badge = e.statusCode ? `<span class="error-status">${sanitize(String(e.statusCode))}</span>` : '';
          return `<div class="error-item" onclick="event.stopPropagation();openOverviewErrorDetail(${i})">
            <div class="error-time"><strong>${sanitize(e.bucket)}</strong> &middot; ${sanitize(time)}</div>
            <div class="error-msg">${sanitize(msg)}${badge}</div>
          </div>`;
        }).join('');
    }

    // EDIT PERMISSIONS MODAL
    let editingRecord = null;
    let editingBucket = '';

    function openEditPermissions(encodedData) {
      editingRecord = JSON.parse(decodeURIComponent(encodedData));
      editingBucket = currentBucket;

      document.getElementById('editPermissionsEmail').textContent = editingRecord.email || `Hash: ${editingRecord.emailHash?.substring(0, 16)}...`;
      document.getElementById('editPermissionsBucket').textContent = editingBucket || currentBucket;
      document.getElementById('editName').value = editingRecord.name || '';

      const grid = document.getElementById('editPermissionsGrid');
      grid.innerHTML = currentBucketPermissions.map(p => {
        const currentState = editingRecord.permissions[p];
        return `
          <div class="permission-row" data-perm="${sanitize(p)}">
            <span class="permission-name">${sanitize(formatPermission(p))}</span>
            <div class="permission-toggle">
              <button type="button" class="opted-in ${currentState === true ? 'active' : ''}" onclick="setEditPermState('${sanitize(p)}', true)">In</button>
              <button type="button" class="opted-out ${currentState === false ? 'active' : ''}" onclick="setEditPermState('${sanitize(p)}', false)">Out</button>
            </div>
          </div>
        `;
      }).join('');

      document.getElementById('editLanguage').value = appSettings.uiLanguage || 'en';

      // Populate custom fields
      editCustomFields = { ...(editingRecord.customFields || {}) };
      renderEditCustomFieldsList();

      document.getElementById('editPermissionsModal').style.display = 'flex';
      closeAllMenus();
    }

    // Called from the subscriptions detail view to open the modal for a different bucket without changing the record being edited
    function openEditFromSubscription(bucket, permissionsEncoded, recordDataEncoded) {
      currentBucketPermissions = JSON.parse(decodeURIComponent(permissionsEncoded));
      openEditPermissions(recordDataEncoded);
      // openEditPermissions sets editingBucket = currentBucket; correct it here
      editingBucket = bucket;
      document.getElementById('editPermissionsBucket').textContent = bucket;
    }

    function setEditPermState(perm, state) {
      const row = document.querySelector(`#editPermissionsGrid .permission-row[data-perm="${perm}"]`);
      if (!row) return;
      row.querySelectorAll('.permission-toggle button').forEach(btn => btn.classList.remove('active'));
      if (state === true) row.querySelector('.opted-in').classList.add('active');
      else row.querySelector('.opted-out').classList.add('active');
    }

    function closeEditPermissionsModal() {
      document.getElementById('editPermissionsModal').style.display = 'none';
      document.getElementById('editName').value = '';
      editingRecord = null;
      editingBucket = '';
    }

    function toggleBucketSubmenu(e) {
      if (e) e.stopPropagation();
      const submenu = document.getElementById('bucketSubmenu');
      const wasOpen = submenu.classList.contains('open');
      closeAllSubmenus();
      if (!wasOpen) submenu.classList.add('open');
    }

    function toggleModulesSubmenu(e) {
      if (e) e.stopPropagation();
      const submenu = document.getElementById('modulesSubmenu');
      const wasOpen = submenu.classList.contains('open');
      closeAllSubmenus();
      if (!wasOpen) submenu.classList.add('open');
    }

    function closeAllSubmenus() {
      document.querySelectorAll('.submenu.open').forEach(el => el.classList.remove('open'));
    }

    document.addEventListener('click', function(e) {
      if (!e.target.closest('.submenu, .btn-plus')) {
        closeAllSubmenus();
      }
    });

    function showPermissionsConfirmModal() {
      if (!editingRecord || !editingRecord.email) {
        notify('error', 'Update Failed', 'Record data is missing, try reloading the page.');
        return;
      }
      document.getElementById('permissionsConfirmModal').style.display = 'flex';
    }

    function closePermissionsConfirmModal() {
      document.getElementById('permissionsConfirmModal').style.display = 'none';
    }

    async function confirmSavePermissions() {
      closePermissionsConfirmModal();
      await savePermissions();
    }

    function showDeleteRecordModal() {
      if (!editingRecord || !editingRecord.emailHash) {
        notify('error', 'Delete Failed', 'Record data is missing, try reloading the page.');
        return;
      }
      document.getElementById('deleteRecordEmail').textContent = editingRecord.email || editingRecord.emailHash;
      document.getElementById('deleteRecordModal').style.display = 'flex';
    }

    function closeDeleteRecordModal() {
      document.getElementById('deleteRecordModal').style.display = 'none';
    }

    async function confirmDeleteRecord() {
      if (!editingRecord || !editingRecord.emailHash) {
        notify('error', 'Delete Failed', 'Record data is missing, try reloading the page.');
        return;
      }

      const bucket = editingBucket || currentBucket;
      const result = await apiRequest(`/api/admin/buckets/${encodeURIComponent(bucket)}/records/${encodeURIComponent(editingRecord.emailHash)}`, {
        method: 'DELETE'
      });

      if (result.ok) {
        notify('success', 'Record Removed', 'Consent record has been deleted');
        closeDeleteRecordModal();
        closeEditPermissionsModal();
        if (currentView === 'subscriptions' && subDetailHash) {
          showIdentityDetails(subDetailHash, false);
        } else if (currentBucket) {
          loadBucket(currentBucket);
        }
      } else { notify('error', 'Delete Failed', result.data?.error || 'Failed to delete record.'); }
    }

    async function savePermissions() {
      if (!editingRecord || !editingRecord.email) {
        notify('error', 'Update Failed', 'Record data is missing, try reloading the page.');
        return;
      }

      const permissions = {};
      document.querySelectorAll('#editPermissionsGrid .permission-row').forEach(row => {
        const perm = row.dataset.perm;
        const inBtn = row.querySelector('.opted-in');
        permissions[perm] = inBtn.classList.contains('active') ? 'OptedIn' : 'OptedOut';
      });

      const customFields = Object.keys(editCustomFields).length > 0 ? editCustomFields : undefined;
      const bucket = editingBucket || currentBucket;
      const nameVal = document.getElementById('editName').value.trim() || undefined;

      const result = await apiRequest(`/api/admin/buckets/${encodeURIComponent(bucket)}/override`, {
        method: 'POST',
        body: {
          email: editingRecord.email,
          name: nameVal,
          permissions,
          customFields
        }
      });

      if (result.ok) {
        notify('success', 'Permissions Updated', 'Consent preferences have been saved');
        closeEditPermissionsModal();
        if (currentBucket) loadBucket(currentBucket);
      } else { notify('error', 'Save Failed', result.data?.error || 'Failed to save permissions.'); }
    }

    async function openOptOutPage(encodedData) {
      const record = JSON.parse(decodeURIComponent(encodedData));
      closeAllMenus();

      if (!record.email) {
        notify('error', 'Link Generation Failed', 'Email address not available for this record.');
        return;
      }

      const permissions = {};
      currentBucketPermissions.forEach(p => {
        permissions[p] = record.permissions[p] === true;
      });

      const language = document.getElementById('editLanguage')?.value || 'en';

      const result = await apiRequest('/api/tokens/generate', {
        method: 'POST',
        body: [{
          bucket: currentBucket,
          email: record.email,
          permissions,
          expiryDays: 30,
          allowReplay: true,
          skipPermissionUpdate: true,
          language
        }]
      });

      if (result.ok) {
        const url = `${PUBLIC_URL || API_BASE}/u/${result.data[0].token}`;
        window.open(url, '_blank');
      } else { notify('error', 'Link Generation Failed', result.data?.error || 'Failed to generate opt-out link.'); }
    }

    // SSE WEBHOOK ERROR NOTIFICATIONS
    let sseAbortController = null;
    let sseReconnectDelay = 1000;

    function connectSSE() {
      if (sseAbortController) sseAbortController.abort();
      sseAbortController = new AbortController();

      fetch(`${window.location.origin}/api/admin/events`, {
        credentials: 'include',
        signal: sseAbortController.signal
      }).then(response => {
        if (!response.ok) throw new Error(`SSE failed: ${response.status}`);
        sseReconnectDelay = 1000;
        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';

        function read() {
          reader.read().then(({ done, value }) => {
            if (done) { scheduleReconnect(); return; }
            buffer += decoder.decode(value, { stream: true });
            const lines = buffer.split('\n');
            buffer = lines.pop();
            let eventType = '';
            let data = '';
            for (const line of lines) {
              if (line.startsWith('event: ')) { eventType = line.slice(7).trim(); }
              else if (line.startsWith('data: ')) { data = line.slice(6); }
              else if (line === '' && eventType && data) {
                handleSSEEvent(eventType, data);
                eventType = '';
                data = '';
              }
            }
            read();
          }).catch(err => {
            if (err.name !== 'AbortError') scheduleReconnect();
          });
        }
        read();
      }).catch(err => {
        if (err.name !== 'AbortError') scheduleReconnect();
      });
    }

    function scheduleReconnect() {
      if (!sseAbortController) return; // disconnectSSE was called, don't reconnect
      setTimeout(() => { if (sseAbortController) connectSSE(); }, sseReconnectDelay);
      sseReconnectDelay = Math.min(sseReconnectDelay * 2, 30000);
    }

    // Debounced consent-update handling to prevent DOM thrashing
    let _consentUpdateTimer = null;
    const _consentUpdatePending = new Set();

    function _flushConsentUpdates() {
      _consentUpdateTimer = null;
      const pendingBuckets = new Set(_consentUpdatePending);
      _consentUpdatePending.clear();

      // Fetch fresh bucket list and diff against current
      apiRequest('/api/admin/buckets').then(result => {
        if (!result.ok) return;
        const fresh = result.data || [];
        const oldNames = new Set(buckets.map(b => b.name));
        const sidebarChanged = fresh.length !== buckets.length || fresh.some(b => !oldNames.has(b.name));

        // Always update data so record counts / permissions stay current
        buckets = fresh;
        try { sessionStorage.setItem('beacon_buckets', JSON.stringify(buckets)); } catch {}

        // Only re-render sidebar DOM when the bucket list itself changed
        if (sidebarChanged) {
          renderBucketsSidebar();
        }

        // Refresh the overview if it's the active view
        if (currentView === 'overview') {
          loadOverview();
        }

        // Refresh the active bucket view only if it was affected
        if (currentView === 'bucket' && currentBucket && pendingBuckets.has(currentBucket)) {
          loadBucket(currentBucket);
        }

        // Refresh subscriptions view if active
        if (currentView === 'subscriptions') {
          if (subDetailHash) showIdentityDetails(subDetailHash, false);
          else loadIdentities(false);
        }
      });
    }

    function handleSSEEvent(type, data) {
      try {
        const evt = JSON.parse(data);
        if (type === 'webhook-error') {
          notify('warning', 'Webhook Error', `${evt.bucket}: ${evt.errorMessage}`);
          if (currentBucket && evt.bucket === currentBucket) {
            loadWebhookErrors(currentBucket);
          }
          loadOverview();
        } else if (type === 'consent-update') {
          _consentUpdatePending.add(evt.bucket);
          if (!_consentUpdateTimer) {
            _consentUpdateTimer = setTimeout(_flushConsentUpdates, 500);
          }
        }
      } catch {}
    }

    function disconnectSSE() {
      if (sseAbortController) { sseAbortController.abort(); sseAbortController = null; }
    }

    // START
    window.addEventListener('popstate', () => {
      const params = new URLSearchParams(window.location.search);
      if (!params.has('modal')) {
        document.getElementById('optionsModal').style.display = 'none';
      }
      restoreViewFromUrl();
    });

