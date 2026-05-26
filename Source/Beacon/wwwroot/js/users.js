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

