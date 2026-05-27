    // SETTINGS
    // Paired files — keep in sync when adding/removing settings:
    //   admin/views/settings.html      (UI cards and toggles)
    //   admin/modals/system-settings.html  (modal dialogs)
    //   Beacon.Core/Services/ISystemConfigurationService.cs  (SystemConfig model)
    //   Beacon/Api/AdminEndpoints.cs   (validation + save endpoint)
    const settingsDefaults = {
      allowDbLookup: true,
      enableCaching: false,
      theme: 'system',
      font: 'inter',
      defaultLanguage: 'en',
      uiLanguage: 'en',
      loginFooterEnabled: false,
      loginFooter: '',
      promoBarEnabled: false,
      promoBar: '',
      promoBarDismissable: true,
      promoBarShowOnLogin: false,
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
        appSettings.allowDbLookup = res.data.allowDbLookup ?? true;
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
        appSettings.loginFooterEnabled = res.data.loginFooterEnabled ?? false;
        appSettings.loginFooter = res.data.loginFooter ?? '';
        const footerToggle = document.getElementById('setting-loginFooterEnabled');
        if (footerToggle) footerToggle.checked = appSettings.loginFooterEnabled;
        appSettings.promoBarEnabled = res.data.promoBarEnabled ?? false;
        appSettings.promoBar = res.data.promoBar ?? '';
        appSettings.promoBarDismissable = res.data.promoBarDismissable ?? true;
        appSettings.promoBarShowOnLogin = res.data.promoBarShowOnLogin ?? false;
        const promoToggle = document.getElementById('setting-promoBarEnabled');
        if (promoToggle) promoToggle.checked = appSettings.promoBarEnabled;
        initPromoBar();
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

    const _adminOnlySections = new Set(['general', 'modules', 'data-policies', 'integration', 'system', 'users', 'api-keys', 'connectors', 'personalisation']);

    function showSettingsSection(section, pushState = true) {
      const mode = (typeof USER_AUTH_METHOD !== 'undefined') ? USER_AUTH_METHOD : '';
      if (mode && currentUserRole !== 'admin' && _adminOnlySections.has(section)) {
        showSettingsSection(mode ? 'appearance' : section, pushState);
        return;
      }
      document.querySelectorAll('.settings-section').forEach(s => s.classList.remove('active'));
      document.querySelectorAll('.settings-subnav-item').forEach(i => i.classList.remove('active'));
      document.getElementById(`settings-section-${section}`)?.classList.add('active');
      const labels = { general: 'General settings', modules: 'General settings', 'data-policies': 'General settings', appearance: 'Preferences', system: 'System settings', integration: 'Customisation', users: 'General settings', 'api-keys': 'General settings', account: 'Preferences', personalisation: 'Customisation', connectors: 'Customisation' };
      const badge = document.getElementById('settingsSectionBadge');
      if (badge) badge.textContent = labels[section] || section;
      if (pushState) updateUrl({ view: 'settings', section });
      if (section === 'users') loadUsers();
      if (section === 'api-keys') loadApiKeys();
      if (section === 'account') loadAccount();
      if (section === 'data-policies') loadDataPoliciesSection();
      if (section === 'personalisation') loadBrandIdentities();
    }

    function saveSetting(key, value) {
      appSettings[key] = value;
    }

    async function saveSettingImmediate(key, value) {
      saveSetting(key, value);
      await saveCurrentSettings();
    }

    async function onObjectStorageToggle(checkbox) {
      if (checkbox.checked && (appSettings.objectStorageProvider ?? 'none') === 'none') {
        checkbox.checked = false;
        notify('error', 'No provider configured', 'Select an object storage provider via the settings gear before enabling.');
        return;
      }
      await saveSettingImmediate('objectStorage', checkbox.checked);
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
          pendingConfirmationPurgeRequireApproval:    appSettings.pendingConfirmationPurgeRequireApproval,
          loginFooterEnabled:                        appSettings.loginFooterEnabled,
          loginFooter:                               appSettings.loginFooter,
          promoBarEnabled:                           appSettings.promoBarEnabled,
          promoBar:                                  appSettings.promoBar,
          promoBarDismissable:                       appSettings.promoBarDismissable,
          promoBarShowOnLogin:                       appSettings.promoBarShowOnLogin
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

