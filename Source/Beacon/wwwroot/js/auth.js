    // ========== SANITIZATION ==========
    function sanitize(str) {
      if (str === null || str === undefined) return '';
      const div = document.createElement('div');
      div.textContent = String(str);
      return div.innerHTML;
    }

    // ========== TOAST NOTIFICATIONS ==========
    function notify(type, title, message = '') {
      const container = document.getElementById('toastContainer');
      const toast = document.createElement('div');
      toast.className = `toast ${type}`;

      const icons = {
        success: '<svg class="toast-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 6L9 17l-5-5"/></svg>',
        error: '<svg class="toast-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/></svg>',
        warning: '<svg class="toast-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>'
      };

      toast.innerHTML = `
        ${icons[type] || icons.error}
        <div class="toast-content">
          <div class="toast-title">${sanitize(title)}</div>
          ${message ? `<div class="toast-message">${sanitize(message)}</div>` : ''}
        </div>
      `;

      container.appendChild(toast);

      setTimeout(() => {
        toast.classList.add('hiding');
        setTimeout(() => toast.remove(), 200);
      }, 4000);
    }

    // ========== AUTH FORM UI ==========
    let _authFormMode = 'password'; // 'password' | 'apikey' — only used in 'both' mode

    function _applyAuthFormMode(isPassword) {
      const usernameInput = document.getElementById('usernameInput');
      const apiKeyInput = document.getElementById('apiKeyInput');
      const desc = document.getElementById('authOverlayDesc');
      const toggleBtn = document.getElementById('authToggleBtn')?.querySelector('button');
      if (isPassword) {
        usernameInput.style.display = '';
        apiKeyInput.placeholder = 'Password';
        apiKeyInput.autocomplete = 'current-password';
        if (desc) desc.textContent = 'Insert your access key to interface with the core. Your username and password will do.';
        if (toggleBtn) toggleBtn.innerHTML = `<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" style="flex-shrink:0"><circle cx="7.5" cy="15.5" r="5.5"/><path d="m21 2-9.6 9.6"/><path d="m15.5 7.5 3 3L22 7l-3-3"/></svg> Access Token`;
      } else {
        usernameInput.style.display = 'none';
        apiKeyInput.placeholder = 'API Key';
        apiKeyInput.autocomplete = 'off';
        if (desc) desc.textContent = 'Enter your personal API key to sign in.';
        if (toggleBtn) toggleBtn.innerHTML = `<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" style="flex-shrink:0"><circle cx="12" cy="8" r="4"/><path d="M20 21a8 8 0 0 0-16 0"/></svg> Password`;
      }
    }

    function setupAuthOverlay() {
      const mode = (typeof USER_AUTH_METHOD !== 'undefined') ? USER_AUTH_METHOD : '';
      const isBoth = mode === 'both';
      if (isBoth) {
        // Restore the user's last-used login method
        const saved = localStorage.getItem('beacon_auth_preference');
        if (saved === 'password' || saved === 'apikey') _authFormMode = saved;
        _applyAuthFormMode(_authFormMode === 'password');
      } else if (mode === 'user') {
        _applyAuthFormMode(true);
      } else if (mode === 'api') {
        _applyAuthFormMode(false);
      }
      document.getElementById('authToggleBtn').style.display = isBoth ? '' : 'none';
      document.getElementById('authToggleSep').style.display = isBoth ? 'flex' : 'none';
    }

    function toggleAuthMode(e) {
      e.preventDefault();
      _authFormMode = (_authFormMode === 'password') ? 'apikey' : 'password';
      localStorage.setItem('beacon_auth_preference', _authFormMode);
      document.getElementById('authError').style.display = 'none';
      document.getElementById('apiKeyInput').value = '';
      _applyAuthFormMode(_authFormMode === 'password');
    }

    // ========== AUTHENTICATE ==========
    // Posts credentials → server sets HttpOnly cookie → stores UI state → redirects to /admin
    async function authenticate() {
      const mode = (typeof USER_AUTH_METHOD !== 'undefined') ? USER_AUTH_METHOD : '';
      const btn = document.getElementById('signInBtn');
      let body;

      if (mode === 'user' || (mode === 'both' && _authFormMode === 'password')) {
        const username = document.getElementById('usernameInput').value.trim();
        const password = document.getElementById('apiKeyInput').value;
        if (!username || !password) {
          notify('warning', 'Input Required', 'Please enter your username and password.');
          return;
        }
        body = { username, password };
      } else {
        const key = document.getElementById('apiKeyInput').value.trim();
        if (!key) {
          notify('warning', 'Input Required', 'Please enter an API key.');
          return;
        }
        body = { apiKey: key };
      }

      btn.classList.add('is-loading');
      btn.disabled = true;

      try {
        const res = await fetch(`${API_BASE}/api/admin/auth`, {
          method: 'POST',
          credentials: 'include',  // Receive the HttpOnly cookie cross-origin
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(body)
        });

        if (!res.ok) {
          document.getElementById('authError').style.display = 'block';
          document.getElementById('apiKeyInput').value = '';
          notify('error', 'Authentication Failed', 'Invalid credentials.');
          return;
        }

        const data = await res.json();
        // Store non-sensitive UI state (role, username, expiry) — token is in HttpOnly cookie
        sessionStorage.setItem('beacon_user_role', data.role || 'admin');
        sessionStorage.setItem('beacon_username', data.username || '');
        if (data.expiresAt) sessionStorage.setItem('beacon_jwt_exp', data.expiresAt);

        window.location.href = '/admin';
      } catch (e) {
        notify('error', 'Network Error', 'Could not connect to server.');
      } finally {
        btn.classList.remove('is-loading');
        btn.disabled = false;
      }
    }

    // ========== LOGOUT (used by admin.js via shared scope) ==========
    async function logout() {
      try {
        await fetch(`${API_BASE}/api/admin/auth/logout`, {
          method: 'POST',
          credentials: 'include'
        });
      } catch { /* ignore network errors on logout */ }
      sessionStorage.removeItem('beacon_user_role');
      sessionStorage.removeItem('beacon_username');
      sessionStorage.removeItem('beacon_jwt_exp');
      sessionStorage.removeItem('beacon_buckets');
      sessionStorage.removeItem('beacon_user_id');
      window.location.href = '/admin/logout';
    }
