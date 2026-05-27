    // LOAD BUCKETS
    async function loadBuckets() {
      // Restore cached buckets immediately to prevent pop-in
      if (buckets.length === 0) {
        try {
          const cached = sessionStorage.getItem('beacon_buckets');
          if (cached) {
            buckets = JSON.parse(cached);
            renderBucketsSidebar();
          }
        } catch {}
      }
      const result = await apiRequest('/api/admin/buckets');
      if (result.ok) {
        buckets = result.data || [];
        try { sessionStorage.setItem('beacon_buckets', JSON.stringify(buckets)); } catch {}
        renderBucketsSidebar();
      }
    }

    // Sidebar preferences (persisted in localStorage)
    const sidebarPrefs = JSON.parse(localStorage.getItem('beacon_sidebar_prefs') || '{}');
    if (!sidebarPrefs.sort) sidebarPrefs.sort = 'default';
    if (!sidebarPrefs.display) sidebarPrefs.display = 'description';

    function saveSidebarPrefs() {
      localStorage.setItem('beacon_sidebar_prefs', JSON.stringify(sidebarPrefs));
    }

    function cycleSidebarSort() {
      const cycle = { default: 'az', az: 'za', za: 'default' };
      sidebarPrefs.sort = cycle[sidebarPrefs.sort] || 'az';
      saveSidebarPrefs();
      renderBucketsSidebar();
    }

    function toggleSidebarDisplay() {
      sidebarPrefs.display = sidebarPrefs.display === 'description' ? 'code' : 'description';
      saveSidebarPrefs();
      renderBucketsSidebar();
    }

    function updateSidebarButtons() {
      const btn = document.getElementById('btnSort');
      const icon = document.getElementById('btnSortIcon');
      btn.classList.toggle('active', sidebarPrefs.sort !== 'default');
      document.getElementById('btnSortTooltip').textContent =
        sidebarPrefs.sort === 'az' ? 'Sorted A\u2013Z' : sidebarPrefs.sort === 'za' ? 'Sorted Z\u2013A' : 'Sort buckets';
      icon.style.transform = sidebarPrefs.sort === 'za' ? 'scaleY(-1)' : '';
      document.getElementById('btnDisplayMode').classList.toggle('active', sidebarPrefs.display === 'code');
      document.getElementById('btnDisplayTooltip').textContent =
        sidebarPrefs.display === 'code' ? 'Show descriptions' : 'Show code names';
    }

    function renderBucketsSidebar() {
      const container = document.getElementById('bucketsList');
      updateSidebarButtons();
      if (buckets.length === 0) {
        container.innerHTML = '<div class="nav-item" style="opacity:0.5;font-size:0.8rem">Create a token to get started</div>';
        return;
      }

      let sorted = [...buckets];
      if (sidebarPrefs.sort === 'az') sorted.sort((a, b) => a.name.localeCompare(b.name));
      else if (sidebarPrefs.sort === 'za') sorted.sort((a, b) => b.name.localeCompare(a.name));

      const displayBuckets = sorted.slice(0, MAX_SIDEBAR_BUCKETS);
      const showCode = sidebarPrefs.display === 'code';
      let html = displayBuckets.map(b => {
        const label = showCode ? b.name : formatPermission(b.name);
        return `<a href="#" class="nav-item" data-bucket="${sanitize(b.name)}" onclick="showBucket('${sanitize(b.name)}')" title="${sanitize(b.name)}">${sanitize(label)}</a>`;
      }).join('');

      if (buckets.length > MAX_SIDEBAR_BUCKETS) {
        const remaining = buckets.length - MAX_SIDEBAR_BUCKETS;
        html += `<a href="#" class="nav-item show-more" onclick="showView('overview')">+${remaining} more in Overview</a>`;
      }

      container.innerHTML = html;
    }

    let tokenPermissions = new Set();
    let removedPermissions = new Set();
    let tokenCustomFields = {};
    let editCustomFields = {};

    // When true, newly added permissions in the token modal default to Out (used when bucket has double opt-in)
    let tokenDefaultOptOut = false;

    function renderPermissionsGrid() {
      const container = document.getElementById('permissionsGrid');

      // Only auto-populate from the selected bucket
      const selectedBucket = document.getElementById('tokenBucket').value.trim().toLowerCase();
      const match = buckets.find(b => b.name === selectedBucket);
      if (match) {
        (match.permissions || []).forEach(p => {
          if (!removedPermissions.has(p)) {
            tokenPermissions.add(p);
          }
        });
      }

      if (tokenPermissions.size === 0) {
        container.innerHTML = '<div style="color:hsl(var(--muted-foreground));font-size:0.875rem;padding:1rem 0">No permissions yet. Add a custom permission below or generate a token first.</div>';
        _syncConfirmationEmailToggle();
        return;
      }

      const defaultIn  = tokenDefaultOptOut ? '' : ' active';
      const defaultOut = tokenDefaultOptOut ? ' active' : '';

      container.innerHTML = [...tokenPermissions].sort().map(p => `
        <div class="permission-row" data-perm="${sanitize(p)}">
          <span class="permission-name">${sanitize(formatPermission(p))}</span>
          <div class="permission-toggle">
            <button type="button" class="opted-in${defaultIn}" onclick="setPermState('${sanitize(p)}', true)">In</button>
            <button type="button" class="opted-out${defaultOut}" onclick="setPermState('${sanitize(p)}', false)">Out</button>
            <button type="button" class="skip" onclick="setPermState('${sanitize(p)}', null)">Skip</button>
          </div>
          <button type="button" class="btn-remove" onclick="removePermission('${sanitize(p)}')" title="Remove">&times;</button>
        </div>
      `).join('');
      _syncConfirmationEmailToggle();
    }

    function showPermissionSuggestions() {
      const input = document.getElementById('newPermissionInput');
      const dropdown = document.getElementById('permissionAutocomplete');
      const query = input.value.trim().toLowerCase();
      document.getElementById('bucketAutocomplete').classList.remove('open');
      const selectedBucket = document.getElementById('tokenBucket').value.trim().toLowerCase();
      const match = buckets.find(b => b.name === selectedBucket);
      if (!match) { hidePermissionSuggestions(); return; }

      const available = (match.permissions || []).filter(p =>
        !tokenPermissions.has(p) && p.includes(query)
      );

      if (available.length === 0 || (available.length === 1 && available[0] === query)) {
        hidePermissionSuggestions();
        return;
      }

      dropdown.innerHTML = available.slice(0, 10).map(p =>
        `<div class="autocomplete-item" onclick="selectPermission('${sanitize(p)}')">${sanitize(formatPermission(p))}</div>`
      ).join('');
      positionDropdown(input, dropdown);
      dropdown.style.display = 'block';
    }

    function hidePermissionSuggestions() {
      document.getElementById('permissionAutocomplete').style.display = '';
    }

    function selectPermission(perm) {
      document.getElementById('newPermissionInput').value = perm;
      hidePermissionSuggestions();
    }

    function addCustomPermission() {
      const input = document.getElementById('newPermissionInput');
      const perm = input.value.trim().toLowerCase().replace(/\s+/g, '_').replace(/[^a-z0-9_-]/g, '');

      if (!perm) {
        notify('warning', 'Invalid Permission', 'Please enter a permission name');
        return;
      }

      if (tokenPermissions.has(perm)) {
        notify('warning', 'Duplicate', 'Permission already exists');
        return;
      }

      tokenPermissions.add(perm);
      removedPermissions.delete(perm);
      input.value = '';
      hidePermissionSuggestions();
      renderPermissionsGrid();
    }

    function removePermission(perm) {
      tokenPermissions.delete(perm);
      removedPermissions.add(perm);
      renderPermissionsGrid();
      notify('success', 'Permission Removed', `"${formatPermission(perm)}" removed from this token`);
    }

    function setPermState(perm, state) {
      const row = document.querySelector(`.permission-row[data-perm="${perm}"]`);
      if (!row) return;
      row.querySelectorAll('.permission-toggle button').forEach(btn => btn.classList.remove('active'));
      if (state === true) row.querySelector('.opted-in').classList.add('active');
      else if (state === false) row.querySelector('.opted-out').classList.add('active');
      else row.querySelector('.skip').classList.add('active');
      _syncConfirmationEmailToggle();
    }

    function _syncConfirmationEmailToggle() {
      const group = document.getElementById('tokenSkipEmailGroup');
      if (!group || group.style.display === 'none') return;
      const hasOptIn = Object.values(getPermissionStates()).some(v => v === true);
      const checkbox = document.getElementById('tokenSendConfirmation');
      const label = group.querySelector('label.checkbox-toggle');
      checkbox.disabled = !hasOptIn;
      label.style.opacity = hasOptIn ? '' : '0.45';
      label.style.pointerEvents = hasOptIn ? '' : 'none';
    }

    function getPermissionStates() {
      const states = {};
      document.querySelectorAll('#permissionsGrid .permission-row').forEach(row => {
        const perm = row.dataset.perm;
        const inBtn = row.querySelector('.opted-in');
        const outBtn = row.querySelector('.opted-out');
        if (inBtn.classList.contains('active')) states[perm] = true;
        else if (outBtn.classList.contains('active')) states[perm] = false;
      });
      return states;
    }

    // CUSTOM FIELDS
    function openCustomFieldsDialog() {
      const frozen = document.getElementById('tokenFormFields').classList.contains('frozen');
      renderCustomFieldsGrid(frozen);
      const modal = document.getElementById('customFieldsModal');
      modal.querySelector('.custom-fields-add-row').style.display = frozen ? 'none' : '';
      modal.style.display = 'flex';
    }

    function closeCustomFieldsDialog() {
      document.getElementById('customFieldsModal').style.display = 'none';
      renderTokenCustomFieldsDisplay();
    }

    function addCustomField(context) {
      const isEdit = context === 'edit';
      const isNl = context === 'nl';
      const keyInput = document.getElementById(isNl ? 'nlFieldKey' : (isEdit ? 'editFieldKey' : 'newFieldKey'));
      const valueInput = document.getElementById(isNl ? 'nlFieldValue' : (isEdit ? 'editFieldValue' : 'newFieldValue'));
      const key = keyInput.value.trim();
      const value = valueInput.value.trim();

      if (!key) { notify('warning', 'Missing Key', 'Please enter a field key'); return; }

      const fields = isNl ? nlCustomFields : (isEdit ? editCustomFields : tokenCustomFields);
      fields[key] = value;
      keyInput.value = '';
      valueInput.value = '';
      keyInput.focus();

      if (isNl) {
        renderNlCustomFieldsList();
      } else if (isEdit) {
        renderEditCustomFieldsList();
      } else {
        renderCustomFieldsGrid();
        renderTokenCustomFieldsDisplay();
      }
    }

    function removeCustomField(key, context) {
      const isEdit = context === 'edit';
      const isNl = context === 'nl';
      const fields = isNl ? nlCustomFields : (isEdit ? editCustomFields : tokenCustomFields);
      delete fields[key];

      if (isNl) {
        renderNlCustomFieldsList();
      } else if (isEdit) {
        renderEditCustomFieldsList();
      } else {
        renderCustomFieldsGrid();
        renderTokenCustomFieldsDisplay();
      }
    }

    function renderCustomFieldsGrid(readonly = false) {
      const grid = document.getElementById('customFieldsGrid');
      const entries = Object.entries(tokenCustomFields);
      grid.innerHTML = entries.map(([k, v]) => `
        <div class="custom-field-row">
          <span class="custom-field-key">${sanitize(k)}</span>
          <span class="custom-field-value">${sanitize(v)}</span>
          ${readonly ? '' : `<button type="button" class="btn-remove" onclick="removeCustomField('${sanitize(k)}', 'token')" title="Remove">&times;</button>`}
        </div>
      `).join('');
    }

    function renderTokenCustomFieldsDisplay() {
      const entries = Object.entries(tokenCustomFields);
      const group = document.getElementById('tokenCustomFieldsGroup');
      const list = document.getElementById('tokenCustomFieldsList');

      if (entries.length === 0) {
        group.style.display = 'none';
        return;
      }

      group.style.display = '';
      list.innerHTML = entries.map(([k, v]) => `
        <div class="custom-field-row">
          <span class="custom-field-key">${sanitize(k)}</span>
          <span class="custom-field-value">${sanitize(v)}</span>
          <button type="button" class="btn-remove" onclick="removeCustomField('${sanitize(k)}', 'token')" title="Remove">&times;</button>
        </div>
      `).join('');
    }

    function renderEditCustomFieldsList() {
      const entries = Object.entries(editCustomFields);
      const group = document.getElementById('editCustomFieldsGroup');
      const list = document.getElementById('editCustomFieldsList');

      group.style.display = '';
      list.innerHTML = entries.map(([k, v]) => `
        <div class="custom-field-row">
          <span class="custom-field-key">${sanitize(k)}</span>
          <span class="custom-field-value">${sanitize(v)}</span>
          <button type="button" class="btn-remove" onclick="removeCustomField('${sanitize(k)}', 'edit')" title="Remove">&times;</button>
        </div>
      `).join('');
    }

    function insertNlFieldVariable(variable) {
      document.getElementById('nlFieldValue').value = variable;
      document.getElementById('nlFieldKey').focus();
    }

    function renderNlCustomFieldsList() {
      const entries = Object.entries(nlCustomFields);
      const list = document.getElementById('nlCustomFieldsList');
      list.innerHTML = entries.map(([k, v]) => {
        const isVariable = /^\{\{.+\}\}$/.test(v);
        const displayValue = isVariable
          ? `<code style="font-size:0.8rem;background:hsl(var(--muted));padding:1px 6px;border-radius:4px">${sanitize(v)}</code>`
          : sanitize(v);
        return `<div class="custom-field-row">
          <span class="custom-field-key">${sanitize(k)}</span>
          <span class="custom-field-value">${displayValue}</span>
          <button type="button" class="btn-remove" onclick="removeCustomField('${sanitize(k)}', 'nl')" title="Remove">&times;</button>
        </div>`;
      }).join('');
    }

