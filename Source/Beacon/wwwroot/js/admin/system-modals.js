    function parsePromoMarkdown(text) {
      if (!text) return '';
      return text
        .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
        .replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>')
        .replace(/__(.*?)__/g, '<strong>$1</strong>')
        .replace(/\*(.*?)\*/g, '<em>$1</em>')
        .replace(/_(.*?)_/g, '<em>$1</em>')
        .replace(/\[(.*?)\]\(([^)]*)\)/g, (_, linkText, url) => {
          // Decode entities added by the HTML-escape pass so we inspect the real URL
          const decoded = url.replace(/&amp;/g, '&').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>');
          // Reject URLs with characters that break out of a double-quoted href attribute
          if (/["'<>]/.test(decoded)) return linkText;
          const external = /^(https?:\/\/|\/\/)/i.test(decoded);
          const safe = external || /^(\/|#|mailto:|tel:)/i.test(decoded);
          if (!safe) return linkText;
          // Re-encode only & for valid HTML; " is already rejected
          const safeUrl = decoded.replace(/&/g, '&amp;');
          const attrs = external ? ' target="_blank" rel="noopener noreferrer"' : '';
          return `<a href="${safeUrl}"${attrs}>${linkText}</a>`;
        });
    }

    function initPromoBar() {
      if (sessionStorage.getItem('beacon_promo_dismissed')) return;
      updatePromoBar();
    }

    function updatePromoBar() {
      let bar = document.getElementById('adminPromoBar');
      if (!appSettings.promoBarEnabled || !appSettings.promoBar.trim()) {
        if (bar) bar.classList.remove('is-visible');
        return;
      }
      if (!bar) {
        bar = document.createElement('div');
        bar.id = 'adminPromoBar';
        bar.className = 'admin-promo-bar';
        const appShell = document.querySelector('.app-shell');
        if (appShell) document.body.insertBefore(bar, appShell);
      }
      const html = parsePromoMarkdown(appSettings.promoBar);
      const dismissable = appSettings.promoBarDismissable ?? true;
      bar.innerHTML = `<div class="admin-promo-bar-content">${html}</div>${dismissable ? '<button class="promo-bar-close" onclick="dismissPromoBar()" title="Close"><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg></button>' : ''}`;
      requestAnimationFrame(() => bar.classList.add('is-visible'));
    }

    function dismissPromoBar() {
      const bar = document.getElementById('adminPromoBar');
      if (bar) bar.classList.remove('is-visible');
      sessionStorage.setItem('beacon_promo_dismissed', '1');
    }

    async function onPromoBarToggle(checkbox) {
      if (checkbox.checked && !appSettings.promoBar.trim()) {
        checkbox.checked = false;
        notify('error', 'No announcement content', 'Add announcement text via the settings gear before enabling.');
        return;
      }
      saveSetting('promoBarEnabled', checkbox.checked);
      await saveCurrentSettings();
      updatePromoBar();
    }

    function openPromoBarModal() {
      const el = document.getElementById('modal-promoBar');
      el.value = appSettings.promoBar;
      document.getElementById('modal-promoBar-count').textContent = `${el.value.length} / 500`;
      document.getElementById('modal-promoBarDismissable').checked = appSettings.promoBarDismissable ?? true;
      document.getElementById('modal-promoBarShowOnLogin').checked = appSettings.promoBarShowOnLogin ?? false;
      document.getElementById('promoBarModal').style.display = 'flex';
    }

    function closePromoBarModal() {
      document.getElementById('promoBarModal').style.display = 'none';
    }

    async function savePromoBarSettings() {
      const val = document.getElementById('modal-promoBar').value;
      appSettings.promoBar = val;
      appSettings.promoBarDismissable = document.getElementById('modal-promoBarDismissable').checked;
      appSettings.promoBarShowOnLogin = document.getElementById('modal-promoBarShowOnLogin').checked;
      if (!val.trim()) appSettings.promoBarEnabled = false;
      const toggle = document.getElementById('setting-promoBarEnabled');
      if (toggle) toggle.checked = appSettings.promoBarEnabled;
      await saveCurrentSettings();
      sessionStorage.removeItem('beacon_promo_dismissed');
      updatePromoBar();
      closePromoBarModal();
    }

    async function onLoginFooterToggle(checkbox) {
      if (checkbox.checked && !appSettings.loginFooter.trim()) {
        checkbox.checked = false;
        notify('error', 'No footer content', 'Add footer text via the settings icon before enabling.');
        return;
      }
      saveSetting('loginFooterEnabled', checkbox.checked);
      await saveCurrentSettings();
    }

    function openLoginFooterModal() {
      const el = document.getElementById('modal-loginFooter');
      el.value = appSettings.loginFooter;
      document.getElementById('modal-loginFooter-count').textContent = `${el.value.length} / 500`;
      document.getElementById('loginFooterModal').style.display = 'flex';
    }

    function closeLoginFooterModal() {
      document.getElementById('loginFooterModal').style.display = 'none';
    }

    async function saveLoginFooterSettings() {
      const val = document.getElementById('modal-loginFooter').value;
      appSettings.loginFooter = val;
      if (!val.trim()) appSettings.loginFooterEnabled = false;
      const toggle = document.getElementById('setting-loginFooterEnabled');
      if (toggle) toggle.checked = appSettings.loginFooterEnabled;
      await saveCurrentSettings();
      closeLoginFooterModal();
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

    function updateSidebarUser() {
      const name = currentUsername || 'Admin';
      const nameEl = document.getElementById('sidebarUserName');
      const avatarEl = document.getElementById('sidebarUserAvatar');
      if (nameEl) nameEl.textContent = name.charAt(0).toUpperCase() + name.slice(1);
      if (avatarEl) avatarEl.textContent = name.charAt(0).toUpperCase();
    }

    function setupUserAuthNav(role) {
      const mode = (typeof USER_AUTH_METHOD !== 'undefined') ? USER_AUTH_METHOD : '';
      const isAdmin = !mode || role === 'admin';
      const settingsNavAccount = document.getElementById('settingsNavAccount');
      const settingsNavDivider = document.getElementById('settingsNavDivider');
      const settingsNavUsers = document.getElementById('settingsNavUsers');
      if (settingsNavAccount) settingsNavAccount.style.display = '';
      if (isAdmin && settingsNavDivider) settingsNavDivider.style.display = '';
      if (isAdmin && settingsNavUsers) settingsNavUsers.style.display = '';
      const settingsNavApiKeys = document.getElementById('settingsNavApiKeys');
      if (isAdmin && settingsNavApiKeys) settingsNavApiKeys.style.display = '';
      const authNotice = document.getElementById('users-auth-disabled-notice');
      if (authNotice) authNotice.style.display = mode ? 'none' : '';
      const navWorkflow = document.getElementById('navWorkflow');
      if (navWorkflow) navWorkflow.style.display = isAdmin ? '' : 'none';
      if (!isAdmin) {
        for (const id of ['settingsNavGeneral', 'settingsNavModules', 'settingsNavDataPolicies', 'settingsNavIntegration', 'settingsNavSystem', 'settingsNavSystemDivider', 'settingsNavConnectors', 'settingsNavPersonalisation']) {
          const el = document.getElementById(id);
          if (el) el.style.display = 'none';
        }
      }
    }

