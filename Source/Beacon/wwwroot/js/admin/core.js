    // admin.js is served by concatenating all files in js/admin/ in the order defined in Program.cs.
    // edit these files directly, do not edit the assembled output!!
    // make sure to update `app.MapGet("/js/admin.js", async (HttpContext context)` in Program.cs if you add new files

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
    let searchType = 'email';
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
        searchType = params.get('qtype') || 'email';
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
        subSearchType = params.get('qtype') || 'email';
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

