    // OVERVIEW
    let webhookBuckets = new Set();

    async function loadOverview(refresh = false) {
      if (refresh) {
        const result = await apiRequest('/api/admin/buckets');
        if (!result.ok) return;
        buckets = result.data || [];
        renderBucketsSidebar();
      }

      // Fetch which buckets have webhooks configured; also refresh brand identities if needed
      const [whResult, biResult] = await Promise.all([
        apiRequest('/api/admin/webhooks/buckets'),
        _brandIdentities.length === 0 ? apiRequest('/api/admin/brand-identities') : Promise.resolve({ ok: true, data: _brandIdentities })
      ]);
      if (whResult.ok) webhookBuckets = new Set(whResult.data || []);
      if (biResult.ok && biResult.data) _brandIdentities = biResult.data;

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

      body.innerHTML = buckets.map((b, idx) => {
        const brandId = getBrandIdentityForBucket(b.name);
        const brandAccent = brandId?.settings?.primaryAccent || null;
        return `
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
                ${brandId ? `<span class="tooltip-wrapper">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="${brandAccent || 'hsl(var(--muted-foreground))'}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="vertical-align:middle">
                    <circle cx="13.5" cy="6.5" r=".5" fill="${brandAccent || 'hsl(var(--muted-foreground))'}"/>
                    <circle cx="17.5" cy="10.5" r=".5" fill="${brandAccent || 'hsl(var(--muted-foreground))'}"/>
                    <circle cx="8.5" cy="7.5" r=".5" fill="${brandAccent || 'hsl(var(--muted-foreground))'}"/>
                    <circle cx="6.5" cy="12.5" r=".5" fill="${brandAccent || 'hsl(var(--muted-foreground))'}"/>
                    <path d="M12 2C6.5 2 2 6.5 2 12a10 10 0 0 0 10 10c.926 0 1.648-.746 1.648-1.688 0-.437-.18-.835-.437-1.125-.29-.289-.438-.652-.438-1.125a1.64 1.64 0 0 1 1.668-1.668h1.996c3.051 0 5.555-2.503 5.555-5.554C21.965 6.012 17.461 2 12 2z"/>
                  </svg>
                  <span class="tooltip tooltip-above tooltip-right">${escHtml(brandId.name)}</span>
                </span>` : ''}
              </span>
              <span class="tooltip-wrapper">
                <button class="btn-actions" onclick="toggleOverviewMenu(event, ${idx})">:</button>
                <span class="tooltip tooltip-above tooltip-right">Actions</span>
              </span>
              <div class="dropdown-menu" id="overviewMenu-${idx}">
                <button class="dropdown-item" onclick="showBucket('${sanitize(b.name)}')">View Records</button>
                <button class="dropdown-item" onclick="${b.isArchived ? `showUnarchiveModal('${sanitize(b.name)}')` : `showArchiveModal('${sanitize(b.name)}')`}">${b.isArchived ? 'Unarchive Bucket' : 'Archive Bucket'}</button>
                <button class="dropdown-item${b.isArchived ? '' : ' disabled'}" ${b.isArchived ? `onclick="initiateBucketRemoval('${sanitize(b.name)}')"` : 'onclick="notify(\'error\',\'Archive first\',\'Archive this bucket before removing it.\')"'}>Remove Bucket</button>
              </div>
            </div>
          </td>
        </tr>
      `;
      }).join('');
    }

    let currentBucketPermissions = [];

    async function copyBucketName() {
        if (!currentBucket) return;
        const tooltipElement = document.getElementById('bucketTooltip');
        try {
            await clipboardWrite(currentBucket);
            tooltipElement.innerText = "Copied!";
            setTimeout(() => { tooltipElement.innerText = currentBucket; }, 2000);
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
      const archivedBadge = document.getElementById('archivedBadge');
      if (archivedBadge) archivedBadge.style.display = currentBucketArchived ? '' : 'none';

      const removeBtn = document.getElementById('removeBucketBtn');
      const removeTip = document.getElementById('removeBucketTooltip');
      if (removeBtn) {
        removeBtn.disabled = !currentBucketArchived;
        removeBtn.style.opacity = currentBucketArchived ? '' : '0.4';
        if (removeTip) removeTip.textContent = currentBucketArchived ? 'Permanently delete this bucket' : 'Archive this bucket first to enable removal';
      }

      const brandBadge = document.getElementById('bucketBrandBadge');
      const brandIdentity = details.brandIdentity;
      if (brandIdentity) {
        const accent = brandIdentity.accent;
        brandBadge.textContent = brandIdentity.name;
        brandBadge.style.background = accent || '';
        brandBadge.style.color = accent ? contrastFg(accent) : '';
        brandBadge.style.display = '';
      } else {
        brandBadge.style.display = 'none';
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

