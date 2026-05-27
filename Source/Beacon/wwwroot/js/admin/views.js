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

      document.getElementById('subTitle').textContent = 'Subscribers';
      const badge = document.getElementById('subBadge');
      badge.textContent = 'Overview';
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

