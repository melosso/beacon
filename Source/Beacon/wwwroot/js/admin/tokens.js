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
      document.getElementById('tokenName').value = '';
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

        const nameVal = document.getElementById('tokenName').value.trim() || undefined;
        const result = await apiRequest('/api/tokens/generate', {
          method: 'POST',
          body: [{ bucket, email, name: nameVal, permissions, expiryDays, allowReplay, language, customFields, skipConfirmationEmail }]
        });

        if (result.ok) {
          const tokenUrl = `${PUBLIC_URL || API_BASE}/u/${result.data[0].token}`;
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
      if (!currentBucketArchived) {
        notify('error', 'Archive first', 'A bucket must be archived before it can be removed. Open Options → Archive Bucket, then try again.');
        return;
      }
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

