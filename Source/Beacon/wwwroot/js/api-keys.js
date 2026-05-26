    // API KEYS VIEW
    function triggerClass(el, cls, duration = 700) {
      if (!el) return;
      el.classList.remove(cls);
      void el.offsetWidth;
      el.classList.add(cls);
      setTimeout(() => el.classList.remove(cls), duration);
    }

    const KNOWN_PERMISSIONS = [
      { value: '_all',             label: 'Full Access (_all)' },
      { value: 'consent:read',     label: 'Consent — Read' },
      { value: 'consent:write',    label: 'Consent — Write' },
      { value: 'tokens:write',     label: 'Tokens — Generate' },
      { value: 'buckets:read',     label: 'Buckets — Read' },
      { value: 'buckets:write',    label: 'Buckets — Write' },
      { value: 'submissions:read', label: 'Submissions — Read' },
      { value: 'submissions:write',label: 'Submissions — Write' },
      { value: 'audit:read',       label: 'Audit — Read' },
      { value: 'webhooks:read',    label: 'Webhooks — Read' },
      { value: 'webhooks:write',   label: 'Webhooks — Write' },
    ];

    let _apiKeysAll = [], _apiKeysPage = 1, _apiKeysPageSize = 25;
    let _editApiKeyId = null;
    let _rotatedApiKey = '';
    let _newRawApiKey = '';

    async function loadApiKeys() {
      const result = await apiRequest('/api/admin/api-keys');
      if (!result.ok) return;
      _apiKeysAll = result.data || [];
      _apiKeysPage = 1;
      renderApiKeysPage();
    }

    function renderApiKeysPage() {
      const start = (_apiKeysPage - 1) * _apiKeysPageSize;
      renderApiKeysTable(_apiKeysAll.slice(start, start + _apiKeysPageSize));
      renderApiKeysPagination();
    }

    function renderApiKeysPagination() {
      const bar = document.getElementById('apiKeysPaginationBar');
      const info = document.getElementById('apiKeysPaginationInfo');
      const prevBtn = document.getElementById('apiKeysPrevBtn');
      const nextBtn = document.getElementById('apiKeysNextBtn');
      const total = _apiKeysAll.length;
      const totalPages = Math.ceil(total / _apiKeysPageSize) || 1;
      if (bar) bar.style.display = total > _apiKeysPageSize ? '' : 'none';
      if (info) {
        const start = (_apiKeysPage - 1) * _apiKeysPageSize + 1;
        const end = Math.min(_apiKeysPage * _apiKeysPageSize, total);
        info.textContent = `${start}–${end} of ${total}`;
      }
      if (prevBtn) prevBtn.disabled = _apiKeysPage <= 1;
      if (nextBtn) nextBtn.disabled = _apiKeysPage >= totalPages;
    }

    function changeApiKeysPage(dir) {
      const totalPages = Math.ceil(_apiKeysAll.length / _apiKeysPageSize) || 1;
      _apiKeysPage = Math.max(1, Math.min(_apiKeysPage + dir, totalPages));
      renderApiKeysPage();
    }

    function updateApiKeysPageSize() {
      _apiKeysPageSize = parseInt(document.getElementById('apiKeysPageSize').value, 10);
      _apiKeysPage = 1;
      renderApiKeysPage();
    }

    function renderPermissionBadges(permissions) {
      if (!permissions || !permissions.length) return '<span style="color:hsl(var(--muted-foreground))">—</span>';
      return permissions.map(p => {
        const isAll = p === '_all';
        return `<span class="status-badge${isAll ? '' : ' status-badge-muted'}" style="margin-right:0.25rem;font-size:0.7rem">${sanitize(p)}</span>`;
      }).join('');
    }

    function apiKeyStatus(k) {
      if (!k.isEnabled) return { label: 'Disabled', color: 'hsl(var(--muted-foreground))' };
      const now = new Date();
      if (k.activeFrom && now < new Date(k.activeFrom)) return { label: 'Pending',  color: 'hsl(var(--warning))' };
      if (k.activeUntil && now > new Date(k.activeUntil)) return { label: 'Expired', color: 'hsl(var(--destructive))' };
      return { label: 'Active', color: 'hsl(var(--success,142 71% 45%))' };
    }

    function renderApiKeysTable(keys) {
      const tbody = document.getElementById('apiKeysBody');
      if (!keys.length) {
        tbody.innerHTML = `<tr><td colspan="7"><div class="table-empty-state">
          <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><circle cx="7.5" cy="15.5" r="5.5"/><path d="m21 2-9.6 9.6"/><path d="m15.5 7.5 3 3L22 7l-3-3"/></svg>
          <p class="empty-title">No API keys</p>
          <p class="empty-sub">Grant programmatic access by creating a key above.</p>
        </div></td></tr>`;
        return;
      }
      tbody.innerHTML = keys.map(k => {
        const st = apiKeyStatus(k);
        return `
        <tr>
          <td><strong>${sanitize(k.name)}</strong></td>
          <td>${renderPermissionBadges(k.permissions)}</td>
          <td><span style="color:${st.color}">${st.label}</span></td>
          <td>${k.activeUntil ? new Date(k.activeUntil).toLocaleDateString() : '<span style="color:hsl(var(--muted-foreground))">—</span>'}</td>
          <td>${k.createdAt ? new Date(k.createdAt).toLocaleDateString() : '-'}</td>
          <td>${k.lastUsedAt ? formatRelativeTime(k.lastUsedAt) : '<span style="color:hsl(var(--muted-foreground))">Never</span>'}</td>
          <td class="col-actions">
            <div class="row-actions">
              <button class="btn-actions" onclick="event.stopPropagation();toggleMenu('apiKeyDropdown-${k.id}',this)" aria-label="Actions">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="5" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="12" cy="19" r="1"/></svg>
              </button>
              <div class="dropdown-menu" id="apiKeyDropdown-${k.id}">
                <button class="dropdown-item" onclick="openEditApiKeyPermissionsModal('${k.id}', '${sanitize(k.name)}', ${JSON.stringify(k.permissions)})">Edit Permissions</button>
                <button class="dropdown-item" onclick="openEditApiKeyDatesModal('${k.id}', '${sanitize(k.name)}', '${k.activeFrom || ''}', '${k.activeUntil || ''}')">Edit Dates</button>
                <button class="dropdown-item" onclick="toggleApiKeyEnabled('${k.id}', ${k.isEnabled})">${k.isEnabled ? 'Disable' : 'Enable'}</button>
                <button class="dropdown-item" onclick="rotateApiKey('${k.id}', '${sanitize(k.name)}')">Rotate Key</button>
                <button class="dropdown-item dropdown-item--destructive" onclick="deleteApiKey('${k.id}', '${sanitize(k.name)}')">Delete</button>
              </div>
            </div>
          </td>
        </tr>`;
      }).join('');
      tbody.querySelectorAll('tr').forEach((row, i) => {
        row.style.animationDelay = `${Math.min(i * 30, 150)}ms`;
      });
    }

    function buildPermissionCheckboxes(containerId, selected) {
      const container = document.getElementById(containerId);
      if (!container) return;
      container.innerHTML = KNOWN_PERMISSIONS.map(p => `
        <label style="display:flex;align-items:center;gap:0.5rem;cursor:pointer;font-size:0.875rem;font-weight:400;margin-bottom:0.35rem">
          <input type="checkbox" value="${p.value}" ${selected.includes(p.value) ? 'checked' : ''} style="width:auto;padding:0;margin:0;flex-shrink:0">
          ${p.label}
        </label>
      `).join('');

      const allCb = container.querySelector('input[value="_all"]');
      if (!allCb) return;
      const others = Array.from(container.querySelectorAll('input[type=checkbox]')).filter(cb => cb !== allCb);

      function syncAllState() {
        const isAll = allCb.checked;
        others.forEach(cb => {
          cb.checked = isAll;
          cb.disabled = isAll;
          if (isAll) triggerClass(cb, 'cb-flash', 300);
        });
      }

      syncAllState();
      allCb.addEventListener('change', syncAllState);
    }

    function getCheckedPermissions(containerId) {
      const container = document.getElementById(containerId);
      if (!container) return [];
      return Array.from(container.querySelectorAll('input[type=checkbox]:checked')).map(cb => cb.value);
    }

    function openAddApiKeyModal() {
      document.getElementById('newApiKeyName').value = '';
      document.getElementById('newApiKeyActiveFrom').value = '';
      document.getElementById('newApiKeyActiveUntil').value = '';
      document.getElementById('newApiKeyRevealSection').style.display = 'none';
      buildPermissionCheckboxes('newApiKeyPermissions', []);
      const btn = document.getElementById('addApiKeyBtn');
      btn.classList.remove('is-loading'); btn.disabled = false;
      btn.querySelector('.btn-label').textContent = 'Create Key';
      document.getElementById('addApiKeyCancelBtn').style.display = '';
      _newRawApiKey = '';
      document.getElementById('addApiKeyModal').style.display = 'flex';
      setTimeout(() => document.getElementById('newApiKeyName')?.focus(), 50);
    }

    function closeAddApiKeyModal() {
      document.getElementById('addApiKeyModal').style.display = 'none';
    }

    async function createApiKey() {
      const name = document.getElementById('newApiKeyName').value.trim();
      const permissions = getCheckedPermissions('newApiKeyPermissions');
      const activeFrom = document.getElementById('newApiKeyActiveFrom').value;
      const activeUntil = document.getElementById('newApiKeyActiveUntil').value;

      if (!name) { notify('error', 'Validation', 'Name is required.'); return; }
      if (_apiKeysAll.some(k => k.name.toLowerCase() === name.toLowerCase()))
        { notify('error', 'Validation', 'An API key with this name already exists.'); return; }
      if (!permissions.length) { notify('error', 'Validation', 'Select at least one permission.'); return; }
      if (activeFrom && activeUntil && new Date(activeUntil) <= new Date(activeFrom))
        { notify('error', 'Validation', 'Active Until must be after Active From.'); return; }

      const btn = document.getElementById('addApiKeyBtn');
      btn.classList.add('is-loading'); btn.disabled = true;

      const body = { name, permissions };
      if (activeFrom) body.activeFrom = activeFrom;
      if (activeUntil) body.activeUntil = activeUntil;

      const result = await apiRequest('/api/admin/api-keys', { method: 'POST', body });

      btn.classList.remove('is-loading'); btn.disabled = false;

      if (result.ok) {
        _newRawApiKey = result.data.apiKey;
        const newKeyOut = document.getElementById('newApiKeyOutput');
        newKeyOut.textContent = _newRawApiKey;
        document.getElementById('newApiKeyRevealSection').style.display = 'block';
        triggerClass(newKeyOut, 'key-new', 1200);
        btn.querySelector('.btn-label').textContent = 'Done';
        btn.onclick = closeAddApiKeyModal;
        document.getElementById('addApiKeyCancelBtn').style.display = 'none';
        loadApiKeys();
      } else {
        notify('error', 'Error', result.data?.error || 'Failed to create API key.');
      }
    }

    function copyNewApiKey() {
      if (!_newRawApiKey) return;
      navigator.clipboard.writeText(_newRawApiKey).then(() => {
        triggerClass(document.getElementById('newApiKeyOutput'), 'key-copied', 700);
        notify('success', 'Copied', 'API key copied to clipboard.');
      });
    }

    async function deleteApiKey(id, name) {
      openConfirmModal(
        `Delete API Key`,
        `Delete <strong>${sanitize(name)}</strong>? This action cannot be undone. Any services using this key will lose access.`,
        'Delete',
        async () => {
          const result = await apiRequest(`/api/admin/api-keys/${id}`, { method: 'DELETE' });
          if (result.ok) { loadApiKeys(); notify('success', 'Deleted', `API key deleted.`); }
          else { notify('error', 'Error', result.data?.error || 'Failed to delete API key.'); }
        }
      );
    }

    async function toggleApiKeyEnabled(id, currentEnabled) {
      const result = await apiRequest(`/api/admin/api-keys/${id}/enabled`, {
        method: 'PATCH',
        body: { isEnabled: !currentEnabled }
      });
      if (result.ok) loadApiKeys();
      else notify('error', 'Error', result.data?.error || 'Failed to update API key.');
    }

    async function rotateApiKey(id, name) {
      openConfirmModal(
        `Rotate API Key`,
        `Rotate <strong>${sanitize(name)}</strong>? The current key will be invalidated immediately.`,
        'Rotate',
        async () => {
          const result = await apiRequest(`/api/admin/api-keys/${id}/rotate`, { method: 'POST' });
          if (result.ok) {
            _rotatedApiKey = result.data.apiKey;
            document.getElementById('apiKeyRevealDesc').textContent = `New key for "${name}". Copy it now — it won't be shown again.`;
            const revealOut = document.getElementById('apiKeyRevealOutput');
            revealOut.textContent = _rotatedApiKey;
            document.getElementById('apiKeyRevealModal').style.display = 'flex';
            triggerClass(revealOut, 'key-new', 1200);
            loadApiKeys();
          } else {
            notify('error', 'Error', result.data?.error || 'Failed to rotate API key.');
          }
        }
      );
    }

    function closeApiKeyRevealModal() {
      document.getElementById('apiKeyRevealModal').style.display = 'none';
    }

    function copyRotatedApiKey() {
      if (!_rotatedApiKey) return;
      navigator.clipboard.writeText(_rotatedApiKey).then(() => {
        triggerClass(document.getElementById('apiKeyRevealOutput'), 'key-copied', 700);
        notify('success', 'Copied', 'API key copied to clipboard.');
      });
    }

    function openEditApiKeyPermissionsModal(id, name, permissions) {
      _editApiKeyId = id;
      document.getElementById('editApiKeyPermissionsDesc').textContent = `Editing permissions for "${name}".`;
      buildPermissionCheckboxes('editApiKeyPermissionsList', permissions);
      const btn = document.getElementById('editApiKeyPermissionsSaveBtn');
      btn.classList.remove('is-loading'); btn.disabled = false;
      document.getElementById('editApiKeyPermissionsModal').style.display = 'flex';
    }

    function closeEditApiKeyPermissionsModal() {
      document.getElementById('editApiKeyPermissionsModal').style.display = 'none';
      _editApiKeyId = null;
    }

    async function saveApiKeyPermissions() {
      const permissions = getCheckedPermissions('editApiKeyPermissionsList');
      if (!permissions.length) { notify('error', 'Validation', 'Select at least one permission.'); return; }
      const btn = document.getElementById('editApiKeyPermissionsSaveBtn');
      btn.classList.add('is-loading'); btn.disabled = true;
      const result = await apiRequest(`/api/admin/api-keys/${_editApiKeyId}/permissions`, {
        method: 'PATCH',
        body: { permissions }
      });
      btn.classList.remove('is-loading'); btn.disabled = false;
      if (result.ok) { closeEditApiKeyPermissionsModal(); loadApiKeys(); notify('success', 'Saved', 'Permissions updated.'); }
      else { notify('error', 'Error', result.data?.error || 'Failed to update permissions.'); }
    }

    function openEditApiKeyDatesModal(id, name, activeFrom, activeUntil) {
      _editApiKeyId = id;
      document.getElementById('editApiKeyDatesDesc').textContent = `Set validity window for "${name}".`;
      document.getElementById('editApiKeyActiveFrom').value = activeFrom ? activeFrom.substring(0, 10) : '';
      document.getElementById('editApiKeyActiveUntil').value = activeUntil ? activeUntil.substring(0, 10) : '';
      const btn = document.getElementById('editApiKeyDatesSaveBtn');
      btn.classList.remove('is-loading'); btn.disabled = false;
      document.getElementById('editApiKeyDatesModal').style.display = 'flex';
    }

    function closeEditApiKeyDatesModal() {
      document.getElementById('editApiKeyDatesModal').style.display = 'none';
      _editApiKeyId = null;
    }

    async function saveApiKeyDates() {
      const activeFrom = document.getElementById('editApiKeyActiveFrom').value || null;
      const activeUntil = document.getElementById('editApiKeyActiveUntil').value || null;
      if (activeFrom && activeUntil && new Date(activeUntil) <= new Date(activeFrom))
        { notify('error', 'Validation', 'Active Until must be after Active From.'); return; }

      const btn = document.getElementById('editApiKeyDatesSaveBtn');
      btn.classList.add('is-loading'); btn.disabled = true;
      const body = { activeFrom, activeUntil };
      const result = await apiRequest(`/api/admin/api-keys/${_editApiKeyId}/dates`, { method: 'PATCH', body });
      btn.classList.remove('is-loading'); btn.disabled = false;
      if (result.ok) { closeEditApiKeyDatesModal(); loadApiKeys(); notify('success', 'Saved', 'Validity window updated.'); }
      else { notify('error', 'Error', result.data?.error || 'Failed to update dates.'); }
    }

