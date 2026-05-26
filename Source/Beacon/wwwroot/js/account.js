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

