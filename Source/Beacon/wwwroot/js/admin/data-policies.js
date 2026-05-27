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
    let auditFilterType = 'id'; // 'id' | 'email'
    let auditFilterEmail = null; // used when auditFilterType === 'email'

    async function loadAudit() {
      const params = new URLSearchParams();
      if (auditFilterBucket) params.set('bucket', auditFilterBucket);
      if (auditFilterIdentity && auditFilterType === 'id') params.set('emailHash', auditFilterIdentity);
      if (auditFilterEmail && auditFilterType === 'email') params.set('emailSearch', auditFilterEmail);
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
      const hasIdentity = (auditFilterIdentity || auditFilterEmail) ? 'has-search' : '';
      const hasBucket = auditFilterBucket ? 'has-search' : '';
      const activeFilterVal = auditFilterType === 'email' ? (auditFilterEmail || '') : (auditFilterIdentity || '');
      thead.innerHTML = `
        <th>Timestamp</th>
        <th>
          <div class="column-search">
            <span>E-mail / ID</span>
            <button class="search-trigger ${hasIdentity}" id="auditIdentitySearchTrigger" onclick="toggleAuditIdentityPopover(event)" title="Filter by identity">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="8"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line></svg>
            </button>
            <div class="search-popover" id="auditIdentityPopover" style="min-width:280px">
              <div class="search-type-toggle">
                <button class="search-type-btn ${auditFilterType === 'id' ? 'active' : ''}" onclick="event.stopPropagation();setAuditFilterType('id')">By ID</button>
                <button class="search-type-btn ${auditFilterType === 'email' ? 'active' : ''}" onclick="event.stopPropagation();setAuditFilterType('email')">By Email</button>
              </div>
              <label>${auditFilterType === 'id' ? 'Filter by identity hash (partial)' : 'Filter by email (exact)'}</label>
              <input type="text" id="auditIdentityInput" placeholder="${auditFilterType === 'id' ? 'e.g., a1b2c3d4' : 'e.g., user@example.com'}" value="${sanitize(activeFilterVal)}" onkeydown="if(event.key==='Enter')applyAuditIdentityFilter()">
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

    function setAuditFilterType(type) {
      auditFilterType = type;
      document.querySelectorAll('#auditIdentityPopover .search-type-btn').forEach(btn => {
        btn.classList.toggle('active', btn.textContent.trim() === (type === 'id' ? 'By ID' : 'By Email'));
      });
      const label = document.querySelector('#auditIdentityPopover label');
      if (label) label.textContent = type === 'id' ? 'Filter by identity hash (partial)' : 'Filter by email (exact)';
      const input = document.getElementById('auditIdentityInput');
      if (input) { input.placeholder = type === 'id' ? 'e.g., a1b2c3d4' : 'e.g., user@example.com'; input.value = ''; input.focus(); }
    }

    function applyAuditIdentityFilter() {
      const val = document.getElementById('auditIdentityInput')?.value.trim();
      if (auditFilterType === 'email') {
        auditFilterEmail = val || null;
        auditFilterIdentity = null;
      } else {
        auditFilterIdentity = val || null;
        auditFilterEmail = null;
      }
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
      auditFilterEmail = null;
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
      const sourceLabel = { Url: 'User', Api: 'API', Admin: 'Administrator' }[e.source] || e.source;
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
      const identityInner = e.email
        ? `<span class="email-text" style="cursor:pointer">${sanitize(e.email)}</span>`
        : `<span class="email-hash" style="cursor:pointer">${sanitize(e.displayId)}</span>`;
      return `
        <tr>
          <td>${sanitize(formatDate(e.changedAt))}</td>
          <td>
            <span class="tooltip-wrapper"
              onclick="auditIdentityClick('${sanitize(e.emailHash)}')"
              ondblclick="auditIdentityDblClick('${sanitize(e.emailHash)}')">
              ${identityInner}
              <span class="tooltip">Click to filter · double-click to copy ID</span>
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
    // /Audit 

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

