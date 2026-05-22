    document.getElementById('openapi-link').href = (typeof API_BASE !== 'undefined' && API_BASE ? API_BASE : '') + '/openapi';
    const MAX_SIDEBAR_BUCKETS = 10;
    let currentBucket = '';
    let currentView = 'overview';
    let auditCurrentPage = 1, auditPageSize = 25, auditTotalRecords = 0;
    let auditFilterBucket = null, auditFilterIdentity = null;
    let _auditClickTimer = null;
    let currentBucketArchived = false;
    let currentPage = 1;
    let pageSize = 10;
    let totalRecords = 0;
    const _sortPrefs = JSON.parse(localStorage.getItem('beacon_sort_prefs') || '{}');
    let sortBy = _sortPrefs.sortBy || 'lastchanged';
    let sortDir = _sortPrefs.sortDir || 'desc';
    let searchQuery = '';
    let searchType = 'id';
    let buckets = [];

    // SESSION STATE (token lives in HttpOnly cookie)
    let currentUserRole = sessionStorage.getItem('beacon_user_role') || 'admin';
    let currentUsername  = sessionStorage.getItem('beacon_username')  || '';
    let tokenExpiresAt   = sessionStorage.getItem('beacon_jwt_exp') ? new Date(sessionStorage.getItem('beacon_jwt_exp')) : null;
    let lastActivity          = Date.now();
    let refreshInterval       = null;
    let _redirectingToLogout  = false;

    // CENTRALIZED API REQUESTS
    async function apiRequest(endpoint, options = {}) {
      const url = `${window.location.origin}${endpoint}`;
      const config = {
        ...options,
        credentials: 'include',  // Send the HttpOnly auth cookie with every request
        headers: { ...options.headers }
      };

      if (options.body && typeof options.body === 'object') {
        config.headers['Content-Type'] = 'application/json';
        config.body = JSON.stringify(options.body);
      }

      try {
        const res = await fetch(url, config);

        if (res.status === 401) {
          if (!options.skipAuthRedirect && !_redirectingToLogout) {
            _redirectingToLogout = true;
            fetch(`${window.location.origin}/api/admin/auth/logout`, { method: 'POST', credentials: 'include' }).catch(() => {});
            ['beacon_user_role', 'beacon_username', 'beacon_jwt_exp', 'beacon_buckets', 'beacon_user_id']
              .forEach(k => sessionStorage.removeItem(k));
            window.location.href = '/admin/logout?reason=expired';
          }
          return { ok: false, status: 401, data: null };
        }

        let data = null;
        const contentType = res.headers.get('content-type');
        if (contentType && contentType.includes('application/json')) {
          data = await res.json();
        }

        if (!res.ok) {
          const errorMsg = data?.error || `Request failed (${res.status})`;
          notify('error', 'Request Failed', errorMsg);
          return { ok: false, status: res.status, data };
        }

        return { ok: true, status: res.status, data };
      } catch (e) {
        const msg = e instanceof TypeError
          ? 'Could not connect to server. The server may be down or returned an error. Check the server logs.'
          : (e.message || 'An unexpected error occurred.');
        notify('error', 'Request Failed', msg);
        return { ok: false, status: 0, data: null };
      }
    }

    async function testAuth() {
      const [bucketsResult, meResult] = await Promise.all([
        apiRequest('/api/admin/buckets', { skipAuthRedirect: true }),
        apiRequest('/api/admin/auth/me',  { skipAuthRedirect: true })
      ]);
      if (bucketsResult.ok) {
        buckets = bucketsResult.data || [];
        try { sessionStorage.setItem('beacon_buckets', JSON.stringify(buckets)); } catch {}
        if (meResult.ok && meResult.data?.role) {
          currentUserRole = meResult.data.role;
          currentUsername  = meResult.data.username || currentUsername;
          sessionStorage.setItem('beacon_user_role', currentUserRole);
          sessionStorage.setItem('beacon_username',  currentUsername);
        }
        return true;
      }
      return false;
    }

    // TOKEN REFRESH (slides the HttpOnly cookie expiry)
    function startTokenRefresh() {
      if (refreshInterval) clearInterval(refreshInterval);
      refreshInterval = setInterval(async () => {
        if (!tokenExpiresAt) return;
        const now = Date.now();
        const expiresIn = tokenExpiresAt.getTime() - now;
        const wasActiveRecently = (now - lastActivity) < 10 * 60 * 1000; // 10 min
        const expiresSoon = expiresIn < 15 * 60 * 1000;                  // 15 min

        if (wasActiveRecently && expiresSoon) {
          try {
            const result = await apiRequest('/api/admin/auth/refresh', { method: 'POST', skipAuthRedirect: true });
            if (result.ok && result.data?.expiresAt) {
              tokenExpiresAt = new Date(result.data.expiresAt);
              sessionStorage.setItem('beacon_jwt_exp', result.data.expiresAt);
            } else if (result.status === 401) {
              notify('error', 'Session Expired', 'Please log in again.');
              window.location.href = '/admin/login';
            }
          } catch { /* network error, will retry next interval */ }
        }
      }, 5 * 60 * 1000); // every 5 minutes
    }

    // INIT
    async function init() {
      console.log('%c@melosso/beacon', 'background:#0a0a0f;color:#FF64B4;font-weight:700;padding:2px 6px;border-radius:3px;font-size:13px');
      console.log('%cAPI docs → /openapi   Source → https://github.com/melosso/beacon', 'color:#555;font-size:11px');

      document.getElementById('tokenExpiry').value = DEFAULT_EXPIRY_DAYS;

      // Track user activity for refresh decisions
      ['mousemove', 'keydown', 'click', 'scroll'].forEach(evt =>
        document.addEventListener(evt, () => { lastActivity = Date.now(); }, { passive: true })
      );

      startTokenRefresh();

      // Restore cached buckets for instant render while we verify the session
      try {
        const cached = sessionStorage.getItem('beacon_buckets');
        if (cached) { buckets = JSON.parse(cached); renderBucketsSidebar(); }
      } catch {}

      // Verify cookie session with server (also loads fresh buckets)
      const valid = await testAuth();
      if (!valid) {
        window.location.href = '/admin/login';
        return;
      }

      renderBucketsSidebar();
      setupUserAuthNav(currentUserRole);
      updateSidebarUser();
      restoreViewFromUrl();
      connectSSE();
      loadServerSettings();
    }

    function restoreViewFromUrl() {
      const params = new URLSearchParams(window.location.search);
      const view = params.get('view');
      const bucket = params.get('bucket');
      const modal = params.get('modal');

      if (view === 'bucket' && bucket) {
        sortBy = params.get('sort') || sortBy;
        sortDir = params.get('dir') || sortDir;
        currentPage = parseInt(params.get('page')) || 1;
        pageSize = parseInt(params.get('size')) || 10;
        searchQuery = params.get('q') || '';
        searchType = params.get('qtype') || 'id';
        showBucket(bucket, false);
        if (modal === 'options') showOptionsModal(false);
        if (modal === 'error-detail') {
          const errorId = params.get('errorId');
          if (errorId) loadWebhookErrors(bucket).then(() => {
            const error = webhookErrorsCache.find(e => e.id === errorId);
            if (error) openErrorDetail(error);
          });
        }
      } else if (view === 'new-bucket') {
        showView('new-bucket', false);
      } else if (view === 'new-token') {
        showView('new-token', false);
      } else if (view === 'submissions') {
        showView('submissions', false);
      } else if (view === 'submission-create') {
        showView('submission-create', false);
      } else if (view === 'subscriptions') {
        subSearchQuery = params.get('q') || '';
        subSearchType = params.get('qtype') || 'id';
        subSortBy = params.get('sort') || subSortBy;
        subSortDir = params.get('dir') || subSortDir;
        subCurrentPage = parseInt(params.get('page')) || 1;
        subPageSize = parseInt(params.get('size')) || 10;
        const identity = params.get('identity');
        if (identity) {
          showView('subscriptions', false, true);
          showIdentityDetails(identity, false);
        } else {
          showView('subscriptions', false);
        }
      } else if (view === 'submission-edit') {
        const nlId = params.get('id');
        if (nlId) {
          editSubmissionForm(nlId, false);
        } else {
          showView('submissions', false);
        }
      } else if (view === 'submission-embed') {
        const nlId = params.get('id');
        if (nlId) {
          showEmbedCode(nlId);
        } else {
          showView('submissions', false);
        }
      } else if (view === 'submission-preview') {
        const nlId = params.get('id');
        const mode = params.get('mode') || 'iframe';
        if (nlId) {
          nlCurrentFormId = nlId;
          showPreview(nlId, mode);
        } else {
          showView('submissions', false);
        }
      } else if (view === 'settings') {
        const section = params.get('section') || 'general';
        showView('settings', false);
        showSettingsSection(section, false);
      } else if (view === 'workflow') {
        showView('workflow', false);
      } else if (view === 'audit') {
        auditFilterBucket = params.get('bucket') || null;
        auditFilterIdentity = params.get('identity') || null;
        auditCurrentPage = parseInt(params.get('page') || '1');
        showView('audit', false);
      } else {
        showView('overview', false);
      }
    }

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

    // VIEWS
    function updateUrl(params, replace = false) {
      const url = new URL(window.location);
      url.search = '';
      url.hash = '';
      for (const [key, value] of Object.entries(params)) {
        if (value) url.searchParams.set(key, value);
      }
      if (replace) {
        history.replaceState(null, '', url);
      } else {
        history.pushState(null, '', url);
      }
    }

    function syncBucketUrl(replace = false) {
      const params = { view: 'bucket', bucket: currentBucket };
      if (sortBy !== 'lastchanged') params.sort = sortBy;
      if (sortDir !== 'desc') params.dir = sortDir;
      if (currentPage > 1) params.page = String(currentPage);
      if (pageSize !== 10) params.size = String(pageSize);
      if (searchQuery) params.q = searchQuery;
      if (searchType !== 'id') params.qtype = searchType;
      updateUrl(params, replace);
    }

    function syncSubUrl(identity = null, replace = false) {
      const params = { view: 'subscriptions' };
      if (identity) {
        params.identity = identity;
        if (subSearchQuery) params.q = subSearchQuery;
        if (subSearchType !== 'id') params.qtype = subSearchType;
      } else {
        if (subSortBy !== 'lastchanged') params.sort = subSortBy;
        if (subSortDir !== 'desc') params.dir = subSortDir;
        if (subCurrentPage > 1) params.page = String(subCurrentPage);
        if (subPageSize !== 10) params.size = String(subPageSize);
        if (subSearchQuery) params.q = subSearchQuery;
        if (subSearchType !== 'id') params.qtype = subSearchType;
      }
      updateUrl(params, replace);
    }

    function showView(view, pushState = true, skipLoad = false) {
      // Users and account are now Settings sections
      if (view === 'users') { showView('settings', pushState, true); showSettingsSection('users', pushState); return; }
      if (view === 'account') { showView('settings', pushState, true); showSettingsSection('account', pushState); return; }
      currentView = view;
      document.querySelectorAll('.view').forEach(v => v.classList.remove('active'));
      document.querySelectorAll('.nav-item').forEach(n => n.classList.remove('active'));

      // submission-edit reuses the create view DOM
      const viewId = (view === 'submission-edit' || view === 'submission-embed') ? 'view-submission-create' : `view-${view}`;
      const viewEl = document.getElementById(viewId);
      if (viewEl) viewEl.classList.add('active');

      document.querySelector(`.nav-item[data-view="${view}"]`)?.classList.add('active');
      // Highlight submission nav when in create/edit
      if (view === 'submission-create' || view === 'submission-edit' || view === 'submission-embed' || view === 'submission-preview') {
        document.querySelector('.nav-item[data-view="submissions"]')?.classList.add('active');
      }

      if (pushState && view !== 'subscriptions') updateUrl({ view });
      
      if (skipLoad) return;

      if (view === 'overview') loadOverview();
      if (view === 'submissions') loadSubmissionForms();
      if (view === 'submission-create') initSubmissionWizard();
      if (view === 'new-bucket') renderNewBucketPerms();
      if (view === 'new-token') document.getElementById('tokenLanguage').value = appSettings.uiLanguage || 'en';
      if (view === 'settings') loadSettings();
      if (view === 'workflow') loadWorkflowPage();
      if (view === 'audit') loadAudit();
      if (view === 'subscriptions') {
        loadIdentities(pushState);
      }
    }

    function showBucket(bucket, pushState = true) {
      if (document.getElementById('optionsModal').style.display === 'flex') closeOptionsModal();
      currentBucket = bucket;
      if (pushState) {
        currentPage = 1;
        searchQuery = '';
        searchType = 'id';
      }

      document.querySelectorAll('.view').forEach(v => v.classList.remove('active'));
      document.querySelectorAll('.nav-item').forEach(n => n.classList.remove('active'));

      document.getElementById('view-bucket').classList.add('active');
      document.getElementById('bucketTitle').textContent = formatPermission(bucket);
      document.getElementById('bucketTooltip').textContent = bucket;

      document.querySelector(`.nav-item[data-bucket="${bucket}"]`)?.classList.add('active');

      if (pushState) syncBucketUrl();
      loadBucket(bucket);
    }

    // SUBSCRIPTIONS
    let subCurrentPage = 1;
    let subPageSize = 10;
    let subTotalRecords = 0;
    const _subSortPrefs = JSON.parse(localStorage.getItem('beacon_sub_sort_prefs') || '{}');
    let subSortBy = _subSortPrefs.sortBy || 'lastchanged';
    let subSortDir = _subSortPrefs.sortDir || 'desc';
    let subSearchQuery = '';
    let subSearchType = 'id';

    // Detail view sort state (client-side, data is already loaded)
    const _subDetailSortPrefs = JSON.parse(localStorage.getItem('beacon_sub_detail_sort_prefs') || '{}');
    let subDetailSortBy = _subDetailSortPrefs.sortBy || 'bucket';
    let subDetailSortDir = _subDetailSortPrefs.sortDir || 'asc';
    let subDetailSubscriptions = [];
    let subDetailEmail = null;
    let subDetailHash = '';
    let subDetailLoaded = false;

    function toggleDetailSort(column) {
      if (subDetailSortBy === column) {
        subDetailSortDir = subDetailSortDir === 'asc' ? 'desc' : 'asc';
      } else {
        subDetailSortBy = column;
        subDetailSortDir = column === 'lastchanged' ? 'desc' : 'asc';
      }
      localStorage.setItem('beacon_sub_detail_sort_prefs', JSON.stringify({ sortBy: subDetailSortBy, sortDir: subDetailSortDir }));
      renderDetailSubscriptions();
    }

    function renderDetailSubscriptions() {
      const body = document.getElementById('subscriptionsBody');
      const thead = document.getElementById('subscriptionsTableHead');
      const sortIcon = '<span class="sort-icon">▲</span>';
      const bucketSortClass = subDetailSortBy === 'bucket' ? (subDetailSortDir === 'asc' ? 'asc' : 'desc') : '';
      const dateSortClass = subDetailSortBy === 'lastchanged' ? (subDetailSortDir === 'asc' ? 'asc' : 'desc') : '';

      thead.innerHTML = `
        <th class="sortable ${bucketSortClass}" onclick="toggleDetailSort('bucket')" style="cursor:pointer">Bucket ${sortIcon}</th>
        <th>Permissions</th>
        <th class="sortable ${dateSortClass}" onclick="toggleDetailSort('lastchanged')" style="cursor:pointer">Last Changed ${sortIcon}</th>
        <th class="col-actions"></th>
      `;

      if (subDetailSubscriptions.length === 0) {
        body.innerHTML = subDetailLoaded
          ? `<tr><td colspan="4" style="text-align:center;padding:3rem;color:hsl(var(--muted-foreground))">No subscriptions found</td></tr>`
          : '';
        return;
      }

      const sorted = [...subDetailSubscriptions].sort((a, b) => {
        if (subDetailSortBy === 'bucket') {
          return subDetailSortDir === 'asc'
            ? a.bucket.localeCompare(b.bucket)
            : b.bucket.localeCompare(a.bucket);
        }
        const da = new Date(a.lastChanged), db = new Date(b.lastChanged);
        return subDetailSortDir === 'asc' ? da - db : db - da;
      });

      body.innerHTML = sorted.map((s, idx) => {
        const permsHtml = `<div style="display:flex;flex-wrap:wrap;gap:0.25rem 0.5rem">` +
          Object.entries(s.permissions).map(([p, optedIn]) => {
            return `<span class="tooltip-wrapper select-none" style="display:inline-flex;align-items:center;gap:0.35rem"><span class="permission-name">${sanitize(formatPermission(p))}</span><label class="switch disabled"><input type="checkbox" ${optedIn ? 'checked' : ''} disabled><span class="slider"></span></label><span class="tooltip tooltip-above">${optedIn ? 'Opted-in' : 'Opted-out'}</span></span>`;
          }).join('') + `</div>`;
        const permKeys = encodeURIComponent(JSON.stringify(Object.keys(s.permissions)));
        const recordData = encodeURIComponent(JSON.stringify({
          email: subDetailEmail,
          emailHash: subDetailHash,
          permissions: s.permissions,
          customFields: {}
        }));
        return `
          <tr>
            <td><span class="tooltip-wrapper select-none" style="cursor:pointer" onclick="showBucket('${sanitize(s.bucket)}')">${sanitize(formatPermission(s.bucket))}<span class="tooltip tooltip-above">${sanitize(s.bucket)}</span></span></td>
            <td>${permsHtml}</td>
            <td>${sanitize(formatDate(s.lastChanged))}</td>
            <td>
              <div class="row-actions">
                <span class="tooltip-wrapper">
                  <button class="btn-actions" onclick="toggleRowMenu(event, 'sub-${idx}')">:</button>
                  <span class="tooltip tooltip-above tooltip-right">Actions</span>
                </span>
                <div class="dropdown-menu" id="rowMenu-sub-${idx}">
                  <button class="dropdown-item" onclick="showBucket('${sanitize(s.bucket)}')">View Bucket</button>
                  <button class="dropdown-item" onclick="openEditFromSubscription('${sanitize(s.bucket)}', '${permKeys}', '${recordData}')">Edit Permissions</button>
                  <button class="dropdown-item" onclick="showAuditForBucketAndIdentity('${sanitize(s.bucket)}', '${sanitize(subDetailHash)}')">View Audit</button>
                </div>
              </div>
            </td>
          </tr>
        `;
      }).join('');
    }

    function updateSubPagination() {
      const totalPages = Math.ceil(subTotalRecords / subPageSize) || 1;
      const start = subTotalRecords === 0 ? 0 : (subCurrentPage - 1) * subPageSize + 1;
      const end = Math.min(subCurrentPage * subPageSize, subTotalRecords);
      document.getElementById('subPaginationInfo').textContent = `Showing ${start.toLocaleString()} to ${end.toLocaleString()} of ${subTotalRecords.toLocaleString()} entries`;
      document.getElementById('subPrevBtn').disabled = subCurrentPage === 1;
      document.getElementById('subNextBtn').disabled = subCurrentPage >= totalPages;
    }

    function updateSubPageSize() {
      subPageSize = parseInt(document.getElementById('subPageSize').value);
      subCurrentPage = 1;
      loadIdentities();
    }

    function changeSubPage(delta) {
      subCurrentPage += delta;
      loadIdentities();
    }

    function toggleSubSort(column) {
      if (subSortBy === column) {
        subSortDir = subSortDir === 'asc' ? 'desc' : 'asc';
      } else {
        subSortBy = column;
        subSortDir = column === 'lastchanged' ? 'desc' : 'asc';
      }
      localStorage.setItem('beacon_sub_sort_prefs', JSON.stringify({ sortBy: subSortBy, sortDir: subSortDir }));
      subCurrentPage = 1;
      loadIdentities();
    }

    function toggleSubSearchPopover(event) {
      event.stopPropagation();
      const popover = document.getElementById('subSearchPopover');
      const trigger = document.getElementById('subSearchTrigger');
      popover.classList.toggle('open');
      trigger.classList.toggle('active', popover.classList.contains('open'));
      if (popover.classList.contains('open')) {
        setTimeout(() => document.getElementById('subSearchInput')?.focus(), 50);
      }
    }

    function searchIdentities() {
      const input = document.getElementById('subSearchInput');
      subSearchQuery = input ? input.value.trim() : '';
      subCurrentPage = 1;
      loadIdentities();
      document.getElementById('subSearchPopover')?.classList.remove('open');
      document.getElementById('subSearchTrigger')?.classList.remove('active');
    }

    function clearSubSearch() {
      const input = document.getElementById('subSearchInput');
      if (input) input.value = '';
      subSearchQuery = '';
      subCurrentPage = 1;
      loadIdentities();
      document.getElementById('subSearchPopover')?.classList.remove('open');
      document.getElementById('subSearchTrigger')?.classList.remove('active');
    }

    function setSubSearchType(type) {
      subSearchType = type;
      // Update toggle, label, placeholder in-place
      document.querySelectorAll('#subSearchPopover .search-type-btn').forEach(btn => {
        btn.classList.toggle('active', btn.textContent.trim() === (type === 'id' ? 'By ID' : 'By Email'));
      });
      const label = document.querySelector('#subSearchPopover label');
      if (label) label.textContent = type === 'id' ? 'Enter ID from logs (partial hash)' : 'Enter email (partial match)';
      const input = document.getElementById('subSearchInput');
      if (input) { input.placeholder = type === 'id' ? 'e.g., a1b2c3d4' : 'e.g., @gmail.com'; input.focus(); }
    }

    async function loadIdentities(pushState = true) {
      const thead = document.getElementById('subscriptionsTableHead');
      const body = document.getElementById('subscriptionsBody');

      document.getElementById('subTitle').textContent = 'Subscriptions';
      const badge = document.getElementById('subBadge');
      badge.textContent = 'Global Identities';
      badge.classList.remove('tooltip-wrapper');
      badge.removeAttribute('data-hash');
      badge.style.cursor = '';
      badge.onclick = null;
      document.getElementById('subBackBtn').style.display = 'none';
      document.getElementById('subRefreshWrapper').style.display = 'inline-block';
      document.querySelector('#view-subscriptions .pagination-container').style.display = 'flex';

      if (pushState) syncSubUrl();

      const idSortClass = subSortBy === 'id' ? (subSortDir === 'asc' ? 'asc' : 'desc') : '';
      const bucketsSortClass = subSortBy === 'buckets' ? (subSortDir === 'asc' ? 'asc' : 'desc') : '';
      const dateSortClass = subSortBy === 'lastchanged' ? (subSortDir === 'asc' ? 'asc' : 'desc') : '';
      const sortIcon = '<span class="sort-icon">▲</span>';
      const hasSearch = subSearchQuery ? 'has-search' : '';

      thead.innerHTML = `
        <th>
          <div class="column-search">
            <span class="sortable ${idSortClass}" onclick="toggleSubSort('id')" style="cursor:pointer">Identity (ID) ${sortIcon}</span>
            <button class="search-trigger ${hasSearch}" id="subSearchTrigger" onclick="toggleSubSearchPopover(event)" title="Search">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <circle cx="11" cy="11" r="8"></circle>
                <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
              </svg>
            </button>
            <div class="search-popover" id="subSearchPopover" style="min-width:240px">
              <div class="search-type-toggle">
                <button class="search-type-btn ${subSearchType === 'id' ? 'active' : ''}" onclick="event.stopPropagation();setSubSearchType('id')">By ID</button>
                <button class="search-type-btn ${subSearchType === 'email' ? 'active' : ''}" onclick="event.stopPropagation();setSubSearchType('email')">By Email</button>
              </div>
              <label>${subSearchType === 'id' ? 'Enter ID from logs (partial hash)' : 'Enter email (partial match)'}</label>
              <input type="text" id="subSearchInput" placeholder="${subSearchType === 'id' ? 'e.g., a1b2c3d4' : 'e.g., @gmail.com'}" value="${sanitize(subSearchQuery)}" onkeydown="if(event.key==='Enter')searchIdentities()">
              <div class="search-actions">
                <button class="btn btn-outline" style="padding:0.375rem 0.75rem;font-size:0.75rem" onclick="clearSubSearch()">Clear</button>
                <button class="btn btn-primary" style="padding:0.375rem 0.75rem;font-size:0.75rem" onclick="searchIdentities()">Search</button>
              </div>
            </div>
          </div>
        </th>
        <th class="sortable ${bucketsSortClass}" onclick="toggleSubSort('buckets')">Buckets ${sortIcon}</th>
        <th class="sortable ${dateSortClass}" onclick="toggleSubSort('lastchanged')">Last Changed ${sortIcon}</th>
        <th style="width:50px"></th>
      `;
      body.innerHTML = '';

      let url = `/api/admin/identities?page=${subCurrentPage}&pageSize=${subPageSize}&sortBy=${subSortBy}&sortDir=${subSortDir}`;
      if (subSearchQuery) url += `&search=${encodeURIComponent(subSearchQuery)}&searchType=${subSearchType}`;

      const result = await apiRequest(url);
      if (!result.ok) return;

      const data = result.data;
      subTotalRecords = data.total;

      if (data.records.length === 0) {
        const msg = subSearchQuery ? `No identities matching "${sanitize(subSearchQuery)}"` : 'No consent records found across any bucket';
        body.innerHTML = `<tr><td colspan="4" style="text-align:center;padding:3rem;color:hsl(var(--muted-foreground))">${msg}</td></tr>`;
      } else {
        body.innerHTML = data.records.map((id, idx) => {
          const idDisplay = `<span class="tooltip-wrapper" ondblclick="event.stopPropagation();copyTextNow('${sanitize(id.emailHash)}')"><span class="email-hash">${sanitize(id.emailHash.substring(0, 16))}...</span><span class="tooltip">Double-click to copy ID</span></span>`;
          return `
            <tr style="cursor:pointer" onclick="showIdentityDetails('${sanitize(id.emailHash)}')">
              <td>${idDisplay}</td>
              <td>${id.bucketCount} bucket${id.bucketCount !== 1 ? 's' : ''}</td>
              <td class="col-link" onclick="event.stopPropagation();showAuditForIdentity('${sanitize(id.emailHash)}')" title="View audit">${sanitize(formatDate(id.lastChanged))}</td>
              <td>
                <div class="row-actions">
                  <span class="tooltip-wrapper">
                    <button class="btn-actions" onclick="event.stopPropagation();toggleRowMenu(event, 'sid-${idx}')">:</button>
                    <span class="tooltip tooltip-above tooltip-right">Actions</span>
                  </span>
                  <div class="dropdown-menu" id="rowMenu-sid-${idx}">
                    <button class="dropdown-item" onclick="showIdentityDetails('${sanitize(id.emailHash)}')">View Details</button>
                    <button class="dropdown-item" onclick="showAuditForIdentity('${sanitize(id.emailHash)}')">View Audit</button>
                  </div>
                </div>
              </td>
            </tr>
          `;
        }).join('');
      }
      updateSubPagination();
    }

    async function showIdentityDetails(emailHash, pushState = true) {
      // Update chrome synchronously before the fetch so there is no flash of stale content
      document.getElementById('subTitle').textContent = 'Identity Details';
      const badge = document.getElementById('subBadge');
      badge.dataset.hash = emailHash;
      delete badge.dataset.email;
      badge.classList.add('tooltip-wrapper');
      badge.style.cursor = 'pointer';
      badge.onclick = () => copyText(badge.dataset.email || badge.dataset.hash);
      badge.ondblclick = () => copyTextNow(badge.dataset.hash);
      badge.innerHTML = `<span class="email-hash" style="color:inherit">${sanitize(emailHash.substring(0, 16))}...</span><span class="tooltip">Click to copy · double-click for ID</span>`;
      document.getElementById('subBackBtn').style.display = 'inline-block';
      document.getElementById('subRefreshWrapper').style.display = 'none';
      document.querySelector('#view-subscriptions .pagination-container').style.display = 'none';

      // Clear cached data and render the detail headers with a blank body (no stale rows)
      subDetailSubscriptions = [];
      subDetailEmail = null;
      subDetailHash = emailHash;
      subDetailLoaded = false;
      renderDetailSubscriptions();

      const result = await apiRequest(`/api/admin/identities/${encodeURIComponent(emailHash)}`);
      if (!result.ok) return;

      if (pushState) syncSubUrl(emailHash);

      const details = result.data;

      // Now that we have the email, update the badge label and wire copy target
      if (details.email) {
        badge.dataset.email = details.email;
        badge.innerHTML = `<span style="color:inherit">${sanitize(details.email)}</span><span class="tooltip">Click to copy · double-click for ID</span>`;
      }

      subDetailSubscriptions = details.subscriptions;
      subDetailEmail = details.email || null;
      subDetailLoaded = true;
      renderDetailSubscriptions();
    }

    // OVERVIEW
    let webhookBuckets = new Set();

    async function loadOverview(refresh = false) {
      if (refresh) {
        const result = await apiRequest('/api/admin/buckets');
        if (!result.ok) return;
        buckets = result.data || [];
        renderBucketsSidebar();
      }

      // Fetch which buckets have webhooks configured
      const whResult = await apiRequest('/api/admin/webhooks/buckets');
      if (whResult.ok) {
        webhookBuckets = new Set(whResult.data || []);
      }

      // Fetch errors for webhook-enabled buckets
      const webhookErrorBuckets = new Set();
      overviewErrorsCache = [];
      if (webhookBuckets.size > 0) {
        const errorChecks = await Promise.all(
          [...webhookBuckets].map(async name => {
            const r = await apiRequest(`/api/admin/buckets/${encodeURIComponent(name)}/webhook/errors`);
            const errors = r.ok && r.data ? r.data : [];
            return { name, errors };
          })
        );
        errorChecks.forEach(c => {
          if (c.errors.length > 0) {
            webhookErrorBuckets.add(c.name);
            c.errors.forEach(e => overviewErrorsCache.push({ bucket: c.name, ...e }));
          }
        });
      }
      const overviewTrigger = document.getElementById('overviewWebhookErrorsTrigger');
      if (overviewTrigger) overviewTrigger.classList.toggle('has-errors', overviewErrorsCache.length > 0);

      const body = document.getElementById('overviewBody');

      if (buckets.length === 0) {
        body.innerHTML = '<tr><td colspan="4" style="text-align:center;padding:3rem;color:hsl(var(--muted-foreground))">No buckets yet! Buckets are created automatically when a token makes its first API call.</td></tr>';
        return;
      }

      body.innerHTML = buckets.map((b, idx) => `
        <tr>
          <td style="cursor:pointer" onclick="showBucket('${sanitize(b.name)}')"><strong>${sanitize(b.name) || 'N/A'}</strong>${b.isArchived ? ' <span class="status-badge" style="background:hsl(var(--muted));color:hsl(var(--muted-foreground));font-size:0.7rem">Archived</span>' : ''}</td>
          <td>${b.totalEmails != null ? Number(b.totalEmails).toLocaleString() : 'N/A'}</td>
          <td>${(b.permissions || []).map(p => `<span class="tooltip-wrapper select-none" style="cursor:pointer" onclick="copyTextNow('${sanitize(p)}')"><span class="status-badge">${sanitize(formatPermission(p))}</span><span class="tooltip">${sanitize(p)}</span></span>`).join(' ') || 'None'}</td>
          <td class="col-actions">
            <div class="row-actions">
              <span class="row-status-icons">
                ${webhookErrorBuckets.has(b.name) ? `<span class="tooltip-wrapper">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="hsl(var(--destructive))" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="vertical-align:middle">
                    <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"></path>
                    <path d="M13.73 21a2 2 0 0 1-3.46 0"></path>
                  </svg>
                  <span class="tooltip tooltip-above tooltip-right">Errors have occurred. Action required!</span>
                </span>` : ''}
                ${webhookBuckets.has(b.name) ? `<span class="tooltip-wrapper">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="hsl(var(--muted-foreground))" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="vertical-align:middle">
                    <path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"/>
                    <path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"/>
                  </svg>
                  <span class="tooltip tooltip-above tooltip-right">Webhook configured</span>
                </span>` : ''}
              </span>
              <span class="tooltip-wrapper">
                <button class="btn-actions" onclick="toggleOverviewMenu(event, ${idx})">:</button>
                <span class="tooltip tooltip-above tooltip-right">Actions</span>
              </span>
              <div class="dropdown-menu" id="overviewMenu-${idx}">
                <button class="dropdown-item" onclick="showBucket('${sanitize(b.name)}')">View Records</button>
                <button class="dropdown-item" onclick="${b.isArchived ? `showUnarchiveModal('${sanitize(b.name)}')` : `showArchiveModal('${sanitize(b.name)}')`}">${b.isArchived ? 'Unarchive Bucket' : 'Archive Bucket'}</button>
                <button class="dropdown-item" onclick="initiateBucketRemoval('${sanitize(b.name)}')">Remove Bucket</button>
              </div>
            </div>
          </td>
        </tr>
      `).join('');
    }

    let currentBucketPermissions = [];

    async function copyBucketName() {
        const tooltipElement = document.getElementById('bucketTooltip');
        const bucketName = tooltipElement.innerText;

        if (!bucketName || bucketName === "-") {
            console.error("Source text is empty or invalid.");
            return;
        }

        try {
            // Utilizing the Asynchronous Clipboard API
            await clipboardWrite(bucketName);
            
            // Optional: Provide visual confirmation
            const originalText = tooltipElement.innerText;
            tooltipElement.innerText = "Copied!";
            
            setTimeout(() => {
                tooltipElement.innerText = originalText;
            }, 2000);
            
        } catch (err) {
            console.error("Failed to copy text: ", err);
        }
    }

    // BUCKET RECORDS
    async function loadBucket(bucket) {
      const thead = document.getElementById('bucketTableHead');
      const body = document.getElementById('bucketBody');

      const detailsResult = await apiRequest(`/api/admin/buckets/${encodeURIComponent(bucket)}`);
      if (!detailsResult.ok) return;

      const details = detailsResult.data;
      currentBucketPermissions = details.permissions || [];
      currentBucketArchived = details.isArchived || false;
      const badge = document.getElementById('readOnlyBadge');
      if (currentBucketArchived) {
        badge.textContent = 'This bucket is archived';
        badge.style.background = 'hsl(var(--muted))';
        badge.style.color = 'hsl(var(--muted-foreground))';
      } else {
        badge.textContent = 'Read-Only';
        badge.style.background = '';
        badge.style.color = '';
      }

      // Fetch webhook errors for this bucket
      loadWebhookErrors(bucket);

      const emailSortClass = sortBy === 'email' ? (sortDir === 'asc' ? 'asc' : 'desc') : '';
      const dateSortClass = sortBy === 'lastchanged' ? (sortDir === 'asc' ? 'asc' : 'desc') : '';
      const sortIcon = '<span class="sort-icon">▲</span>';
      const hasSearch = searchQuery ? 'has-search' : '';

      thead.innerHTML = `
        <th>
          <div class="column-search">
            <span class="sortable ${emailSortClass}" onclick="toggleSort('email')" style="cursor:pointer">Email ${sortIcon}</span>
            <button class="search-trigger ${hasSearch}" onclick="toggleSearchPopover(event)" title="Search">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <circle cx="11" cy="11" r="8"></circle>
                <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
              </svg>
            </button>
            <div class="search-popover" id="searchPopover">
              <div class="search-type-toggle">
                <button class="search-type-btn ${searchType === 'id' ? 'active' : ''}" onclick="event.stopPropagation();setSearchType('id')">By ID</button>
                <button class="search-type-btn ${searchType === 'email' ? 'active' : ''}" onclick="event.stopPropagation();setSearchType('email')">By Email</button>
              </div>
              <label>${searchType === 'id' ? 'Enter ID from logs (e.g., a1b2c3d4e5f6)' : 'Enter email (partial match)'}</label>
              <input type="text" id="searchInput" placeholder="${searchType === 'id' ? 'e.g., a1b2c3d4e5f6' : 'e.g., @gmail.com'}" value="${sanitize(searchQuery)}" onkeydown="if(event.key==='Enter'){searchRecords();toggleSearchPopover(event);}">
              <div class="search-actions">
                <button class="btn btn-outline" style="padding:0.375rem 0.75rem;font-size:0.75rem" onclick="clearSearch()">Clear</button>
                <button class="btn btn-primary" style="padding:0.375rem 0.75rem;font-size:0.75rem" onclick="searchRecords();toggleSearchPopover(event)">Search</button>
              </div>
            </div>
          </div>
        </th>
        ${currentBucketPermissions.map(p => `<th><span class="tooltip-wrapper select-none" style="cursor:pointer" onclick="copyTextNow('${sanitize(p)}')">${sanitize(formatPermission(p))}<span class="tooltip">${sanitize(p)}</span></span></th>`).join('')}
        <th class="sortable ${dateSortClass}" onclick="toggleSort('lastchanged')">Last Changed ${sortIcon}</th>
        <th style="width:50px"></th>
      `;

      let url = `/api/admin/buckets/${encodeURIComponent(bucket)}/records?page=${currentPage}&pageSize=${pageSize}&sortBy=${sortBy}&sortDir=${sortDir}`;
      if (searchQuery) {
        url += `&search=${encodeURIComponent(searchQuery)}&searchType=${searchType}`;
      }
      const recordsResult = await apiRequest(url);
      if (!recordsResult.ok) return;

      const data = recordsResult.data;
      totalRecords = data.total;

      if (data.records.length === 0) {
        const noResultsMsg = searchQuery
          ? `No records matching "${sanitize(searchQuery)}", please try a different identifier or search type`
          : 'No consent records yet! They appear here after the first API call for this bucket';
        body.innerHTML = `<tr><td colspan="${currentBucketPermissions.length + 3}" style="text-align:center;padding:3rem;color:hsl(var(--muted-foreground))">${noResultsMsg}</td></tr>`;
      } else {
        body.innerHTML = data.records.map((r, idx) => {
          const switchClass = currentBucketArchived ? 'switch disabled archived' : 'switch disabled';
          const permCells = currentBucketPermissions.map(p => {
            const isOptedIn = r.permissions && r.permissions[p];
            return `<td><span class="tooltip-wrapper"><label class="${switchClass}"><input type="checkbox" ${isOptedIn ? 'checked' : ''} disabled><span class="slider"></span></label><span class="tooltip">${isOptedIn ? 'Opted-in' : 'Opted-out'}</span></span></td>`;
          }).join('');

          const emailId = r.emailHash ? sanitize(r.emailHash.substring(0, 12)) : '';
          const emailDisplay = r.email
            ? `<span class="tooltip-wrapper"><span class="email-text select-none" style="cursor:pointer" onclick="copyText('${sanitize(r.email)}')" ondblclick="copyTextNow('${emailId}')">${sanitize(r.email)}</span><span class="tooltip">Click to copy &middot; double-click for ID</span></span>`
            : `<span class="email-hash" title="${sanitize(r.emailHash || '')}">${r.emailHash ? sanitize(r.emailHash.substring(0, 16)) + '...' : 'N/A'}</span>`;

          const recordData = encodeURIComponent(JSON.stringify({
            email: r.email,
            emailHash: r.emailHash,
            permissions: r.permissions || {},
            customFields: r.customFields || {}
          }));

          return `
            <tr>
              <td>${emailDisplay}</td>
              ${permCells}
              <td>${sanitize(formatDate(r.lastChanged))}</td>
              <td>
                <div class="row-actions"${currentBucketArchived ? ' style="opacity:0.4;pointer-events:none"' : ''}>
                  <span class="tooltip-wrapper">
                    <button class="btn-actions" onclick="toggleRowMenu(event, ${idx})"${currentBucketArchived ? ' disabled' : ''}>:</button>
                    <span class="tooltip tooltip-above tooltip-right">${currentBucketArchived ? 'Actions disabled (archived)' : 'Actions'}</span>
                  </span>
                  <div class="dropdown-menu" id="rowMenu-${idx}">
                    <button class="dropdown-item" onclick="openEditPermissions('${recordData}')">Edit Permissions</button>
                    <button class="dropdown-item" onclick="openOptOutPage('${recordData}')">Open Consent Page <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="margin-left:auto;flex-shrink:0"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"></path><polyline points="15 3 21 3 21 9"></polyline><line x1="10" y1="14" x2="21" y2="3"></line></svg></button>
                    <button class="dropdown-item" onclick="showAuditForBucketAndIdentity('${sanitize(currentBucket)}', '${sanitize(r.emailHash)}')">View Audit</button>
                  </div>
                </div>
              </td>
            </tr>
          `;
        }).join('');
      }

      updatePagination();
    }

    function updatePagination() {
      const totalPages = Math.ceil(totalRecords / pageSize) || 1;
      const start = totalRecords === 0 ? 0 : (currentPage - 1) * pageSize + 1;
      const end = Math.min(currentPage * pageSize, totalRecords);

      document.getElementById('paginationInfo').textContent = `Showing ${start.toLocaleString()} to ${end.toLocaleString()} of ${totalRecords.toLocaleString()} entries`;
      document.getElementById('prevBtn').disabled = currentPage === 1;
      document.getElementById('nextBtn').disabled = currentPage >= totalPages;
    }

    function updatePageSize() {
      pageSize = parseInt(document.getElementById('pageSize').value);
      currentPage = 1;
      if (currentBucket) { syncBucketUrl(true); loadBucket(currentBucket); }
    }

    function changePage(delta) {
      currentPage += delta;
      if (currentBucket) { syncBucketUrl(true); loadBucket(currentBucket); }
    }

    function toggleSort(column) {
      if (sortBy === column) {
        sortDir = sortDir === 'asc' ? 'desc' : 'asc';
      } else {
        sortBy = column;
        sortDir = column === 'lastchanged' ? 'desc' : 'asc';
      }
      localStorage.setItem('beacon_sort_prefs', JSON.stringify({ sortBy, sortDir }));
      currentPage = 1;
      if (currentBucket) { syncBucketUrl(true); loadBucket(currentBucket); }
    }

    function toggleSearchPopover(event) {
      event.stopPropagation();
      const popover = document.getElementById('searchPopover');
      const trigger = event.currentTarget.closest('.column-search')?.querySelector('.search-trigger') || event.currentTarget;
      popover.classList.toggle('open');
      trigger.classList.toggle('active', popover.classList.contains('open'));
      if (popover.classList.contains('open')) {
        setTimeout(() => document.getElementById('searchInput')?.focus(), 50);
      }
    }

    function setSearchType(type) {
      searchType = type;
      // Update toggle, label, placeholder in-place, no re-render or data refetch
      document.querySelectorAll('#searchPopover .search-type-btn').forEach(btn => {
        btn.classList.toggle('active', btn.textContent.trim() === (type === 'id' ? 'By ID' : 'By Email'));
      });
      const label = document.querySelector('#searchPopover label');
      if (label) label.textContent = type === 'id' ? 'Enter ID from logs (e.g., a1b2c3d4e5f6)' : 'Enter email (partial match)';
      const input = document.getElementById('searchInput');
      if (input) { input.placeholder = type === 'id' ? 'e.g., a1b2c3d4e5f6' : 'e.g., @gmail.com'; input.focus(); }
    }

    function searchRecords() {
      const input = document.getElementById('searchInput');
      searchQuery = input ? input.value.trim() : '';
      currentPage = 1;
      if (currentBucket) { syncBucketUrl(true); loadBucket(currentBucket); }
    }

    function clearSearch() {
      const input = document.getElementById('searchInput');
      if (input) input.value = '';
      searchQuery = '';
      currentPage = 1;
      if (currentBucket) { syncBucketUrl(true); loadBucket(currentBucket); }
    }

    // Close search popover when clicking outside
    document.addEventListener('click', (e) => {
      const popover = document.getElementById('searchPopover');
      const trigger = document.querySelector('.search-trigger');
      if (popover && !popover.contains(e.target) && !trigger?.contains(e.target)) {
        popover.classList.remove('open');
        trigger?.classList.remove('active');
      }
    });

    // BUCKET AUTOCOMPLETE
    let selectedAutocompleteIndex = -1;

    function showBucketSuggestions() {
      const input = document.getElementById('tokenBucket');
      const dropdown = document.getElementById('bucketAutocomplete');
      const query = input.value.trim().toLowerCase();
      hidePermissionSuggestions();

      // Filter buckets that match the query
      const matches = buckets.filter(b =>
        b.name.toLowerCase().includes(query)
      );

      if (matches.length === 0 || (matches.length === 1 && matches[0].name.toLowerCase() === query)) {
        dropdown.classList.remove('open');
        return;
      }

      selectedAutocompleteIndex = -1;
      dropdown.innerHTML = matches.slice(0, 10).map((b, idx) => `
        <div class="autocomplete-item" data-index="${idx}" onclick="selectBucket('${sanitize(b.name)}')" onmouseenter="highlightAutocomplete(${idx})">
          <div class="bucket-name">${sanitize(b.name)}</div>
          <div class="bucket-info">${Number(b.totalEmails || 0).toLocaleString()} records · ${(b.permissions || []).length} permissions</div>
        </div>
      `).join('');

      positionDropdown(input, dropdown);
      dropdown.classList.add('open');
    }

    async function selectBucket(name) {
      document.getElementById('tokenBucket').value = name;
      document.getElementById('bucketAutocomplete').classList.remove('open');
      selectedAutocompleteIndex = -1;
      tokenPermissions.clear();
      removedPermissions.clear();
      tokenDefaultOptOut = false;

      // If global double opt-in is on, check whether this bucket has it enabled
      if (appSettings.enableDoubleOptIn) {
        try {
          const r = await apiRequest(`/api/admin/buckets/${encodeURIComponent(name)}/options`);
          if (r.ok && r.data) {
            tokenDefaultOptOut = r.data.doubleOptIn ?? true;
          }
        } catch {}
      }

      // Show/hide confirmation email notice + skip-email toggle
      document.getElementById('tokenDoubleOptInNotice').style.display = tokenDefaultOptOut ? 'flex' : 'none';
      document.getElementById('tokenSkipEmailGroup').style.display = tokenDefaultOptOut ? 'block' : 'none';
      if (tokenDefaultOptOut) document.getElementById('tokenSendConfirmation').checked = true;

      renderPermissionsGrid();
    }

    function hideBucketSuggestions() {
      document.getElementById('bucketAutocomplete').classList.remove('open');
      selectedAutocompleteIndex = -1;
    }

    function highlightAutocomplete(index) {
      const items = document.querySelectorAll('#bucketAutocomplete .autocomplete-item');
      items.forEach((item, i) => {
        item.classList.toggle('selected', i === index);
      });
      selectedAutocompleteIndex = index;
    }

    // When the bucket field loses focus without an autocomplete selection, treat it the same as
    // selecting from the dropdown: normalize the name, clear stale permissions, check double opt-in.
    document.getElementById('tokenBucket')?.addEventListener('blur', function() {
      const name = this.value.trim().toLowerCase().replace(/\s+/g, '_').replace(/[^a-z0-9_-]/g, '');
      if (!name) return;
      selectBucket(name);
    });

    // Keyboard navigation for autocomplete
    document.getElementById('tokenBucket')?.addEventListener('keydown', function(e) {
      const dropdown = document.getElementById('bucketAutocomplete');
      if (!dropdown.classList.contains('open')) return;

      const items = dropdown.querySelectorAll('.autocomplete-item');
      if (items.length === 0) return;

      if (e.key === 'ArrowDown') {
        e.preventDefault();
        selectedAutocompleteIndex = Math.min(selectedAutocompleteIndex + 1, items.length - 1);
        highlightAutocomplete(selectedAutocompleteIndex);
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        selectedAutocompleteIndex = Math.max(selectedAutocompleteIndex - 1, 0);
        highlightAutocomplete(selectedAutocompleteIndex);
      } else if (e.key === 'Enter' && selectedAutocompleteIndex >= 0) {
        e.preventDefault();
        const selectedItem = items[selectedAutocompleteIndex];
        if (selectedItem) {
          const name = selectedItem.querySelector('.bucket-name').textContent;
          selectBucket(name);
        }
      } else if (e.key === 'Escape') {
        hideBucketSuggestions();
      }
    });

    // Close autocomplete when clicking outside their wrapper
    document.addEventListener('click', function(e) {
      const bucketWrapper = document.getElementById('bucketAutocomplete')?.parentElement;
      const permWrapper = document.getElementById('permissionAutocomplete')?.parentElement;
      const cronWrapper = document.getElementById('cronAutocomplete')?.parentElement;
      if (bucketWrapper && !bucketWrapper.contains(e.target)) {
        hideBucketSuggestions();
      }
      if (permWrapper && !permWrapper.contains(e.target)) {
        hidePermissionSuggestions();
      }
      if (cronWrapper && !cronWrapper.contains(e.target)) {
        hideCronSuggestions();
      }
    });

    // NEW BUCKET
    let newBucketPerms = [];

    function renderNewBucketPerms() {
      const container = document.getElementById('newBucketPermsList');
      if (newBucketPerms.length === 0) {
        container.innerHTML = '<div style="color:hsl(var(--muted-foreground));font-size:0.875rem;padding:0.5rem 0">No permissions yet! You can add one below.</div>';
        return;
      }
      container.innerHTML = newBucketPerms.map(p => `
        <div class="permission-row" data-perm="${sanitize(p)}">
          <span class="permission-name">${sanitize(formatPermission(p))}</span>
          <button type="button" class="btn-remove" onclick="removeNewBucketPerm('${sanitize(p)}')" title="Remove">&times;</button>
        </div>
      `).join('');
    }

    function addNewBucketPerm() {
      const input = document.getElementById('createBucketPermInput');
      const perm = input.value.trim().toLowerCase().replace(/\s+/g, '_').replace(/[^a-z0-9_-]/g, '');
      if (!perm) { notify('warning', 'Invalid', 'Please enter a permission name'); return; }
      if (newBucketPerms.includes(perm)) { notify('warning', 'Duplicate', 'Permission already added'); return; }
      newBucketPerms.push(perm);
      input.value = '';
      renderNewBucketPerms();
    }

    function removeNewBucketPerm(perm) {
      newBucketPerms = newBucketPerms.filter(p => p !== perm);
      renderNewBucketPerms();
    }

    async function createNewBucket() {
      const name = document.getElementById('newBucketName').value.trim().toLowerCase().replace(/\s+/g, '_').replace(/[^a-z0-9_-]/g, '');
      if (!name) { notify('warning', 'Missing Name', 'Please enter a bucket name'); return; }
      if (newBucketPerms.length === 0) { notify('warning', 'No Permissions', 'Add at least one permission'); return; }
      if (buckets.some(b => b.name === name)) { notify('warning', 'Already Exists', `Bucket "${name}" already exists`); return; }

      let ok = true;
      for (const perm of newBucketPerms) {
        const result = await apiRequest(`/api/admin/buckets/${encodeURIComponent(name)}/permissions`, {
          method: 'POST',
          body: { permission: perm }
        });
        if (!result.ok) { ok = false; break; }
      }

      if (ok) {
        notify('success', 'Bucket Created', `"${name}" created with ${newBucketPerms.length} permission(s)`);
        newBucketPerms = [];
        document.getElementById('newBucketName').value = '';
        renderNewBucketPerms();
        await loadBuckets();
        showBucket(name);
      }
    }

    // TOKEN GENERATION
    function handleGenerateToken() {
      const btn = document.getElementById('generateTokenBtn');
      if (btn.textContent === 'Create new token') {
        clearTokenForm();
      } else {
        generateToken().catch(err => {
          console.error('Token generation failed:', err);
          notify('error', 'Token Generation Failed', 'An unexpected error occurred. Check the server logs.');
          document.getElementById('generateTokenBtn').disabled = false;
          document.getElementById('generateTokenBtn').textContent = 'Generate Token';
        });
      }
    }

    function newTokenForBucket(bucket) {
      clearTokenForm();
      showView('new-token');
      selectBucket(bucket);
    }

    function clearTokenForm() {
      document.getElementById('tokenBucket').value = '';
      document.getElementById('tokenEmail').value = '';
      document.getElementById('tokenExpiry').value = DEFAULT_EXPIRY_DAYS;
      document.getElementById('tokenAllowReplay').checked = true;
      document.getElementById('tokenLanguage').value = appSettings.uiLanguage || 'en';
      document.getElementById('tokenOutputWrapper').style.display = 'none';
      document.getElementById('tokenOutput').textContent = '';
      document.getElementById('generateTokenBtn').textContent = 'Generate Token';
      document.getElementById('viewTokenBucketBtn').style.display = 'none';
      document.getElementById('tokenDoubleOptInNotice').style.display = 'none';
      document.getElementById('tokenSkipEmailGroup').style.display = 'none';
      document.getElementById('tokenSendConfirmation').checked = true;
      document.getElementById('tokenFormFields').classList.remove('frozen');

      tokenPermissions.clear();
      removedPermissions.clear();
      tokenDefaultOptOut = false;
      renderPermissionsGrid();

      tokenCustomFields = {};
      renderTokenCustomFieldsDisplay();
    }

    async function generateToken() {
      const bucket = document.getElementById('tokenBucket').value.trim().toLowerCase().replace(/\s+/g, '_').replace(/[^a-z0-9_-]/g, '');
      if (!bucket) {
        notify('warning', 'Missing Field', 'Please enter a bucket name');
        return;
      }

      const email = document.getElementById('tokenEmail').value.trim();
      if (!email) {
        notify('warning', 'Missing Field', 'Please enter an email address');
        return;
      }

      const permissions = getPermissionStates();
      if (Object.keys(permissions).length === 0) {
        notify('warning', 'No Permissions', 'Please set at least one permission to In or Out');
        return;
      }

      const btn = document.getElementById('generateTokenBtn');
      btn.disabled = true;

      try {
        // Check if email already exists in this bucket
        const checkResult = await apiRequest(`/api/admin/buckets/${encodeURIComponent(bucket)}/check-email`, {
          method: 'POST',
          body: { email }
        });

        if (checkResult.ok && checkResult.data?.exists) {
          notify('error', 'Duplicate Email', `This email address already exists in bucket "${bucket}"`);
          return;
        }

        const expiryDays = parseInt(document.getElementById('tokenExpiry').value) || DEFAULT_EXPIRY_DAYS;
        const allowReplay = document.getElementById('tokenAllowReplay').checked;
        const language = document.getElementById('tokenLanguage').value;
        const customFields = Object.keys(tokenCustomFields).length > 0 ? tokenCustomFields : undefined;
        const skipConfirmationEmail = !document.getElementById('tokenSendConfirmation').checked;

        const result = await apiRequest('/api/tokens/generate', {
          method: 'POST',
          body: [{ bucket, email, permissions, expiryDays, allowReplay, language, customFields, skipConfirmationEmail }]
        });

        if (result.ok) {
          const tokenUrl = PUBLIC_URL ? `${PUBLIC_URL}/u/${result.data[0].token}` : `${window.location.origin}/u/${result.data[0].token}`;
          document.getElementById('tokenOutput').textContent = tokenUrl;
          document.getElementById('tokenOutputWrapper').style.display = 'block';
          document.getElementById('tokenOutput').style.display = 'block';
          document.getElementById('viewTokenBucketBtn').style.display = '';
          document.getElementById('tokenOutputWrapper').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
          btn.textContent = 'Create new token';

          // Use the server-confirmed doubleOptIn flag to determinee whether to show the confirmation email notice, not the client-side bucket setting which may be stalee
          const doubleOptInConfirmed = result.data[0].doubleOptIn ?? tokenDefaultOptOut;
          tokenDefaultOptOut = doubleOptInConfirmed;
          const hasOptIn = Object.values(permissions).some(v => v === true);
          document.getElementById('tokenDoubleOptInNotice').style.display = (doubleOptInConfirmed && hasOptIn) ? 'flex' : 'none';
          document.getElementById('tokenSkipEmailGroup').style.display = 'none';

          const toastMsg = (doubleOptInConfirmed && hasOptIn)
            ? 'Token generated and confirmation email queued.'
            : 'The token has been created successfully.';
          notify('success', 'Token Generated', toastMsg);

          // Freeze the form fields until the user starts a new token
          document.getElementById('tokenFormFields').classList.add('frozen');

          loadBuckets();
        } else { notify('error', 'Token Generation Failed', result.data?.error || 'Failed to generate token.'); }
      } finally {
        btn.disabled = false;
      }
    }

    function openTokenBucketView() {
      const bucket = document.getElementById('tokenBucket').value.trim().toLowerCase().replace(/\s+/g, '_').replace(/[^a-z0-9_-]/g, '');
      const email = document.getElementById('tokenEmail').value.trim();
      if (!bucket) { notify('warning', 'Missing Field', 'Please enter a bucket name first'); return; }
      const params = new URLSearchParams({ view: 'bucket', bucket });
      if (email) { params.set('q', email); params.set('qtype', 'email'); }
      window.location.href = `${window.location.pathname}?${params}`;
    }

    function copyTokenLink() {
      const text = document.getElementById('tokenOutput').textContent;
      clipboardWrite(text);
      notify('success', 'Copied', 'Token URL copied to clipboard');
    }

    let copyClickTimer = null;

    function copyLabel(text) {
      return text.length > 24 ? text.slice(0, 16) + '…' : text;
    }

    function copyText(text) {
      clearTimeout(copyClickTimer);
      copyClickTimer = setTimeout(() => {
        clipboardWrite(text);
        notify('success', 'Copied', copyLabel(text));
      }, 250);
    }

    function copyTextNow(text) {
      clearTimeout(copyClickTimer);
      clipboardWrite(text);
      notify('success', 'Copied', copyLabel(text));
    }

    // HELPERS
    async function clipboardWrite(text) {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        return navigator.clipboard.writeText(text);
      }
      const ta = document.createElement('textarea');
      ta.value = text;
      ta.style.position = 'fixed';
      ta.style.left = '-9999px';
      document.body.appendChild(ta);
      ta.select();
      document.execCommand('copy');
      document.body.removeChild(ta);
    }

    function formatPermission(p) {
      if (!p) return '';
      // Convert snake_case or kebab-case to Title Case
      return p
        .replace(/[-_]/g, ' ')
        .replace(/\b\w/g, c => c.toUpperCase());
    }

    function formatDate(dateStr) {
      if (!dateStr) return 'N/A';
      // Server stores UTC but may serialize without Z suffix to ensure UTC parsing
      if (!dateStr.endsWith('Z') && !dateStr.includes('+') && !dateStr.includes('-', 10)) dateStr += 'Z';
      const date = new Date(dateStr);
      if (isNaN(date.getTime())) return 'N/A';
      return date.toLocaleString(undefined, {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit'
      });
    }

    // REMOVE BUCKET
    let generatedPassphrase = '';
    let bucketToRemove = '';

    // ARCHIVE
    let archiveTargetBucket = '';

    function showArchiveModal(bucket) {
      archiveTargetBucket = bucket;
      document.getElementById('archiveBucketName').textContent = bucket;
      document.getElementById('archiveModal').style.display = 'flex';
      closeAllMenus();
    }

    function closeArchiveModal() {
      document.getElementById('archiveModal').style.display = 'none';
      archiveTargetBucket = '';
    }

    async function confirmArchive() {
      if (!archiveTargetBucket) return;
      const bucket = archiveTargetBucket;
      const result = await apiRequest(`/api/admin/buckets/${encodeURIComponent(bucket)}/archive`, { method: 'POST' });
      closeArchiveModal();
      if (result.ok) {
        notify('success', 'Archived', `Bucket "${bucket}" has been archived`);
        if (currentBucket === bucket) {
          currentBucketArchived = true;
          const badge = document.getElementById('readOnlyBadge');
          badge.textContent = 'This bucket is archived';
          badge.style.background = 'hsl(var(--muted))';
          badge.style.color = 'hsl(var(--muted-foreground))';
          loadBucket(currentBucket);
        }
        await loadOverview(true);
      } else {
        notify('error', 'Archive Failed', result.data?.error || 'Failed to archive bucket.');
      }
    }

    function showUnarchiveModal(bucket) {
      archiveTargetBucket = bucket;
      document.getElementById('unarchiveBucketName').textContent = bucket;
      document.getElementById('unarchiveModal').style.display = 'flex';
      closeAllMenus();
    }

    function closeUnarchiveModal() {
      document.getElementById('unarchiveModal').style.display = 'none';
      archiveTargetBucket = '';
    }

    async function confirmUnarchive() {
      if (!archiveTargetBucket) return;
      const bucket = archiveTargetBucket;
      const result = await apiRequest(`/api/admin/buckets/${encodeURIComponent(bucket)}/unarchive`, { method: 'POST' });
      closeUnarchiveModal();
      if (result.ok) {
        notify('success', 'Unarchived', `Bucket "${bucket}" has been unarchived`);
        if (currentBucket === bucket) {
          currentBucketArchived = false;
          const badge = document.getElementById('readOnlyBadge');
          badge.textContent = 'Read-Only';
          badge.style.background = '';
          badge.style.color = '';
          loadBucket(currentBucket);
        }
        await loadOverview(true);
      } else {
        notify('error', 'Unarchive Failed', result.data?.error || 'Failed to unarchive bucket.');
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
      bucketToRemove = currentBucket;
      showRemoveModal();
    }

    function initiateBucketRemoval(bucket) {
      bucketToRemove = bucket;
      showRemoveModal();
      closeAllMenus();
    }

    function showRemoveModal() {
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
      generatedPassphrase = code.join(' ');

      document.getElementById('passphraseDisplay').textContent = generatedPassphrase;
      document.getElementById('passphraseInput').value = '';
      document.getElementById('confirmRemoveBtn').classList.remove('active');
      document.getElementById('removeModal').style.display = 'flex';
    }

    function verifyPassphrase() {
      const input = document.getElementById('passphraseInput').value.toUpperCase().trim();
      const btn = document.getElementById('confirmRemoveBtn');

      if (input === generatedPassphrase) {
        btn.classList.add('active');
      } else {
        btn.classList.remove('active');
      }
    }

    async function confirmRemoval() {
      if (!bucketToRemove) return;

      const result = await apiRequest(`/api/admin/buckets/${encodeURIComponent(bucketToRemove)}`, {
        method: 'DELETE'
      });

      if (result.ok) {
        notify('success', 'Bucket Removed', `Successfully deleted bucket "${bucketToRemove}"`);
        closeRemoveModal();
        await loadBuckets();
        showView('overview');
      } else { notify('error', 'Delete Failed', result.data?.error || 'Failed to delete bucket.'); }
    }

    function closeRemoveModal() {
      document.getElementById('removeModal').style.display = 'none';
      generatedPassphrase = '';
      bucketToRemove = '';
    }

    // BUCKET PERMISSIONS MANAGEMENT
    let bucketPermsData = [];
    let permToRemove = '';
    let permRemovePassphrase = '';

    function renderBucketPermsGrid() {
      const container = document.getElementById('bucketPermsGrid');
      const badge = document.getElementById('bucketPermsBadge');
      badge.textContent = bucketPermsData.length;
      if (bucketPermsData.length > 0) {
        badge.classList.add('active');
      } else {
        badge.classList.remove('active');
      }

      if (bucketPermsData.length === 0) {
        container.innerHTML = '<div style="color:hsl(var(--muted-foreground));font-size:0.875rem;padding:1rem 0">No permissions yet! You can add one below or generate a token to create them automatically.</div>';
        return;
      }

      container.innerHTML = bucketPermsData.map(p => `
        <div class="permission-row" data-perm="${sanitize(p.permission)}">
          <span class="permission-name">${sanitize(formatPermission(p.permission))}</span>
          <span style="font-size:0.75rem;color:hsl(var(--muted-foreground));white-space:nowrap">${p.optedIn} in / ${p.optedOut} out</span>
          <button type="button" class="btn-remove" onclick="showPermRemoveModal('${sanitize(p.permission)}')" title="Remove">&times;</button>
        </div>
      `).join('');
    }

    async function loadBucketPerms() {
      if (!currentBucket) return;
      const result = await apiRequest(`/api/admin/buckets/${encodeURIComponent(currentBucket)}`);
      if (result.ok) {
        bucketPermsData = (result.data.stats || []).map(s => ({
          permission: s.permission,
          optedIn: s.optedIn,
          optedOut: s.optedOut
        }));
        renderBucketPermsGrid();
      }
    }

    async function addBucketPermission() {
      const input = document.getElementById('newBucketPermInput');
      const perm = input.value.trim().toLowerCase().replace(/\s+/g, '_').replace(/[^a-z0-9_-]/g, '');

      if (!perm) {
        notify('warning', 'Invalid Permission', 'Please enter a permission name');
        return;
      }

      if (bucketPermsData.some(p => p.permission === perm)) {
        notify('warning', 'Duplicate', 'Permission already exists in this bucket');
        return;
      }

      const result = await apiRequest(`/api/admin/buckets/${encodeURIComponent(currentBucket)}/permissions`, {
        method: 'POST',
        body: { permission: perm }
      });

      if (result.ok) {
        input.value = '';
        notify('success', 'Permission Added', `"${formatPermission(perm)}" added to bucket`);
        await loadBucketPerms();
        await loadBucket(currentBucket);
      } else { notify('error', 'Add Failed', result.data?.error || 'Failed to add permission.'); }
    }

    function showPermRemoveModal(perm) {
      permToRemove = perm;
      document.getElementById('permRemoveName').textContent = formatPermission(perm);

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
      permRemovePassphrase = code.join(' ');

      document.getElementById('permPassphraseDisplay').textContent = permRemovePassphrase;
      document.getElementById('permPassphraseInput').value = '';
      document.getElementById('confirmPermRemoveBtn').classList.remove('active');
      document.getElementById('permissionRemoveModal').style.display = 'flex';
      document.getElementById('permPassphraseInput').style.zIndex = 1000;
    }

    function verifyPermPassphrase() {
      const input = document.getElementById('permPassphraseInput').value.toUpperCase().trim();
      const btn = document.getElementById('confirmPermRemoveBtn');

      if (input === permRemovePassphrase) {
        btn.classList.add('active');
      } else {
        btn.classList.remove('active');
      }
    }

    async function confirmPermRemoval() {
      if (!permToRemove || !currentBucket) return;

      const result = await apiRequest(`/api/admin/buckets/${encodeURIComponent(currentBucket)}/permissions/${encodeURIComponent(permToRemove)}`, {
        method: 'DELETE'
      });

      if (result.ok) {
        notify('success', 'Permission Removed', `"${formatPermission(permToRemove)}" and all its records deleted`);
        closePermRemoveModal();
        await loadBucketPerms();
        // Refresh the main bucket view
        await loadBucket(currentBucket);
      } else { notify('error', 'Remove Failed', result.data?.error || 'Failed to remove permission.'); }
    }

    function closePermRemoveModal() {
      document.getElementById('permissionRemoveModal').style.display = 'none';
      permRemovePassphrase = '';
      permToRemove = '';
    }

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

      const result = await apiRequest(`/api/admin/buckets/${encodeURIComponent(bucket)}/override`, {
        method: 'POST',
        body: {
          email: editingRecord.email,
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
        const url = PUBLIC_URL ? `${PUBLIC_URL}/u/${result.data[0].token}` : `${window.location.origin}/u/${result.data[0].token}`;
        window.open(url, '_blank');
      } else { notify('error', 'Link Generation Failed', result.data?.error || 'Failed to generate opt-out link.'); }
    }

    function openShareUrlModal() {
      const sel = document.getElementById('shareUrlPermission');
      if (sel) {
        sel.innerHTML = '<option value="">All permissions</option>' +
          (currentBucketPermissions || []).map(p =>
            `<option value="${sanitize(p)}">${sanitize(formatPermission(p))}</option>`
          ).join('');
      }
      document.getElementById('shareUrlSource').value = '';
      document.getElementById('shareUrlMedium').value = '';
      const expEl = document.getElementById('shareUrlExpiry');
      if (expEl) expEl.value = '30';
      const statusEl = document.getElementById('exportTokensStatus');
      if (statusEl) statusEl.textContent = '';
      document.getElementById('shareUrlModal').style.display = 'flex';
    }

    function closeShareUrlModal() {
      document.getElementById('shareUrlModal').style.display = 'none';
    }

    async function exportBucketTokens() {
      const btn = document.getElementById('btnExportTokens');
      const statusEl = document.getElementById('exportTokensStatus');
      if (btn) btn.disabled = true;
      if (statusEl) statusEl.textContent = 'Generating…';

      try {
        const permission = document.getElementById('shareUrlPermission')?.value || null;
        const source     = document.getElementById('shareUrlSource')?.value.trim() || null;
        const medium     = document.getElementById('shareUrlMedium')?.value.trim() || null;
        const alias      = document.getElementById('bucketUtmCampaign')?.value.trim() || null;
        const expiry     = parseInt(document.getElementById('shareUrlExpiry')?.value, 10) || 30;
        const format     = document.querySelector('input[name="shareUrlFormat"]:checked')?.value || 'csv';

        const res = await fetch(`/api/admin/buckets/${encodeURIComponent(currentBucket)}/tokens/export`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            ...(typeof ADMIN_API_KEY !== 'undefined' && ADMIN_API_KEY
              ? { 'X-Api-Key': ADMIN_API_KEY }
              : {})
          },
          credentials: 'include',
          body: JSON.stringify({
            permission,
            utmCampaign: alias,
            utmSource:   source,
            utmMedium:   medium,
            expiryDays:  expiry,
            format
          })
        });

        if (!res.ok) {
          const err = await res.json().catch(() => ({}));
          if (statusEl) statusEl.textContent = err.error || 'Export failed.';
          return;
        }

        const blob = await res.blob();
        const url  = URL.createObjectURL(blob);
        const a    = document.createElement('a');
        a.href     = url;
        a.download = `unsubscribe-links-${currentBucket}.${format}`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);

        if (statusEl) statusEl.textContent = 'Download started.';
      } catch {
        if (statusEl) statusEl.textContent = 'Request failed.';
      } finally {
        if (btn) btn.disabled = false;
      }
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

    // SETTINGS
    const settingsDefaults = {
      allowDbLookup: false,
      enableCaching: false,
      theme: 'system',
      font: 'inter',
      defaultLanguage: 'en',
      uiLanguage: 'en',
      emailNotifications: false,
      emailProvider: 'none',
      emailResendApiKey: '',
      emailFromAddress: '',
      emailFromName: '',
      emailSmtpHost: '',
      emailSmtpPort: 587,
      emailSmtpUsername: '',
      emailSmtpPassword: '',
      emailSmtpUseTls: true,
      emailQueueEnabled: false,
      objectStorage: false,
      enableDoubleOptIn: false,
      enableUtmTracking: false,
      emailQueueCron: '*/5 * * * *',
      dataPoliciesEnabled: false,
      dataPolicyCron: '0 0 * * *',
      retentionPurgeEnabled: false,
      retentionPurgeDays: 1095,
      pendingConfirmationPurgeEnabled: false,
      pendingConfirmationPurgeDays: 30,
      retentionPurgeRequireApproval: false,
      pendingConfirmationPurgeRequireApproval: false
    };
    let appSettings = (() => {
      try {
        const saved = localStorage.getItem('beacon_settings');
        return saved ? { ...settingsDefaults, ...JSON.parse(saved) } : { ...settingsDefaults };
      } catch { return { ...settingsDefaults }; }
    })();

    function loadSettings() {
      try {
        const saved = localStorage.getItem('beacon_settings');
        if (saved) appSettings = { ...settingsDefaults, ...JSON.parse(saved) };
      } catch {}
      document.getElementById('setting-enableCaching').checked = appSettings.enableCaching;
      document.getElementById('setting-emailNotifications').checked = appSettings.emailNotifications;
      document.getElementById('setting-objectStorage').checked = appSettings.objectStorage;
      // Font select
      const fontEl = document.getElementById('setting-font');
      if (fontEl) fontEl.value = appSettings.font || 'inter';
      // Default language select
      const langEl = document.getElementById('setting-defaultLanguage');
      if (langEl) langEl.value = appSettings.defaultLanguage || 'en';
      // UI language select
      const uiLangEl = document.getElementById('setting-uiLanguage');
      if (uiLangEl) uiLangEl.value = appSettings.uiLanguage || 'en';
      // Theme radios
      const themeEl = document.querySelector(`input[name="setting-theme"][value="${appSettings.theme || 'system'}"]`);
      if (themeEl) themeEl.checked = true;
      applyAppearance();
      const mode = (typeof USER_AUTH_METHOD !== 'undefined') ? USER_AUTH_METHOD : '';
      showSettingsSection((mode && currentUserRole !== 'admin') ? 'appearance' : 'general', false);
      // Load server-side settings
      loadServerSettings();
      // Apply read-only instance constraints (from config.js)
      applyInstanceConstraints();
    }

    function applyInstanceConstraints() {
      const emailDisabled = typeof DISABLE_EMAIL_NOTIFICATIONS !== 'undefined' && DISABLE_EMAIL_NOTIFICATIONS;
      if (!emailDisabled) return;

      // Show notice banner in Integrations section
      const notice = document.getElementById('email-disabled-notice');
      if (notice) notice.style.display = 'block';

      // Show notice banner in Modules section
      const modulesNotice = document.getElementById('modules-disabled-notice');
      if (modulesNotice) modulesNotice.style.display = 'block';

      // Disable email notifications toggle + gear button
      const emailToggle = document.getElementById('setting-emailNotifications');
      if (emailToggle) { emailToggle.disabled = true; emailToggle.closest('.settings-item-card-label')?.style.setProperty('opacity', '0.5'); }
      const emailGear = document.querySelector('#email-settings-items .btn-settings-gear');
      if (emailGear) { emailGear.disabled = true; emailGear.style.opacity = '0.4'; emailGear.style.cursor = 'not-allowed'; }

      // Disable double opt-in toggle and gray its card
      const doiToggle = document.getElementById('setting-enableDoubleOptIn');
      if (doiToggle) {
        doiToggle.disabled = true;
        const card = doiToggle.closest('.settings-item-card');
        if (card) { card.style.opacity = '0.5'; card.style.pointerEvents = 'none'; }
      }
    }

    async function loadServerSettings() {
      const mode = (typeof USER_AUTH_METHOD !== 'undefined') ? USER_AUTH_METHOD : '';
      if (mode && currentUserRole !== 'admin') return;
      const res = await apiRequest('/api/admin/settings');
      if (res.ok && res.data) {
        appSettings.allowDbLookup = res.data.allowDbLookup ?? false;
        document.getElementById('setting-allowDbLookup').checked = appSettings.allowDbLookup;
        appSettings.defaultLanguage = res.data.defaultLanguage ?? 'en';
        const langEl = document.getElementById('setting-defaultLanguage');
        if (langEl) langEl.value = appSettings.defaultLanguage;
        // Integration
        appSettings.emailNotifications = res.data.emailNotifications ?? false;
        document.getElementById('setting-emailNotifications').checked = appSettings.emailNotifications;
        appSettings.emailProvider      = res.data.emailProvider      ?? 'none';
        appSettings.emailResendApiKey  = res.data.emailResendApiKey  ?? '';
        appSettings.emailFromAddress   = res.data.emailFromAddress   ?? '';
        appSettings.emailFromName      = res.data.emailFromName      ?? '';
        appSettings.emailSmtpHost      = res.data.emailSmtpHost      ?? '';
        appSettings.emailSmtpPort      = res.data.emailSmtpPort      ?? 587;
        appSettings.emailSmtpUsername  = res.data.emailSmtpUsername  ?? '';
        appSettings.emailSmtpPassword  = res.data.emailSmtpPassword  ?? '';
        appSettings.emailSmtpUseTls    = res.data.emailSmtpUseTls    ?? true;
        appSettings.emailQueueEnabled  = res.data.emailQueueEnabled  ?? false;
        appSettings.objectStorage             = res.data.objectStorage             ?? false;
        document.getElementById('setting-objectStorage').checked = appSettings.objectStorage;
        appSettings.objectStorageProvider     = res.data.objectStorageProvider     ?? 'none';
        appSettings.objectStorageBucket       = res.data.objectStorageBucket       ?? '';
        appSettings.objectStorageRegion       = res.data.objectStorageRegion       ?? 'us-east-1';
        appSettings.objectStorageEndpoint     = res.data.objectStorageEndpoint     ?? '';
        appSettings.objectStorageAccessKey    = res.data.objectStorageAccessKey    ?? '';
        appSettings.objectStorageSecretKey    = res.data.objectStorageSecretKey    ?? '';
        appSettings.objectStoragePublicUrl    = res.data.objectStoragePublicUrl    ?? '';
        appSettings.enableSubmissionForms            = res.data.enableSubmissionForms            ?? true;
        document.getElementById('setting-enableSubmissionForms').checked = appSettings.enableSubmissionForms;
        appSettings.submissionDefaultRateLimitPerMinute = res.data.submissionDefaultRateLimitPerMinute ?? 10;
        appSettings.submissionDefaultHoneypotEnabled    = res.data.submissionDefaultHoneypotEnabled    ?? true;
        appSettings.submissionDefaultConsentRequired    = res.data.submissionDefaultConsentRequired    ?? true;
        appSettings.enableCaching             = res.data.enableCaching             ?? false;
        document.getElementById('setting-enableCaching').checked = appSettings.enableCaching;
        appSettings.cacheTtlSeconds           = res.data.cacheTtlSeconds           ?? 300;
        appSettings.cacheConsentRecords       = res.data.cacheConsentRecords       ?? true;
        appSettings.cacheBucketData           = res.data.cacheBucketData           ?? true;
        appSettings.enableDoubleOptIn  = res.data.enableDoubleOptIn  ?? false;
        document.getElementById('setting-enableDoubleOptIn').checked = appSettings.enableDoubleOptIn;
        appSettings.enableUtmTracking  = res.data.enableUtmTracking  ?? false;
        const utmToggle = document.getElementById('setting-enableUtmTracking');
        if (utmToggle) utmToggle.checked = appSettings.enableUtmTracking;
        appSettings.perPermissionEmail = res.data.perPermissionEmail ?? false;
        appSettings.emailQueueCron     = res.data.emailQueueCron     ?? '*/5 * * * *';
        const cronEl = document.getElementById('setting-emailQueueCron');
        if (cronEl) cronEl.value = appSettings.emailQueueCron;
        // Data Policies
        appSettings.dataPoliciesEnabled            = res.data.dataPoliciesEnabled            ?? false;
        appSettings.dataPolicyCron                 = res.data.dataPolicyCron                 ?? '0 0 * * *';
        appSettings.retentionPurgeEnabled          = res.data.retentionPurgeEnabled          ?? false;
        appSettings.retentionPurgeDays             = res.data.retentionPurgeDays             ?? 1095;
        appSettings.pendingConfirmationPurgeEnabled = res.data.pendingConfirmationPurgeEnabled ?? false;
        appSettings.pendingConfirmationPurgeDays   = res.data.pendingConfirmationPurgeDays   ?? 30;
        appSettings.retentionPurgeRequireApproval            = res.data.retentionPurgeRequireApproval            ?? false;
        appSettings.pendingConfirmationPurgeRequireApproval  = res.data.pendingConfirmationPurgeRequireApproval  ?? false;
      }
    }

    function applyAppearance() {
      const fontMap = {
        inter:   '"Inter", system-ui, -apple-system, sans-serif',
        manrope: '"Manrope", system-ui, -apple-system, sans-serif',
        system:  'system-ui, -apple-system, sans-serif'
      };
      const font = appSettings.font || 'inter';
      document.documentElement.style.setProperty('--app-font', fontMap[font] || fontMap.inter);
      const theme = appSettings.theme || 'system';
      if (theme === 'system') {
        document.documentElement.removeAttribute('data-theme');
      } else {
        document.documentElement.setAttribute('data-theme', theme);
      }
      // Appearance changes persist immediately, no save button needed
      try { localStorage.setItem('beacon_settings', JSON.stringify(appSettings)); } catch {}
    }

    const _adminOnlySections = new Set(['general', 'modules', 'data-policies', 'integration', 'system', 'users', 'api-keys']);

    function showSettingsSection(section, pushState = true) {
      const mode = (typeof USER_AUTH_METHOD !== 'undefined') ? USER_AUTH_METHOD : '';
      if (mode && currentUserRole !== 'admin' && _adminOnlySections.has(section)) {
        showSettingsSection(mode ? 'appearance' : section, pushState);
        return;
      }
      document.querySelectorAll('.settings-section').forEach(s => s.classList.remove('active'));
      document.querySelectorAll('.settings-subnav-item').forEach(i => i.classList.remove('active'));
      document.getElementById(`settings-section-${section}`)?.classList.add('active');
      document.querySelector(`.settings-subnav-item[data-section="${section}"]`)?.classList.add('active');
      const labels = { general: 'General', modules: 'Modules', 'data-policies': 'Data Policies', appearance: 'Appearance', system: 'System', integration: 'Integration', users: 'Users', 'api-keys': 'API Keys', account: 'Account' };
      const badge = document.getElementById('settingsSectionBadge');
      if (badge) badge.textContent = labels[section] || section;
      if (pushState) updateUrl({ view: 'settings', section });
      if (section === 'users') loadUsers();
      if (section === 'api-keys') loadApiKeys();
      if (section === 'account') loadAccount();
      if (section === 'data-policies') loadDataPoliciesSection();
    }

    function saveSetting(key, value) {
      appSettings[key] = value;
    }

    async function saveCurrentSettings() {
      try { localStorage.setItem('beacon_settings', JSON.stringify(appSettings)); } catch {}
      const res = await apiRequest('/api/admin/settings', {
        method: 'PUT',
        body: {
          allowDbLookup:      appSettings.allowDbLookup,
          defaultLanguage:    appSettings.defaultLanguage,
          emailNotifications: appSettings.emailNotifications,
          emailProvider:      appSettings.emailProvider,
          emailResendApiKey:  appSettings.emailResendApiKey,
          emailFromAddress:   appSettings.emailFromAddress,
          emailFromName:      appSettings.emailFromName,
          emailSmtpHost:      appSettings.emailSmtpHost,
          emailSmtpPort:      appSettings.emailSmtpPort,
          emailSmtpUsername:  appSettings.emailSmtpUsername,
          emailSmtpPassword:  appSettings.emailSmtpPassword,
          emailSmtpUseTls:    appSettings.emailSmtpUseTls,
          emailQueueEnabled:  appSettings.emailQueueEnabled,
          objectStorage:             appSettings.objectStorage,
          objectStorageProvider:     appSettings.objectStorageProvider,
          objectStorageBucket:       appSettings.objectStorageBucket,
          objectStorageRegion:       appSettings.objectStorageRegion,
          objectStorageEndpoint:     appSettings.objectStorageEndpoint,
          objectStorageAccessKey:    appSettings.objectStorageAccessKey,
          objectStorageSecretKey:    appSettings.objectStorageSecretKey,
          objectStoragePublicUrl:    appSettings.objectStoragePublicUrl,
          enableSubmissionForms:               appSettings.enableSubmissionForms,
          submissionDefaultRateLimitPerMinute: appSettings.submissionDefaultRateLimitPerMinute,
          submissionDefaultHoneypotEnabled:    appSettings.submissionDefaultHoneypotEnabled,
          submissionDefaultConsentRequired:    appSettings.submissionDefaultConsentRequired,
          enableCaching:             appSettings.enableCaching,
          cacheTtlSeconds:           appSettings.cacheTtlSeconds,
          cacheConsentRecords:       appSettings.cacheConsentRecords,
          cacheBucketData:           appSettings.cacheBucketData,
          enableDoubleOptIn:         appSettings.enableDoubleOptIn,
          enableUtmTracking:         appSettings.enableUtmTracking,
          perPermissionEmail: appSettings.perPermissionEmail,
          emailQueueCron:     appSettings.emailQueueCron,
          dataPoliciesEnabled:             appSettings.dataPoliciesEnabled,
          dataPolicyCron:                  appSettings.dataPolicyCron,
          retentionPurgeEnabled:           appSettings.retentionPurgeEnabled,
          retentionPurgeDays:              appSettings.retentionPurgeDays,
          pendingConfirmationPurgeEnabled:            appSettings.pendingConfirmationPurgeEnabled,
          pendingConfirmationPurgeDays:               appSettings.pendingConfirmationPurgeDays,
          retentionPurgeRequireApproval:              appSettings.retentionPurgeRequireApproval,
          pendingConfirmationPurgeRequireApproval:    appSettings.pendingConfirmationPurgeRequireApproval
        }
      });
      if (res.ok) {
        notify('success', 'Settings saved', 'Your changes have been applied.');
      }
    }

    const CRON_REGEX = /^(\*|[0-9]+(,[0-9]+)*|[0-9]+-[0-9]+)(\/[0-9]+)? (\*|[0-9]+(,[0-9]+)*|[0-9]+-[0-9]+)(\/[0-9]+)? (\*|[0-9]+(,[0-9]+)*|[0-9]+-[0-9]+)(\/[0-9]+)? (\*|[0-9]+(,[0-9]+)*|[0-9]+-[0-9]+)(\/[0-9]+)? (\*|[0-9]+(,[0-9]+)*|[0-9]+-[0-9]+)(\/[0-9]+)?$/;

    function validateCronInput(input) {
      const valid = CRON_REGEX.test(input.value.trim());
      input.style.borderColor = valid ? '' : 'hsl(var(--destructive))';
      return valid;
    }

    const DATA_POLICY_CRON_PRESETS = [
      { expr: '0 0 * * *',    label: 'Once a day (00:00 UTC)' },
      { expr: '0 2 * * *',    label: 'Once a day (02:00 UTC)' },
      { expr: '0 9 * * *',    label: 'Once a day (09:00 UTC)' },
      { expr: '0 0 * * 0',    label: 'Once a week (Sunday 00:00 UTC)' },
      { expr: '0 0 1 * *',    label: 'Once a month (1st at 00:00 UTC)' },
    ];

    const CRON_PRESETS = [
      { expr: '* * * * *',       label: 'Every minute' },
      { expr: '*/2 * * * *',     label: 'Every 2 minutes' },
      { expr: '*/5 * * * *',     label: 'Every 5 minutes' },
      { expr: '*/10 * * * *',    label: 'Every 10 minutes' },
      { expr: '*/15 * * * *',    label: 'Every 15 minutes' },
      { expr: '*/30 * * * *',    label: 'Every 30 minutes' },
      { expr: '0 * * * *',       label: 'Every hour' },
      { expr: '0 */2 * * *',     label: 'Every 2 hours' },
      { expr: '0 */6 * * *',     label: 'Every 6 hours' },
      { expr: '0 */12 * * *',    label: 'Every 12 hours' },
      { expr: '0 0 * * *',       label: 'Once a day (00:00 UTC)' },
      { expr: '0 9 * * *',       label: 'Once a day (09:00 UTC)' },
      { expr: '0 0 * * 0',       label: 'Once a week (Sunday 00:00 UTC)' },
    ];

    let selectedCronIndex = -1;

    function showCronSuggestions(force = false) {
      const input = document.getElementById('setting-emailQueueCron');
      const dropdown = document.getElementById('cronAutocomplete');
      const query = input.value.trim().toLowerCase();

      const matches = (force || !query)
        ? CRON_PRESETS
        : CRON_PRESETS.filter(p => p.expr.includes(query) || p.label.toLowerCase().includes(query));

      if (!force && (matches.length === 0 || (matches.length === 1 && matches[0].expr === query))) {
        dropdown.classList.remove('open');
        return;
      }

      selectedCronIndex = -1;
      dropdown.innerHTML = matches.map((p, idx) => `
        <div class="autocomplete-item" data-index="${idx}" onclick="selectCronPreset('${p.expr}')" onmouseenter="highlightCron(${idx})">
          <div class="bucket-name">${sanitize(p.expr)}</div>
          <div class="bucket-info">${sanitize(p.label)}</div>
        </div>
      `).join('');

      positionDropdown(input, dropdown);
      dropdown.classList.add('open');
    }

    function selectCronPreset(expr) {
      const input = document.getElementById('setting-emailQueueCron');
      input.value = expr;
      document.getElementById('cronAutocomplete').classList.remove('open');
      selectedCronIndex = -1;
      validateCronInput(input);
      saveSetting('emailQueueCron', expr);
    }

    function hideCronSuggestions() {
      document.getElementById('cronAutocomplete')?.classList.remove('open');
      selectedCronIndex = -1;
    }

    function highlightCron(index) {
      document.querySelectorAll('#cronAutocomplete .autocomplete-item').forEach((item, i) => {
        item.classList.toggle('selected', i === index);
      });
      selectedCronIndex = index;
    }

    document.getElementById('setting-emailQueueCron')?.addEventListener('keydown', function(e) {
      const dropdown = document.getElementById('cronAutocomplete');
      if (!dropdown.classList.contains('open')) return;
      const items = dropdown.querySelectorAll('.autocomplete-item');
      if (items.length === 0) return;
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        selectedCronIndex = Math.min(selectedCronIndex + 1, items.length - 1);
        highlightCron(selectedCronIndex);
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        selectedCronIndex = Math.max(selectedCronIndex - 1, 0);
        highlightCron(selectedCronIndex);
      } else if (e.key === 'Enter' && selectedCronIndex >= 0) {
        e.preventDefault();
        const expr = items[selectedCronIndex].querySelector('.bucket-name').textContent;
        selectCronPreset(expr);
      } else if (e.key === 'Escape') {
        hideCronSuggestions();
      }
    });

    // DATA POLICIES CRON (mirrors email queue cron, separate input/dropdown IDs)
    let _dpCronIndex = -1;

    function showDataPolicyCronSuggestions(force = false) {
      const input = document.getElementById('setting-dataPolicyCron');
      const dropdown = document.getElementById('dataPolicyCronAutocomplete');
      if (!input || !dropdown) return;
      const query = input.value.trim().toLowerCase();
      const matches = (force || !query)
        ? DATA_POLICY_CRON_PRESETS
        : DATA_POLICY_CRON_PRESETS.filter(p => p.expr.includes(query) || p.label.toLowerCase().includes(query));
      if (!force && (matches.length === 0 || (matches.length === 1 && matches[0].expr === query))) {
        dropdown.classList.remove('open');
        return;
      }
      _dpCronIndex = -1;
      dropdown.innerHTML = matches.map((p, idx) => `
        <div class="autocomplete-item" data-index="${idx}" onclick="selectDataPolicyCronPreset('${p.expr}')" onmouseenter="_dpCronIndex=${idx};document.querySelectorAll('#dataPolicyCronAutocomplete .autocomplete-item').forEach((el,i)=>el.classList.toggle('selected',i===${idx}))">
          <div class="bucket-name">${sanitize(p.expr)}</div>
          <div class="bucket-info">${sanitize(p.label)}</div>
        </div>
      `).join('');
      positionDropdown(input, dropdown);
      dropdown.classList.add('open');
    }

    function selectDataPolicyCronPreset(expr) {
      const input = document.getElementById('setting-dataPolicyCron');
      if (!input) return;
      input.value = expr;
      document.getElementById('dataPolicyCronAutocomplete')?.classList.remove('open');
      _dpCronIndex = -1;
      validateCronInput(input);
      saveSetting('dataPolicyCron', expr);
    }

    document.addEventListener('DOMContentLoaded', () => {
      document.getElementById('setting-dataPolicyCron')?.addEventListener('keydown', function(e) {
        const dropdown = document.getElementById('dataPolicyCronAutocomplete');
        if (!dropdown?.classList.contains('open')) return;
        const items = dropdown.querySelectorAll('.autocomplete-item');
        if (items.length === 0) return;
        if (e.key === 'ArrowDown') { e.preventDefault(); _dpCronIndex = Math.min(_dpCronIndex + 1, items.length - 1); }
        else if (e.key === 'ArrowUp') { e.preventDefault(); _dpCronIndex = Math.max(_dpCronIndex - 1, 0); }
        else if (e.key === 'Enter' && _dpCronIndex >= 0) { e.preventDefault(); selectDataPolicyCronPreset(items[_dpCronIndex].querySelector('.bucket-name').textContent); return; }
        else if (e.key === 'Escape') { dropdown.classList.remove('open'); return; }
        items.forEach((el, i) => el.classList.toggle('selected', i === _dpCronIndex));
      });
    });

    // DATA POLICIES SECTION
    function toggleDataPoliciesEnabled(enabled) {
      saveSetting('dataPoliciesEnabled', enabled);
      const controls = document.getElementById('data-policies-controls');
      if (controls) { controls.style.opacity = enabled ? '' : '0.5'; controls.style.pointerEvents = enabled ? '' : 'none'; }
    }

    async function loadDataPoliciesSection() {
      const el = document.getElementById('setting-dataPoliciesEnabled');
      if (el) { el.checked = !!appSettings.dataPoliciesEnabled; toggleDataPoliciesEnabled(!!appSettings.dataPoliciesEnabled); }
      const cronEl = document.getElementById('setting-dataPolicyCron');
      if (cronEl) cronEl.value = appSettings.dataPolicyCron || '0 0 * * *';
      const rpe = document.getElementById('setting-retentionPurgeEnabled');
      if (rpe) rpe.checked = !!appSettings.retentionPurgeEnabled;
      const rpra = document.getElementById('setting-retentionPurgeRequireApproval');
      if (rpra) rpra.checked = !!appSettings.retentionPurgeRequireApproval;
      const pce = document.getElementById('setting-pendingConfirmationPurgeEnabled');
      if (pce) pce.checked = !!appSettings.pendingConfirmationPurgeEnabled;
      const pcra = document.getElementById('setting-pendingConfirmationPurgeRequireApproval');
      if (pcra) pcra.checked = !!appSettings.pendingConfirmationPurgeRequireApproval;
      await loadWorkflowTasks();
    }

    // Track tasks actioned in this session to prevent duplicate actions and enable instant UI updatess
    const _locallyActionedIds = new Set();
    let _workflowAllTasks = [];
    let _workflowPage = 1;
    let _workflowPageSize = 25;
    let _workflowArchivedVisible = false;

    // Audit
    async function loadAudit() {
      const params = new URLSearchParams();
      if (auditFilterBucket) params.set('bucket', auditFilterBucket);
      if (auditFilterIdentity) params.set('emailHash', auditFilterIdentity);
      params.set('page', auditCurrentPage);
      params.set('size', auditPageSize);

      const urlParams = { view: 'audit' };
      if (auditFilterBucket) urlParams.bucket = auditFilterBucket;
      if (auditFilterIdentity) urlParams.identity = auditFilterIdentity;
      if (auditCurrentPage > 1) urlParams.page = auditCurrentPage;
      updateUrl(urlParams);

      renderAuditHeader();

      const tbody = document.getElementById('auditBody');
      tbody.innerHTML = '<tr><td colspan="8" style="text-align:center;padding:3rem;color:hsl(var(--muted-foreground))">Loading…</td></tr>';

      const res = await apiRequest(`/api/admin/audit?${params}`);
      if (!res.ok) {
        tbody.innerHTML = '<tr><td colspan="8" style="text-align:center;padding:3rem;color:hsl(var(--muted-foreground))">Failed to load audit log.</td></tr>';
        return;
      }

      auditTotalRecords = res.data.total;
      if (res.data.records.length === 0) {
        const msg = (auditFilterBucket || auditFilterIdentity)
          ? 'No audit entries match the active filter'
          : 'No audit entries yet';
        tbody.innerHTML = `<tr><td colspan="8" style="text-align:center;padding:3rem;color:hsl(var(--muted-foreground))">${msg}</td></tr>`;
      } else {
        tbody.innerHTML = res.data.records.map(renderAuditRow).join('');
      }
      updateAuditPagination();
    }

    function renderAuditHeader() {
      const thead = document.getElementById('auditTableHead');
      if (!thead) return;
      const hasIdentity = auditFilterIdentity ? 'has-search' : '';
      const hasBucket = auditFilterBucket ? 'has-search' : '';
      thead.innerHTML = `
        <th>Timestamp</th>
        <th>
          <div class="column-search">
            <span>Identity</span>
            <button class="search-trigger ${hasIdentity}" id="auditIdentitySearchTrigger" onclick="toggleAuditIdentityPopover(event)" title="Filter by identity">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="8"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line></svg>
            </button>
            <div class="search-popover" id="auditIdentityPopover" style="min-width:260px">
              <label>Filter by identity hash (partial)</label>
              <input type="text" id="auditIdentityInput" placeholder="e.g., a1b2c3d4" value="${sanitize(auditFilterIdentity || '')}" onkeydown="if(event.key==='Enter')applyAuditIdentityFilter()">
              <div class="search-actions">
                <button class="btn btn-outline" style="padding:0.375rem 0.75rem;font-size:0.75rem" onclick="clearAuditIdentityFilter()">Clear</button>
                <button class="btn btn-primary" style="padding:0.375rem 0.75rem;font-size:0.75rem" onclick="applyAuditIdentityFilter()">Filter</button>
              </div>
            </div>
          </div>
        </th>
        <th>
          <div class="column-search">
            <span>Bucket</span>
            <button class="search-trigger ${hasBucket}" id="auditBucketSearchTrigger" onclick="toggleAuditBucketPopover(event)" title="Filter by bucket">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="8"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line></svg>
            </button>
            <div class="search-popover" id="auditBucketPopover" style="min-width:220px">
              <label>Filter by bucket name</label>
              <input type="text" id="auditBucketInput" placeholder="e.g., newsletter" value="${sanitize(auditFilterBucket || '')}" onkeydown="if(event.key==='Enter')applyAuditBucketFilter()">
              <div class="search-actions">
                <button class="btn btn-outline" style="padding:0.375rem 0.75rem;font-size:0.75rem" onclick="clearAuditBucketFilter()">Clear</button>
                <button class="btn btn-primary" style="padding:0.375rem 0.75rem;font-size:0.75rem" onclick="applyAuditBucketFilter()">Filter</button>
              </div>
            </div>
          </div>
        </th>
        <th>Permission</th>
        <th>Change</th>
        <th>Source</th>
        <th>Actor</th>
        <th></th>
      `;
    }

    function toggleAuditIdentityPopover(event) {
      event.stopPropagation();
      const popover = document.getElementById('auditIdentityPopover');
      const trigger = document.getElementById('auditIdentitySearchTrigger');
      const isOpen = popover?.classList.contains('open');
      document.querySelectorAll('.search-popover.open').forEach(p => p.classList.remove('open'));
      document.querySelectorAll('.search-trigger.active').forEach(t => t.classList.remove('active'));
      if (!isOpen) {
        popover?.classList.add('open');
        trigger?.classList.add('active');
        setTimeout(() => document.getElementById('auditIdentityInput')?.focus(), 50);
      }
    }

    function toggleAuditBucketPopover(event) {
      event.stopPropagation();
      const popover = document.getElementById('auditBucketPopover');
      const trigger = document.getElementById('auditBucketSearchTrigger');
      const isOpen = popover?.classList.contains('open');
      document.querySelectorAll('.search-popover.open').forEach(p => p.classList.remove('open'));
      document.querySelectorAll('.search-trigger.active').forEach(t => t.classList.remove('active'));
      if (!isOpen) {
        popover?.classList.add('open');
        trigger?.classList.add('active');
        setTimeout(() => document.getElementById('auditBucketInput')?.focus(), 50);
      }
    }

    function applyAuditIdentityFilter() {
      const val = document.getElementById('auditIdentityInput')?.value.trim();
      auditFilterIdentity = val || null;
      auditCurrentPage = 1;
      document.getElementById('auditIdentityPopover')?.classList.remove('open');
      document.getElementById('auditIdentitySearchTrigger')?.classList.remove('active');
      loadAudit();
    }

    function applyAuditBucketFilter() {
      const val = document.getElementById('auditBucketInput')?.value.trim();
      auditFilterBucket = val || null;
      auditCurrentPage = 1;
      document.getElementById('auditBucketPopover')?.classList.remove('open');
      document.getElementById('auditBucketSearchTrigger')?.classList.remove('active');
      loadAudit();
    }

    function clearAuditIdentityFilter() {
      auditFilterIdentity = null;
      auditCurrentPage = 1;
      document.getElementById('auditIdentityPopover')?.classList.remove('open');
      document.getElementById('auditIdentitySearchTrigger')?.classList.remove('active');
      loadAudit();
    }

    function clearAuditBucketFilter() {
      auditFilterBucket = null;
      auditCurrentPage = 1;
      document.getElementById('auditBucketPopover')?.classList.remove('open');
      document.getElementById('auditBucketSearchTrigger')?.classList.remove('active');
      loadAudit();
    }

    function auditStatusChip(status) {
      if (!status) return '<span class="status-chip">None</span>';
      const cls = status === 'OptedIn' ? 'opted-in' : status === 'OptedOut' ? 'opted-out' : 'pending';
      const label = status === 'OptedIn' ? 'Opted in' : status === 'OptedOut' ? 'Opted out' : status;
      return `<span class="status-chip ${cls}">${label}</span>`;
    }

    function renderAuditRow(e) {
      const sourceLabel = { Url: 'User', Api: 'API', Admin: 'Admin' }[e.source] || e.source;
      const nullCell = '<span class="muted">—</span>';
      const oldChip = auditStatusChip(e.oldStatus);
      const newChip = auditStatusChip(e.newStatus);
      const hasFields = !!e.customFields;
      const encodedFields = hasFields ? encodeURIComponent(e.customFields) : '';
      const actionsCell = `<div class="row-actions">
          <span class="tooltip-wrapper">
            <button class="btn-actions" onclick="openAuditCustomFields('${encodedFields}')">:</button>
            <span class="tooltip tooltip-above tooltip-right">Custom fields</span>
          </span>
         </div>`;
      return `
        <tr>
          <td>${sanitize(formatDate(e.changedAt))}</td>
          <td>
            <span class="tooltip-wrapper"
              onclick="auditIdentityClick('${sanitize(e.emailHash)}')"
              ondblclick="auditIdentityDblClick('${sanitize(e.emailHash)}')">
              <span class="email-hash" style="cursor:pointer">${sanitize(e.displayId)}</span>
              <span class="tooltip">Click to filter · double-click to copy</span>
            </span>
          </td>
          <td><span class="bucket-name" style="cursor:pointer" onclick="showAuditForBucket('${sanitize(e.bucket)}')">${sanitize(e.bucket)}</span></td>
          <td><code>${sanitize(e.permission)}</code></td>
          <td style="white-space:nowrap">${oldChip} → ${newChip}</td>
          <td>${sanitize(sourceLabel)}</td>
          <td>${e.actorId ? sanitize(e.actorId) : nullCell}</td>
          <td>${actionsCell}</td>
        </tr>`;
    }

    function openAuditCustomFields(encodedFields) {
      let fields = {};
      try { fields = JSON.parse(decodeURIComponent(encodedFields)); } catch {}
      const list = document.getElementById('auditCustomFieldsList');
      if (list) {
        const entries = Object.entries(fields);
        list.innerHTML = entries.length
          ? entries.map(([k, v]) => `
            <div class="custom-field-row">
              <span class="custom-field-key">${sanitize(k)}</span>
              <span class="custom-field-value">${sanitize(String(v))}</span>
            </div>`).join('')
          : '<p class="muted" style="font-size:0.85rem">No custom fields.</p>';
      }
      document.getElementById('auditCustomFieldsModal').style.display = 'flex';
    }

    function closeAuditCustomFieldsModal() {
      document.getElementById('auditCustomFieldsModal').style.display = 'none';
    }

    function auditIdentityClick(hash) {
      clearTimeout(_auditClickTimer);
      _auditClickTimer = setTimeout(() => showAuditForIdentity(hash), 250);
    }

    function auditIdentityDblClick(hash) {
      clearTimeout(_auditClickTimer);
      copyTextNow(hash);
    }

    function showAuditForBucket(bucket) {
      auditFilterBucket = bucket;
      auditFilterIdentity = null;
      auditCurrentPage = 1;
      showView('audit');
    }

    function showAuditForIdentity(hash) {
      auditFilterBucket = null;
      auditFilterIdentity = hash;
      auditCurrentPage = 1;
      showView('audit');
    }

    function showAuditForBucketAndIdentity(bucket, hash) {
      auditFilterBucket = bucket;
      auditFilterIdentity = hash;
      auditCurrentPage = 1;
      showView('audit');
    }

    function clearAuditFilter() {
      auditFilterBucket = null;
      auditFilterIdentity = null;
      auditCurrentPage = 1;
      loadAudit();
    }

    function updateAuditPageSize() {
      auditPageSize = parseInt(document.getElementById('auditPageSizeSelect').value);
      auditCurrentPage = 1;
      loadAudit();
    }

    function changeAuditPage(delta) {
      const maxPage = Math.ceil(auditTotalRecords / auditPageSize) || 1;
      auditCurrentPage = Math.max(1, Math.min(auditCurrentPage + delta, maxPage));
      loadAudit();
    }

    function updateAuditPagination() {
      const maxPage = Math.ceil(auditTotalRecords / auditPageSize) || 1;
      const start = auditTotalRecords === 0 ? 0 : (auditCurrentPage - 1) * auditPageSize + 1;
      const end = Math.min(auditCurrentPage * auditPageSize, auditTotalRecords);
      const info = document.getElementById('auditPaginationInfo');
      if (info) info.textContent = auditTotalRecords === 0 ? 'No entries' : `${start}–${end} of ${auditTotalRecords}`;
      const prev = document.getElementById('auditPrevBtn');
      const next = document.getElementById('auditNextBtn');
      if (prev) prev.disabled = auditCurrentPage <= 1;
      if (next) next.disabled = auditCurrentPage >= maxPage;
    }
    // ── /Audit ─────────────────────────────────────────────────────────────

    const _workflowTypeLabels = {
      RetentionPurge: 'Opted-out anonymisation',
      PendingConfirmationPurge: 'Pending confirmation cleanup'
    };

    async function loadWorkflowPage() {
      const notice = document.getElementById('workflow-policy-notice');
      const noPolicies = !appSettings.retentionPurgeEnabled && !appSettings.pendingConfirmationPurgeEnabled;
      if (notice) notice.style.display = noPolicies ? '' : 'none';
      _locallyActionedIds.clear();
      _workflowArchivedVisible = false;
      _workflowPage = 1;
      const btn = document.getElementById('workflowArchiveToggle');
      if (btn) btn.textContent = 'Show archived';
      await loadWorkflowTasks();
    }

    async function loadWorkflowTasks() {
      const res = await apiRequest('/api/admin/data-policies/tasks?limit=200');
      if (!res.ok || !res.data) return;
      _workflowAllTasks = res.data;

      // Settings panel: last 5 completed/failed only
      renderSettingsRunHistory(_workflowAllTasks.filter(t => t.status === 'Completed' || t.status === 'Failed').slice(0, 5));

      // Workflow page
      renderWorkflowTable();
    }

    function renderWorkflowTable() {
      const tasks = _workflowArchivedVisible
        ? _workflowAllTasks.filter(t => t.status !== 'Running' && t.status !== 'Pending')
        : _workflowAllTasks.filter(t => t.status === 'PendingApproval' || _locallyActionedIds.has(t.id));

      // Sort newest first by scheduledAt
      tasks.sort((a, b) => new Date(b.scheduledAt) - new Date(a.scheduledAt));

      const total = tasks.length;
      const totalPages = Math.ceil(total / _workflowPageSize) || 1;
      _workflowPage = Math.min(_workflowPage, totalPages);
      const start = (_workflowPage - 1) * _workflowPageSize;
      const page = tasks.slice(start, start + _workflowPageSize);

      const tbody = document.getElementById('workflowTbody');
      if (!tbody) return;

      if (total === 0) {
        const msg = _workflowArchivedVisible ? 'No runs yet.' : 'No items awaiting approval.';
        tbody.innerHTML = `<tr><td colspan="6" style="text-align:center;padding:2rem;color:hsl(var(--muted-foreground));font-size:0.875rem">${msg}</td></tr>`;
      } else {
        tbody.innerHTML = page.map(t => {
          const isPending = t.status === 'PendingApproval';
          const queued = t.scheduledAt ? new Date(t.scheduledAt).toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }) : '—';
          const records = (t.status === 'Completed' || t.status === 'Failed') ? t.recordsAffected : '—';
          const badge = isPending
            ? '<span class="status-badge pending-approval">Pending approval</span>'
            : t.status === 'Completed'
              ? '<span class="status-badge completed">Completed</span>'
              : t.status === 'Failed'
                ? '<span class="status-badge failed">Failed</span>'
                : '<span class="status-badge rejected">Rejected</span>';
          const actions = isPending
            ? `<div style="display:flex;gap:0.5rem;white-space:nowrap">
                 <button class="btn btn-outline btn-sm" onclick="approveWorkflowTask('${sanitize(t.id)}')">Approve</button>
                 <button class="btn btn-outline btn-sm" style="color:hsl(var(--muted-foreground))" onclick="rejectWorkflowTask('${sanitize(t.id)}')">Reject</button>
               </div>`
            : '';
          return `<tr id="workflow-row-${sanitize(t.id)}">
            <td>${sanitize(_workflowTypeLabels[t.taskType] || t.taskType)}</td>
            <td>${sanitize(t.triggeredBy)}</td>
            <td>${queued}</td>
            <td>${records}</td>
            <td>${badge}${t.errorMessage ? `<span title="${sanitize(t.errorMessage)}" style="margin-left:0.4rem;opacity:0.6;cursor:help;font-size:0.75rem">ⓘ</span>` : ''}</td>
            <td>${actions}</td>
          </tr>`;
        }).join('');
      }

      // Pagination
      const bar = document.getElementById('workflowPaginationBar');
      if (bar) bar.style.display = 'flex';
      const startN = total === 0 ? 0 : start + 1;
      const endN = Math.min(start + _workflowPageSize, total);
      const info = document.getElementById('workflowPaginationInfo');
      if (info) info.textContent = `Showing ${startN} to ${endN} of ${total}`;
      const prevBtn = document.getElementById('workflowPrevBtn');
      const nextBtn = document.getElementById('workflowNextBtn');
      if (prevBtn) prevBtn.disabled = _workflowPage <= 1;
      if (nextBtn) nextBtn.disabled = _workflowPage >= totalPages;
    }

    function changeWorkflowPage(dir) {
      const tasks = _workflowArchivedVisible
        ? _workflowAllTasks.filter(t => t.status !== 'Running' && t.status !== 'Pending')
        : _workflowAllTasks.filter(t => t.status === 'PendingApproval' || _locallyActionedIds.has(t.id));
      const totalPages = Math.ceil(tasks.length / _workflowPageSize) || 1;
      _workflowPage = Math.max(1, Math.min(totalPages, _workflowPage + dir));
      renderWorkflowTable();
    }

    function updateWorkflowPageSize() {
      const el = document.getElementById('workflowPageSize');
      if (el) _workflowPageSize = parseInt(el.value, 10);
      _workflowPage = 1;
      renderWorkflowTable();
    }

    function toggleWorkflowArchived() {
      _workflowArchivedVisible = !_workflowArchivedVisible;
      _workflowPage = 1;
      const btn = document.getElementById('workflowArchiveToggle');
      if (btn) btn.textContent = _workflowArchivedVisible ? 'Hide archived' : 'Show archived';
      renderWorkflowTable();
    }

    function renderSettingsRunHistory(tasks) {
      const tbody = document.getElementById('workflowTasksTbody');
      if (!tbody) return;
      if (!tasks || tasks.length === 0) {
        tbody.innerHTML = '<tr><td colspan="6" style="text-align:center;padding:2rem;color:hsl(var(--muted-foreground));font-size:0.875rem">No completed runs yet.</td></tr>';
        return;
      }
      tbody.innerHTML = tasks.map(t => {
        const started = t.startedAt ? new Date(t.startedAt) : null;
        const completed = t.completedAt ? new Date(t.completedAt) : null;
        const durationMs = (started && completed) ? completed - started : null;
        const duration = durationMs !== null ? (durationMs < 1000 ? `${durationMs}ms` : `${(durationMs / 1000).toFixed(1)}s`) : '—';
        const startedStr = started ? started.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }) : '—';
        const badge = t.status === 'Completed'
          ? '<span class="status-badge completed">Completed</span>'
          : '<span class="status-badge failed">Failed</span>';
        return `<tr>
          <td>${sanitize(_workflowTypeLabels[t.taskType] || t.taskType)}</td>
          <td>${sanitize(t.triggeredBy)}</td>
          <td>${startedStr}</td>
          <td>${duration}</td>
          <td>${t.recordsAffected}</td>
          <td>${badge}${t.errorMessage ? `<span title="${sanitize(t.errorMessage)}" style="margin-left:0.4rem;opacity:0.6;cursor:help;font-size:0.75rem">ⓘ</span>` : ''}</td>
        </tr>`;
      }).join('');
    }

    async function approveWorkflowTask(id) {
      const row = document.getElementById(`workflow-row-${id}`);
      const btns = row ? row.querySelectorAll('button') : [];
      btns.forEach(b => { b.disabled = true; });
      const res = await apiRequest(`/api/admin/data-policies/tasks/${id}/approve`, { method: 'POST' });
      if (res.ok && res.data) {
        _locallyActionedIds.add(id);
        // Update cached task so toggling archived re-renders correctly
        const idx = _workflowAllTasks.findIndex(t => t.id === id);
        if (idx >= 0) _workflowAllTasks[idx] = res.data;
        // Update row in-place (cols: 0=task, 1=by, 2=queued, 3=records, 4=badge, 5=actions)
        if (row) {
          if (row.cells[3]) row.cells[3].textContent = res.data.recordsAffected ?? '—';
          if (row.cells[4]) row.cells[4].innerHTML = res.data.status === 'Completed'
            ? '<span class="status-badge completed">Completed</span>'
            : '<span class="status-badge failed">Failed</span>';
          if (row.cells[5]) row.cells[5].innerHTML = '';
        }
        if (res.data.status === 'Completed') {
          notify('success', 'Policy executed', `${res.data.recordsAffected} record(s) affected.`);
        } else {
          notify('error', 'Execution failed', res.data.errorMessage || 'An error occurred.');
        }
      } else {
        btns.forEach(b => { b.disabled = false; });
        notify('error', 'Approval failed', 'Could not approve the task.');
      }
    }

    async function rejectWorkflowTask(id) {
      const row = document.getElementById(`workflow-row-${id}`);
      const btns = row ? row.querySelectorAll('button') : [];
      btns.forEach(b => { b.disabled = true; });
      const res = await apiRequest(`/api/admin/data-policies/tasks/${id}/reject`, { method: 'POST' });
      if (res.ok && res.data) {
        _locallyActionedIds.add(id);
        // Update cached task so toggling archived re-renders correctly
        const idx = _workflowAllTasks.findIndex(t => t.id === id);
        if (idx >= 0) _workflowAllTasks[idx] = res.data;
        // Update row in-place
        if (row) {
          if (row.cells[4]) row.cells[4].innerHTML = '<span class="status-badge rejected">Rejected</span>';
          if (row.cells[5]) row.cells[5].innerHTML = '';
        }
      } else {
        btns.forEach(b => { b.disabled = false; });
        notify('error', 'Rejection failed', 'Could not reject the task.');
      }
    }

    async function triggerDataPoliciesRun() {
      // Update both possible Run Now buttons (settings panel + workflow page)
      const btns = ['dataPoliciesRunBtn', 'workflowRunBtn'].map(id => document.getElementById(id)).filter(Boolean);
      btns.forEach(b => { b.classList.add('is-loading'); b.disabled = true; });
      const res = await apiRequest('/api/admin/data-policies/run', { method: 'POST' });
      if (res.ok || res.status === 202) {
        notify('success', 'Run triggered', 'Policies will run in a moment.');
        // Poll twice: once quickly, once after giving the worker time to finish
        setTimeout(loadWorkflowTasks, 1500);
        setTimeout(loadWorkflowTasks, 6000);
      } else {
        notify('error', 'Run Failed', 'Could not trigger a manual run.');
      }
      btns.forEach(b => { b.classList.remove('is-loading'); b.disabled = false; });
    }

    // RETENTION PURGE MODAL
    function openRetentionPurgeModal() {
      const el = document.getElementById('setting-retentionPurgeDays');
      if (el) el.value = appSettings.retentionPurgeDays ?? 1095;
      document.getElementById('retentionPurgeModal').style.display = 'flex';
    }
    function closeRetentionPurgeModal() { document.getElementById('retentionPurgeModal').style.display = 'none'; }
    function saveRetentionPurgeDays() {
      const days = parseInt(document.getElementById('setting-retentionPurgeDays').value, 10);
      if (isNaN(days) || days < 1) { notify('warning', 'Invalid value', 'Enter a number greater than 0.'); return; }
      saveSetting('retentionPurgeDays', days);
      closeRetentionPurgeModal();
    }

    // PENDING CONFIRMATION MODAL
    function openPendingConfirmationModal() {
      const el = document.getElementById('setting-pendingConfirmationPurgeDays');
      if (el) el.value = appSettings.pendingConfirmationPurgeDays ?? 30;
      document.getElementById('pendingConfirmationModal').style.display = 'flex';
    }
    function closePendingConfirmationModal() { document.getElementById('pendingConfirmationModal').style.display = 'none'; }
    function savePendingConfirmationPurgeDays() {
      const days = parseInt(document.getElementById('setting-pendingConfirmationPurgeDays').value, 10);
      if (isNaN(days) || days < 1) { notify('warning', 'Invalid value', 'Enter a number greater than 0.'); return; }
      saveSetting('pendingConfirmationPurgeDays', days);
      closePendingConfirmationModal();
    }

    function openCacheSettingsModal() {
      document.getElementById('setting-cacheTtlSeconds').value = appSettings.cacheTtlSeconds ?? 300;
      document.getElementById('setting-cacheConsentRecords').checked = appSettings.cacheConsentRecords ?? true;
      document.getElementById('setting-cacheBucketData').checked = appSettings.cacheBucketData ?? true;
      document.getElementById('cacheStatsResult').textContent = '';
      document.getElementById('cacheSettingsModal').style.display = 'flex';
      loadCacheStats();
    }

    function closeCacheSettingsModal() {
      document.getElementById('cacheSettingsModal').style.display = 'none';
    }

    async function saveCacheSettings() {
      appSettings.cacheTtlSeconds = parseInt(document.getElementById('setting-cacheTtlSeconds').value, 10) || 300;
      appSettings.cacheConsentRecords = document.getElementById('setting-cacheConsentRecords').checked;
      appSettings.cacheBucketData = document.getElementById('setting-cacheBucketData').checked;
      await saveCurrentSettings();
      closeCacheSettingsModal();
    }

    async function flushCache() {
      const btn = document.getElementById('btnFlushCache');
      if (btn) btn.disabled = true;
      try {
        const res = await apiRequest('/api/admin/cache', { method: 'DELETE' });
        if (res.ok) {
          notify('success', 'Cache flushed', 'All cached entries have been cleared.');
          loadCacheStats();
        } else {
          notify('error', 'Failed', 'Could not flush cache.');
        }
      } finally {
        if (btn) btn.disabled = false;
      }
    }

    async function loadCacheStats() {
      const el = document.getElementById('cacheStatsResult');
      if (!el) return;
      const res = await apiRequest('/api/admin/cache/stats');
      if (res.ok && res.data) {
        const { enabled, provider, approximateKeyCount } = res.data;
        el.textContent = enabled
          ? `Provider: ${provider} — ~${approximateKeyCount} key${approximateKeyCount !== 1 ? 's' : ''} tracked`
          : 'Caching is disabled.';
      }
    }

    // EMAIL SETTINGS MODAL
    function openEmailSettingsModal() {
      if (typeof DISABLE_EMAIL_NOTIFICATIONS !== 'undefined' && DISABLE_EMAIL_NOTIFICATIONS) return;
      document.getElementById('emailProvider').value          = appSettings.emailProvider || 'none';
      document.getElementById('emailResendApiKey').value      = appSettings.emailResendApiKey || '';
      document.getElementById('emailFromAddress').value       = appSettings.emailFromAddress || '';
      document.getElementById('emailFromName').value          = appSettings.emailFromName || '';
      document.getElementById('emailSmtpHost').value          = appSettings.emailSmtpHost || '';
      document.getElementById('emailSmtpPort').value          = appSettings.emailSmtpPort || 587;
      document.getElementById('emailSmtpUsername').value      = appSettings.emailSmtpUsername || '';
      document.getElementById('emailSmtpPassword').value      = appSettings.emailSmtpPassword || '';
      document.getElementById('emailSmtpUseTls').checked      = appSettings.emailSmtpUseTls !== false;
      document.getElementById('emailQueueEnabled').checked    = !!appSettings.emailQueueEnabled;
      showEmailProviderFields(appSettings.emailProvider || 'none');
      document.getElementById('emailSettingsModal').style.display = 'flex';
    }

    function closeEmailSettingsModal() {
      document.getElementById('emailSettingsModal').style.display = 'none';
    }

    function showEmailProviderFields(provider) {
      const isConfigured = provider === 'resend' || provider === 'smtp';
      document.getElementById('email-fields-resend').style.display = provider === 'resend' ? '' : 'none';
      document.getElementById('email-fields-smtp').style.display   = provider === 'smtp'   ? '' : 'none';
      document.getElementById('email-fields-sender').style.display = isConfigured ? '' : 'none';
      document.getElementById('email-fields-queue').style.display  = isConfigured ? '' : 'none';
    }

    function validateEmailConfig(provider, fields) {
      if (provider === 'resend') {
        if (!fields.emailResendApiKey?.trim()) return 'Resend API key is required.';
        if (!fields.emailFromAddress?.trim())  return 'From address is required.';
      }
      if (provider === 'smtp') {
        if (!fields.emailSmtpHost?.trim())     return 'SMTP host is required.';
        if (!fields.emailSmtpPort)             return 'SMTP port is required.';
        if (!fields.emailSmtpUsername?.trim()) return 'SMTP username is required.';
        if (!fields.emailSmtpPassword?.trim()) return 'SMTP password is required.';
        if (!fields.emailFromAddress?.trim())  return 'From address is required.';
      }
      return null;
    }

    async function onEmailNotificationsToggle(checkbox) {
      if (typeof DISABLE_EMAIL_NOTIFICATIONS !== 'undefined' && DISABLE_EMAIL_NOTIFICATIONS) {
        checkbox.checked = false;
        return;
      }
      if (!checkbox.checked) {
        saveSetting('emailNotifications', false);
        return;
      }
      const provider = appSettings.emailProvider || 'none';
      if (provider === 'none') {
        checkbox.checked = false;
        notify('error', 'No provider configured', 'Select an email provider via the settings icon before enabling notifications.');
        return;
      }
      const error = validateEmailConfig(provider, appSettings);
      if (error) {
        checkbox.checked = false;
        notify('error', 'Incomplete configuration', error);
        return;
      }
      saveSetting('emailNotifications', true);
    }

    async function saveEmailSettings() {
      const draft = {
        emailProvider:     document.getElementById('emailProvider').value,
        emailResendApiKey: document.getElementById('emailResendApiKey').value,
        emailFromAddress:  document.getElementById('emailFromAddress').value,
        emailFromName:     document.getElementById('emailFromName').value,
        emailSmtpHost:     document.getElementById('emailSmtpHost').value,
        emailSmtpPort:     parseInt(document.getElementById('emailSmtpPort').value) || 587,
        emailSmtpUsername: document.getElementById('emailSmtpUsername').value,
        emailSmtpPassword: document.getElementById('emailSmtpPassword').value,
        emailSmtpUseTls:   document.getElementById('emailSmtpUseTls').checked,
        emailQueueEnabled: document.getElementById('emailQueueEnabled').checked,
      };

      if (appSettings.emailNotifications && draft.emailProvider !== 'none') {
        const error = validateEmailConfig(draft.emailProvider, draft);
        if (error) {
          notify('error', 'Incomplete configuration', error);
          return;
        }
      }

      Object.assign(appSettings, draft);
      await saveCurrentSettings();
      closeEmailSettingsModal();
    }

    function openDoubleOptInSettingsModal() {
      const perPerm = appSettings.perPermissionEmail ?? false;
      document.getElementById('setting-perPermissionEmail').value = String(perPerm);
      document.getElementById('doubleOptInSettingsModal').style.display = 'flex';
    }

    function closeDoubleOptInSettingsModal() {
      document.getElementById('doubleOptInSettingsModal').style.display = 'none';
    }

    function toggleReveal(inputId, btn) {
      const input = document.getElementById(inputId);
      const isHidden = input.type === 'password';
      input.type = isHidden ? 'text' : 'password';
      btn.classList.toggle('active', isHidden);
    }

    function openObjectStorageModal() {
      document.getElementById('objectStorageProvider').value    = appSettings.objectStorageProvider  || 'none';
      document.getElementById('objectStorageBucket').value     = appSettings.objectStorageBucket    || '';
      document.getElementById('objectStorageRegion').value     = appSettings.objectStorageRegion    || 'us-east-1';
      document.getElementById('objectStorageEndpoint').value   = appSettings.objectStorageEndpoint  || '';
      document.getElementById('objectStorageAccessKey').value  = appSettings.objectStorageAccessKey || '';
      document.getElementById('objectStorageSecretKey').value  = appSettings.objectStorageSecretKey || '';
      document.getElementById('objectStoragePublicUrl').value  = appSettings.objectStoragePublicUrl || '';
      document.getElementById('objectStorageTestResult').textContent = '';
      showObjectStorageProviderFields(appSettings.objectStorageProvider || 'none');
      document.getElementById('objectStorageModal').style.display = 'flex';
    }

    function closeObjectStorageModal() {
      document.getElementById('objectStorageModal').style.display = 'none';
    }

    function showObjectStorageProviderFields(provider) {
      const endpointRow = document.getElementById('objectStorageEndpointRow');
      const regionRow   = document.getElementById('objectStorageRegionRow');
      if (endpointRow) endpointRow.style.display = provider === 's3' || provider === 'none' ? 'none' : '';
      if (regionRow)   regionRow.style.display   = provider === 'r2' ? 'none' : (provider === 'none' ? 'none' : '');
    }

    async function testObjectStorageConnection() {
      const btn = document.getElementById('btnTestObjectStorage');
      const result = document.getElementById('objectStorageTestResult');
      if (btn) btn.disabled = true;
      result.textContent = 'Testing…';
      result.className = '';
      try {
        const res = await apiRequest('/api/admin/settings/object-storage/test', {
          method: 'POST',
          body: {
            provider:  document.getElementById('objectStorageProvider').value,
            endpoint:  document.getElementById('objectStorageEndpoint').value,
            bucket:    document.getElementById('objectStorageBucket').value,
            region:    document.getElementById('objectStorageRegion').value,
            accessKey: document.getElementById('objectStorageAccessKey').value,
            secretKey: document.getElementById('objectStorageSecretKey').value,
          }
        });
        if (res.ok && res.data) {
          result.textContent = res.data.success
            ? `Connected (${res.data.latencyMs}ms)`
            : res.data.message;
          result.className = res.data.success ? 'test-result-ok' : 'test-result-fail';
        }
      } catch {
        result.textContent = 'Request failed.';
        result.className = 'test-result-fail';
      } finally {
        if (btn) btn.disabled = false;
      }
    }

    async function saveObjectStorageSettings() {
      const provider = document.getElementById('objectStorageProvider').value;
      const bucket   = document.getElementById('objectStorageBucket').value.trim();
      if (appSettings.objectStorage && provider !== 'none' && !bucket) {
        notify('error', 'Bucket required', 'Enter a bucket name before saving.');
        return;
      }
      appSettings.objectStorageProvider  = provider;
      appSettings.objectStorageBucket    = bucket;
      appSettings.objectStorageRegion    = document.getElementById('objectStorageRegion').value.trim();
      appSettings.objectStorageEndpoint  = document.getElementById('objectStorageEndpoint').value.trim();
      appSettings.objectStorageAccessKey = document.getElementById('objectStorageAccessKey').value;
      appSettings.objectStorageSecretKey = document.getElementById('objectStorageSecretKey').value;
      appSettings.objectStoragePublicUrl = document.getElementById('objectStoragePublicUrl').value.trim();
      await saveCurrentSettings();
      closeObjectStorageModal();
    }

    function openSubmissionFormsSettingsModal() {
      document.getElementById('setting-submissionDefaultRateLimitPerMinute').value = appSettings.submissionDefaultRateLimitPerMinute ?? 10;
      document.getElementById('setting-submissionDefaultHoneypotEnabled').checked  = appSettings.submissionDefaultHoneypotEnabled ?? true;
      document.getElementById('setting-submissionDefaultConsentRequired').checked  = appSettings.submissionDefaultConsentRequired ?? true;
      document.getElementById('submissionFormsSettingsModal').style.display = 'flex';
    }

    function closeSubmissionFormsSettingsModal() {
      document.getElementById('submissionFormsSettingsModal').style.display = 'none';
    }

    async function saveSubmissionFormsSettings() {
      appSettings.submissionDefaultRateLimitPerMinute = parseInt(document.getElementById('setting-submissionDefaultRateLimitPerMinute').value, 10) || 10;
      appSettings.submissionDefaultHoneypotEnabled    = document.getElementById('setting-submissionDefaultHoneypotEnabled').checked;
      appSettings.submissionDefaultConsentRequired    = document.getElementById('setting-submissionDefaultConsentRequired').checked;
      await saveCurrentSettings();
      closeSubmissionFormsSettingsModal();
    }

    function updateSidebarUser() { /* name fixed to Administrator per product decision */ }

    function setupUserAuthNav(role) {
      const mode = (typeof USER_AUTH_METHOD !== 'undefined') ? USER_AUTH_METHOD : '';
      const isAdmin = !mode || role === 'admin';
      const settingsNavAccount = document.getElementById('settingsNavAccount');
      const settingsNavDivider = document.getElementById('settingsNavDivider');
      const settingsNavUsers = document.getElementById('settingsNavUsers');
      if (settingsNavAccount) settingsNavAccount.style.display = '';
      if (settingsNavDivider) settingsNavDivider.style.display = '';
      if (isAdmin && settingsNavUsers) settingsNavUsers.style.display = '';
      const settingsNavApiKeys = document.getElementById('settingsNavApiKeys');
      if (isAdmin && settingsNavApiKeys) settingsNavApiKeys.style.display = '';
      const authNotice = document.getElementById('users-auth-disabled-notice');
      if (authNotice) authNotice.style.display = mode ? 'none' : '';
      const navWorkflow = document.getElementById('navWorkflow');
      if (navWorkflow) navWorkflow.style.display = isAdmin ? '' : 'none';
      if (!isAdmin) {
        for (const id of ['settingsNavGeneral', 'settingsNavModules', 'settingsNavIntegration', 'settingsNavSystem', 'settingsNavSystemDivider']) {
          const el = document.getElementById(id);
          if (el) el.style.display = 'none';
        }
      }
    }

    // USERS VIEW
    let _usersAll = [], _usersPage = 1, _usersPageSize = 25;

    async function loadUsers() {
      const result = await apiRequest('/api/admin/users');
      if (!result.ok) return;
      _usersAll = result.data || [];
      _usersPage = 1;
      renderUsersPage();
    }

    function renderUsersPage() {
      const start = (_usersPage - 1) * _usersPageSize;
      renderUsersTable(_usersAll.slice(start, start + _usersPageSize));
      renderUsersPagination();
    }

    function renderUsersPagination() {
      const total = _usersAll.length;
      const totalPages = Math.ceil(total / _usersPageSize) || 1;
      const start = total === 0 ? 0 : (_usersPage - 1) * _usersPageSize + 1;
      const end = Math.min(_usersPage * _usersPageSize, total);
      document.getElementById('usersPaginationInfo').textContent = `Showing ${start} to ${end} of ${total}`;
      document.getElementById('usersPrevBtn').disabled = _usersPage <= 1;
      document.getElementById('usersNextBtn').disabled = _usersPage >= totalPages;
      document.getElementById('usersPaginationBar').style.display = total > _usersPageSize ? 'flex' : 'none';
    }

    function changeUsersPage(dir) {
      const totalPages = Math.ceil(_usersAll.length / _usersPageSize) || 1;
      _usersPage = Math.max(1, Math.min(totalPages, _usersPage + dir));
      renderUsersPage();
    }

    function updateUsersPageSize() {
      _usersPageSize = parseInt(document.getElementById('usersPageSize').value);
      _usersPage = 1;
      renderUsersPage();
    }

    function formatRelativeTime(isoString) {
      const date = new Date(isoString);
      const diff = Date.now() - date.getTime();
      const abs = Math.abs(diff);
      const full = date.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
      let rel;
      if      (abs < 60_000)           rel = 'Just now';
      else if (abs < 3_600_000)        rel = `${Math.floor(abs / 60_000)}m ago`;
      else if (abs < 86_400_000)       rel = `${Math.floor(abs / 3_600_000)}h ago`;
      else if (abs < 7 * 86_400_000)   rel = `${Math.floor(abs / 86_400_000)}d ago`;
      else                             rel = date.toLocaleDateString();
      return `<span class="tooltip-wrapper" style="cursor:default">${rel}<span class="tooltip tooltip-above">${full}</span></span>`;
    }

    function renderUsersTable(users) {
      const tbody = document.getElementById('usersBody');
      if (!users.length) {
        tbody.innerHTML = `<tr><td colspan="6"><div class="table-empty-state">
          <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>
          <p class="empty-title">No users</p>
          <p class="empty-sub">Create the first user with the button above.</p>
        </div></td></tr>`;
        return;
      }
      tbody.innerHTML = users.map(u => `
        <tr>
          <td><strong>${sanitize(u.username)}</strong></td>
          <td><span class="status-badge ${u.role === 'admin' ? '' : 'status-badge-muted'}">${sanitize(u.role)}</span></td>
          <td>${u.isEnabled ? '<span style="color:hsl(var(--success,142 71% 45%))">Enabled</span>' : '<span style="color:hsl(var(--muted-foreground))">Disabled</span>'}</td>
          <td>${u.createdAt ? new Date(u.createdAt).toLocaleDateString() : '-'}</td>
          <td>${u.lastLoginAt ? formatRelativeTime(u.lastLoginAt) : '<span style="color:hsl(var(--muted-foreground))">Never</span>'}</td>
          <td class="col-actions">
            <div class="row-actions">
              <button class="btn-actions" onclick="event.stopPropagation();toggleMenu('userDropdown-${u.id}',this)">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="5" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="12" cy="19" r="1"/></svg>
              </button>
              <div class="dropdown-menu" id="userDropdown-${u.id}">
                <button class="dropdown-item" onclick="openRenameUserModal('${u.id}', '${sanitize(u.username)}')">Rename</button>
                <button class="dropdown-item" onclick="openUserPasswordModal('${u.id}', '${sanitize(u.username)}')">Change Password</button>
                <button class="dropdown-item" onclick="openUserRoleModal('${u.id}', '${sanitize(u.username)}', '${u.role}')">Change Role</button>
                <button class="dropdown-item" onclick="regenerateUserApiKey('${u.id}', '${sanitize(u.username)}')">Regenerate API Key</button>
                <button class="dropdown-item dropdown-item--destructive" onclick="deleteUser('${u.id}', '${sanitize(u.username)}')">Delete</button>
              </div>
            </div>
          </td>
        </tr>
      `).join('');
      tbody.querySelectorAll('tr').forEach((row, i) => {
        row.style.animationDelay = `${Math.min(i * 30, 150)}ms`;
      });
    }

    let userActionTargetId = null;

    function openUserPasswordModal(id, username) {
      if (username === currentUsername) {
        notify('info', 'Use Account Settings', 'To change your own password, go to Settings → Account.');
        return;
      }
      userActionTargetId = id;
      document.getElementById('userPasswordModalDesc').textContent = `Set a new password for ${username}.`;
      document.getElementById('userPasswordInput').value = '';
      document.getElementById('userPasswordModal').style.display = 'flex';
      setTimeout(() => document.getElementById('userPasswordInput').focus(), 50);
    }

    function closeUserPasswordModal() {
      document.getElementById('userPasswordModal').style.display = 'none';
    }

    async function saveUserPassword() {
      const pwd = document.getElementById('userPasswordInput').value;
      if (pwd.length < 12) { notify('error', 'Password too short', 'Minimum 12 characters required.'); return; }
      const btn = document.getElementById('userPasswordSaveBtn');
      btn.classList.add('is-loading'); btn.disabled = true;
      const result = await apiRequest(`/api/admin/users/${userActionTargetId}/password`, { method: 'PATCH', body: { newPassword: pwd } });
      btn.classList.remove('is-loading'); btn.disabled = false;
      if (result.ok) { closeUserPasswordModal(); notify('success', 'Password updated', 'The password has been changed.'); }
      else { notify('error', 'Password Update Failed', result.data?.error || 'Failed to update password.'); }
    }

    function openUserRoleModal(id, username, currentRole) {
      userActionTargetId = id;
      document.getElementById('userRoleModalDesc').textContent = `Change role for ${username}.`;
      document.getElementById('userRoleSelect').value = currentRole;
      document.getElementById('userRoleModal').style.display = 'flex';
    }

    function closeUserRoleModal() {
      document.getElementById('userRoleModal').style.display = 'none';
    }

    async function saveUserRole() {
      const role = document.getElementById('userRoleSelect').value;
      const btn = document.getElementById('userRoleSaveBtn');
      btn.classList.add('is-loading'); btn.disabled = true;
      const result = await apiRequest(`/api/admin/users/${userActionTargetId}/role`, { method: 'PATCH', body: { role } });
      btn.classList.remove('is-loading'); btn.disabled = false;
      if (result.ok) { closeUserRoleModal(); notify('success', 'Role updated', 'The role has been changed.'); loadUsers(); }
      else { notify('error', 'Role Update Failed', result.data?.error || 'Failed to update role.'); }
    }

    function openRenameUserModal(id, username) {
      userActionTargetId = id;
      document.getElementById('renameUserModalDesc').textContent = `Current username: ${username}`;
      document.getElementById('renameUserInput').value = username;
      document.getElementById('renameUserModal').style.display = 'flex';
      setTimeout(() => document.getElementById('renameUserInput').focus(), 50);
    }

    function closeRenameUserModal() {
      document.getElementById('renameUserModal').style.display = 'none';
    }

    async function saveRenameUser() {
      const username = document.getElementById('renameUserInput').value.trim();
      if (!username) { notify('error', 'Validation', 'Username is required.'); return; }
      const btn = document.getElementById('renameUserSaveBtn');
      btn.classList.add('is-loading'); btn.disabled = true;
      const result = await apiRequest(`/api/admin/users/${userActionTargetId}/username`, { method: 'PATCH', body: { username } });
      btn.classList.remove('is-loading'); btn.disabled = false;
      if (result.ok) { closeRenameUserModal(); notify('success', 'User renamed', 'The username has been updated.'); loadUsers(); }
      else { notify('error', 'Rename Failed', result.data?.error || 'Failed to rename user.'); }
    }

    async function deleteUser(id, username) {
      openConfirmModal(
        'Delete User',
        `Delete "${username}"? This cannot be undone.`,
        'Delete',
        async () => {
          const result = await apiRequest(`/api/admin/users/${id}`, { method: 'DELETE' });
          if (result.ok) { notify('success', 'User deleted', `${username} has been removed.`); loadUsers(); }
          else { notify('error', 'Delete Failed', result.data?.error || 'Failed to delete user.'); }
        }
      );
    }

    async function regenerateUserApiKey(id, username) {
      openConfirmModal(
        'Regenerate API Key',
        `Regenerate the API key for "${username}"? The old key will stop working immediately.`,
        'Regenerate',
        async () => {
          const result = await apiRequest(`/api/admin/users/${id}/api-key`, { method: 'POST' });
          if (result.ok) {
            document.getElementById('userApiKeyRevealDesc').textContent = `New API key for ${username} — copy it now, it will not be shown again.`;
            document.getElementById('userApiKeyRevealOutput').textContent = result.data.apiKey;
            document.getElementById('userApiKeyRevealModal').style.display = 'flex';
          } else { notify('error', 'Regenerate Failed', result.data?.error || 'Failed to regenerate API key.'); }
        }
      );
    }

    function closeUserApiKeyRevealModal() {
      document.getElementById('userApiKeyRevealModal').style.display = 'none';
    }

    function copyUserApiKey() {
      const key = document.getElementById('userApiKeyRevealOutput').textContent;
      if (!key) return;
      navigator.clipboard.writeText(key).then(() => notify('success', 'Copied', 'API key copied to clipboard.'));
    }

    // CONFIRM MODAL
    let _confirmAction = null;

    function openConfirmModal(title, message, actionLabel, action) {
      _confirmAction = action;
      document.getElementById('confirmModalTitle').textContent = title;
      document.getElementById('confirmModalMessage').innerHTML = message;
      document.querySelector('#confirmModalBtn .btn-label').textContent = actionLabel;
      document.getElementById('confirmModal').style.display = 'flex';
    }

    function closeConfirmModal() {
      document.getElementById('confirmModal').style.display = 'none';
      _confirmAction = null;
    }

    async function executeConfirmAction() {
      if (_confirmAction) {
        const action = _confirmAction;
        closeConfirmModal();
        await action();
      }
    }

    // ADD USER MODAL
    let _newUserApiKey = '';

    function openAddUserModal() {
      document.getElementById('newUserUsername').value = '';
      document.getElementById('newUserPassword').value = '';
      document.getElementById('newUserConfirmPassword').value = '';
      document.getElementById('newUserRole').value = 'user';
      document.getElementById('newUserApiKeySection').style.display = 'none';
      document.getElementById('newUserApiKeyOutput').textContent = '';
      _newUserApiKey = '';
      document.getElementById('addUserBtn').disabled = false;
      document.getElementById('addUserModal').style.display = 'flex';
    }

    function closeAddUserModal() {
      document.getElementById('addUserModal').style.display = 'none';
    }

    async function createUser() {
      const username = document.getElementById('newUserUsername').value.trim();
      const password = document.getElementById('newUserPassword').value;
      const confirm = document.getElementById('newUserConfirmPassword').value;
      const role = document.getElementById('newUserRole').value;

      if (!username) { notify('error', 'Validation', 'Username is required.'); return; }
      if (password !== confirm) { notify('error', 'Validation', 'Passwords do not match.'); return; }

      const btn = document.getElementById('addUserBtn');
      btn.classList.add('is-loading');
      btn.disabled = true;

      const result = await apiRequest('/api/admin/users', {
        method: 'POST',
        body: { username, password, role }
      });

      btn.classList.remove('is-loading');
      btn.disabled = false;

      if (result.ok) {
        _newUserApiKey = result.data.apiKey;
        document.getElementById('newUserApiKeyOutput').textContent = _newUserApiKey;
        document.getElementById('newUserApiKeySection').style.display = 'block';
        document.querySelector('#addUserModal .modal-actions .btn-outline').textContent = 'Done';
        document.getElementById('addUserBtn').style.display = 'none';
        notify('success', 'User created', `${username} has been created. Copy the API key now.`);
        loadUsers();
      } else { notify('error', 'Create Failed', result.data?.error || 'Failed to create user.'); }
    }

    function copyNewUserApiKey() {
      if (!_newUserApiKey) return;
      navigator.clipboard.writeText(_newUserApiKey).then(() => notify('success', 'Copied', 'API key copied to clipboard.'));
    }

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

    // ACCOUNT VIEW
    async function loadAccount() {
      const result = await apiRequest('/api/admin/users/me');
      if (!result.ok) return;
      const me = result.data;
      const usernameEl = document.getElementById('accountUsername');
      if (usernameEl) usernameEl.textContent = me.username || '';
      document.getElementById('accountRoleBadge').innerHTML =
        `<span class="status-badge ${me.role === 'admin' ? '' : 'status-badge-muted'}">${sanitize(me.role || 'admin')}</span>`;
      sessionStorage.setItem('beacon_user_id', me.id || '');
      sessionStorage.setItem('beacon_user_role', me.role || '');
      const hasDbRecord = !!me.id;
      const itemsEl = document.getElementById('accountSecurityItems');
      const noticeEl = document.getElementById('accountGlobalAdminNotice');
      if (itemsEl) itemsEl.style.display = hasDbRecord ? '' : 'none';
      if (noticeEl) noticeEl.style.display = hasDbRecord ? 'none' : '';
    }

    function openAccountPasswordModal() {
      document.getElementById('accountCurrentPassword').value = '';
      document.getElementById('accountNewPassword').value = '';
      document.getElementById('accountConfirmPassword').value = '';
      document.getElementById('accountPasswordModal').style.display = 'flex';
      setTimeout(() => document.getElementById('accountCurrentPassword')?.focus(), 50);
    }

    function closeAccountPasswordModal() {
      document.getElementById('accountPasswordModal').style.display = 'none';
    }

    async function saveAccountPassword() {
      const currentPwd = document.getElementById('accountCurrentPassword').value;
      const newPwd = document.getElementById('accountNewPassword').value;
      const confirmPwd = document.getElementById('accountConfirmPassword').value;

      if (newPwd !== confirmPwd) { notify('error', 'Validation', 'Passwords do not match.'); return; }
      if (newPwd.length < 12) { notify('error', 'Validation', 'Password must be at least 12 characters.'); return; }

      const userId = sessionStorage.getItem('beacon_user_id');
      if (!userId) { notify('error', 'Session Error', 'Could not determine your user ID. Try signing out and back in.'); return; }

      const body = { newPassword: newPwd };
      if (currentPwd) body.currentPassword = currentPwd;

      const btn = document.getElementById('accountPasswordSaveBtn');
      btn.classList.add('is-loading'); btn.disabled = true;
      const result = await apiRequest(`/api/admin/users/${userId}/password`, { method: 'PATCH', body });
      btn.classList.remove('is-loading'); btn.disabled = false;
      if (result.ok) { closeAccountPasswordModal(); notify('success', 'Password updated', 'Your password has been changed.'); }
      else { notify('error', 'Password Update Failed', result.data?.error || 'Failed to change password.'); }
    }

    async function regenerateOwnApiKey() {
      const userId = sessionStorage.getItem('beacon_user_id');
      if (!userId) { notify('error', 'Session Error', 'Could not determine your user ID. Try signing out and back in.'); return; }
      openConfirmModal(
        'Regenerate API Key',
        'Are you sure you want to regenerate this? Your current key will be invalidated right away.',
        'Regenerate',
        async () => {
          const result = await apiRequest(`/api/admin/users/${userId}/api-key`, { method: 'POST' });
          if (result.ok) {
            document.getElementById('userApiKeyRevealDesc').textContent = 'Copy your new API key now, it won\'t be shown again. Your old key is now revoked.';
            document.getElementById('userApiKeyRevealOutput').textContent = result.data.apiKey;
            document.getElementById('userApiKeyRevealModal').style.display = 'flex';
          } else { notify('error', 'Regenerate Failed', result.data?.error || 'Failed to regenerate API key.'); }
        }
      );
    }

    init();
