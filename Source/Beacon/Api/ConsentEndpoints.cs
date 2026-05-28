using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Beacon.Core.Validation;
using Beacon.Tokens;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace Beacon.Api;

public static class ConsentEndpoints
{
    private const string PermissionTag = "Permission Management";
    private const string PermissionControlTag = "Permission Control";

    public static void MapConsentEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/u/{token}", ShowPreferencePage)
            .WithName("ShowPreferencePage")
            .WithTags(PermissionControlTag)
            .WithDescription("Display the email preference management page for a user. Token is generated via /api/tokens/generate.");

        routes.MapPost("/u/{token}", ProcessPreferenceUpdate)
            .WithName("ProcessPreferenceUpdate")
            .WithTags(PermissionControlTag)
            .WithDescription("Process user preference updates from the form submission.");

        routes.MapPost("/api/consent/check", CheckConsent)
            .WithName("CheckConsent")
            .WithTags(PermissionTag)
            .RequireAuthorization()
            .WithDescription("Check if an email is opted-in or opted-out for a specific permission.");

        routes.MapGet("/confirm/{token}", ConfirmSubscription)
            .WithName("ConfirmSubscription")
            .ExcludeFromDescription();
    }

    private static async Task<IResult> ShowPreferencePage(
        string token,
        HttpContext context,
        CancellationToken ct,
        [FromServices] IAntiforgery antiforgery,
        [FromServices] TokenValidator validator,
        [FromServices] IConsentService consentService,
        [FromServices] ITokenUsageRepository tokenUsageRepository,
        [FromServices] ISystemConfigurationService configService,
        [FromServices] IBrandIdentityService brandService)
    {
        var result = validator.Validate(token);

        var lang = result.Payload?.Language ?? "en";

        if (result.IsExpired)
        {
            var expiredBrand = result.Payload is not null ? await ResolveBrandAsync(brandService, result.Payload.Bucket, ct) : null;
            return Results.Content(GetStatusPage("expired", lang, brand: expiredBrand), "text/html");
        }

        if (!result.IsValid || result.Payload is null)
        {
            return Results.Content(GetStatusPage("invalid", "en"), "text/html");
        }

        var brand = await ResolveBrandAsync(brandService, result.Payload.Bucket, ct);

        // Only check token usage if replay is not allowed
        if (!result.Payload.AllowReplay)
        {
            var tokenHash = ComputeTokenHash(token);
            if (await tokenUsageRepository.IsTokenUsedAsync(tokenHash))
            {
                return Results.Content(GetStatusPage("already_processed", lang, brand: brand), "text/html");
            }
        }

        var permissionStates = new List<(string permission, bool optedIn)>();
        var allowDbLookup = configService.Get().AllowDbLookup;
        foreach (var permission in result.Payload.Permissions)
        {
            if (InputValidator.IsPermissionAllowed(permission))
            {
                bool optedIn;
                if (allowDbLookup)
                {
                    var status = await consentService.CheckAsync(result.Payload.Bucket, result.Payload.Email, permission);
                    optedIn = status == ConsentStatus.OptedIn;
                }
                else
                {
                    optedIn = true;
                }
                permissionStates.Add((permission, optedIn));
            }
        }

        var tokens = antiforgery.GetAndStoreTokens(context);

        var utmInputs = BuildUtmHiddenInputs(context.Request.Query);

        return Results.Content(GetPreferencePage(
            token,
            result.Payload.Email,
            permissionStates,
            tokens.RequestToken!,
            tokens.FormFieldName,
            result.Payload.Language,
            utmInputs,
            brand), "text/html");
    }

    private static async Task<IResult> ProcessPreferenceUpdate(
        string token,
        HttpContext context,
        CancellationToken ct,
        [FromServices] IAntiforgery antiforgery,
        [FromServices] TokenValidator validator,
        [FromServices] IConsentService consentService,
        [FromServices] IConsentRepository consentRepository,
        [FromServices] IWebhookService webhookService,
        [FromServices] IBucketRepository bucketRepository,
        [FromServices] EmailHasher emailHasher,
        [FromServices] ITokenUsageRepository tokenUsageRepository,
        [FromServices] IAdminNotificationService notifications,
        [FromServices] ISystemConfigurationService sysConfig,
        [FromServices] IBrandIdentityService brandService)
    {
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            return Results.BadRequest("Invalid antiforgery token.");
        }

        var result = validator.Validate(token);

        var lang = result.Payload?.Language ?? "en";

        if (result.IsExpired)
        {
            return Results.Content(GetStatusPage("expired", lang), "text/html");
        }

        if (!result.IsValid || result.Payload is null)
        {
            return Results.Content(GetStatusPage("invalid", "en"), "text/html");
        }

        var brand = await ResolveBrandAsync(brandService, result.Payload.Bucket, ct);

        // Only check token usage if replay is not allowed
        if (!result.Payload.AllowReplay)
        {
            var tokenHash = ComputeTokenHash(token);
            if (await tokenUsageRepository.IsTokenUsedAsync(tokenHash))
            {
                return Results.Content(GetStatusPage("already_processed", lang, brand: brand), "text/html");
            }
        }

        if (await bucketRepository.IsArchivedAsync(result.Payload.Bucket.Trim().ToLowerInvariant()))
        {
            return Results.Content(GetStatusPage("invalid", lang, brand: brand), "text/html");
        }

        var form = await context.Request.ReadFormAsync();
        var action = form["action"].ToString();
        var utmCustomFields = sysConfig.Get().EnableUtmTracking ? BuildUtmCustomFields(context) : null;

        try
        {
            using var transaction = await consentService.BeginTransactionAsync();

            if (action == "unsubscribe_all")
            {
                var validPermissions = result.Payload.Permissions
                    .Where(InputValidator.IsPermissionAllowed)
                    .ToArray();

                await consentService.ProcessOptOutAsync(
                    result.Payload.Bucket,
                    result.Payload.Email,
                    validPermissions,
                    token,
                    ConsentSource.Url,
                    utmCustomFields);

                await consentService.CommitTransactionAsync();

                // Fire one webhook with full permission snapshot
                await TriggerWebhookSafe(webhookService, consentRepository, emailHasher,
                    result.Payload.Bucket, result.Payload.Email, null);
                await notifications.PublishConsentUpdateAsync(new ConsentUpdateNotification(result.Payload.Bucket));

                // Only mark token as used if replay is not allowed
                if (!result.Payload.AllowReplay)
                {
                    var tokenHash = ComputeTokenHash(token);
                    await tokenUsageRepository.MarkTokenUsedAsync(tokenHash, result.Payload.ExpiresAtUtc);
                }
                return Results.Content(GetStatusPage("unsubscribed", lang, optedOut: validPermissions, brand: brand), "text/html");
            }

            // Process individual toggle states
            var optedOut = new List<string>();
            var keptIn = new List<string>();

            foreach (var permission in result.Payload.Permissions)
            {
                if (!InputValidator.IsPermissionAllowed(permission)) continue;

                var isChecked = form.ContainsKey($"pref_{permission}");
                if (!isChecked)
                {
                    await consentService.ProcessOptOutAsync(
                        result.Payload.Bucket,
                        result.Payload.Email,
                        [permission],
                        token,
                        ConsentSource.Url,
                        utmCustomFields);
                    optedOut.Add(permission);
                }
                else
                {
                    // Don't let the preference page self-confirm a PendingConfirmation record;
                    // the user must click the confirmation link sent via email.
                    var currentStatus = await consentService.CheckAsync(
                        result.Payload.Bucket,
                        result.Payload.Email,
                        permission);

                    if (currentStatus != ConsentStatus.PendingConfirmation)
                    {
                        await consentService.OverrideAsync(
                            result.Payload.Bucket,
                            result.Payload.Email,
                            permission,
                            ConsentStatus.OptedIn,
                            source: ConsentSource.Url);
                    }
                    keptIn.Add(permission);
                }
            }

            await consentService.CommitTransactionAsync();

            // Fire one webhook with full permission snapshot
            await TriggerWebhookSafe(webhookService, consentRepository, emailHasher,
                result.Payload.Bucket, result.Payload.Email, null);
            await notifications.PublishConsentUpdateAsync(new ConsentUpdateNotification(result.Payload.Bucket));

            // Only mark token as used if replay is not allowed
            if (!result.Payload.AllowReplay)
            {
                var tokenHash = ComputeTokenHash(token);
                await tokenUsageRepository.MarkTokenUsedAsync(tokenHash, result.Payload.ExpiresAtUtc);
            }
            return Results.Content(GetStatusPage("updated", lang, [.. optedOut], [.. keptIn], brand), "text/html");
        }
        catch
        {
            return Results.StatusCode(500);
        }
    }

    private static async Task TriggerWebhookSafe(
        IWebhookService webhookService,
        IConsentRepository repository,
        EmailHasher emailHasher,
        string bucket,
        string email,
        string? customFieldsJson)
    {
        try
        {
            var normalizedBucket = bucket.Trim().ToLowerInvariant();
            var normalizedEmail = email.Trim().ToLowerInvariant();
            var emailHash = emailHasher.Hash(normalizedEmail);

            // Fetch full permission snapshot for this email
            var records = await repository.GetByEmailAsync(normalizedBucket, emailHash);
            var permissions = records.Select(r => new PermissionState
            {
                Permission = r.Permission,
                Status = r.Status
            }).ToList();

            if (permissions.Count == 0) return;

            var data = new WebhookTriggerData
            {
                Bucket = normalizedBucket,
                Email = normalizedEmail,
                EmailHash = emailHash,
                Permissions = permissions,
                CustomFields = customFieldsJson
            };

            await webhookService.TriggerWebhookAsync(normalizedBucket, data);
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Webhook trigger failed for bucket {Bucket}; non-fatal", bucket);
        }
    }

    private static async Task<IResult> ConfirmSubscription(
        string token,
        CancellationToken ct,
        [FromServices] IEmailQueueRepository emailQueue,
        [FromServices] IConsentService consentService,
        [FromServices] Encryptor encryptor,
        [FromServices] IBrandIdentityService brandService)
    {
        var entry = await emailQueue.GetByConfirmationTokenAsync(token);
        if (entry is null)
            return Results.Content(GetStatusPage("invalid", "en"), "text/html");

        var lang = entry.Language ?? "en";
        var brand = await ResolveBrandAsync(brandService, entry.Bucket, ct);

        if (entry.Status == EmailQueueStatus.Confirmed)
            return Results.Content(GetStatusPage("already_processed", lang, brand: brand), "text/html");

        if (entry.ExpiresAt < DateTime.UtcNow || entry.Status == EmailQueueStatus.Expired)
            return Results.Content(GetStatusPage("expired", lang, brand: brand), "text/html");

        var email = encryptor.Decrypt(entry.EncryptedEmail);

        // Split permissions in case they are grouped (comma-separated)
        var permissions = entry.Permission.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var permission in permissions)
        {
            await consentService.OverrideAsync(entry.Bucket, email, permission, ConsentStatus.OptedIn,
                source: ConsentSource.Api);
        }

        await emailQueue.MarkConfirmedAsync(entry.Id, DateTime.UtcNow);

        return Results.Content(GetStatusPage("confirmed", lang, brand: brand), "text/html");
    }

    private static async Task<IResult> CheckConsent(
        [FromBody] CheckConsentRequest request,
        [FromServices] IConsentService consentService)
    {
        if (string.IsNullOrWhiteSpace(request.Bucket))
        {
            return Results.BadRequest(new { error = "Bucket is required" });
        }

        var emailValidation = InputValidator.ValidateEmail(request.Email);
        if (!emailValidation.IsValid)
        {
            return Results.BadRequest(new { error = emailValidation.Error });
        }

        var permissionValidation = InputValidator.ValidatePermission(request.Permission);
        if (!permissionValidation.IsValid)
        {
            return Results.BadRequest(new { error = permissionValidation.Error });
        }

        var status = await consentService.CheckAsync(request.Bucket, request.Email, request.Permission);

        return Results.Ok(new CheckConsentResponse
        {
            Status = status == ConsentStatus.OptedIn ? "opted_in" : "opted_out"
        });
    }

    private static string ComputeTokenHash(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string FormatPermission(string permission)
    {
        if (string.IsNullOrEmpty(permission)) return string.Empty;

        // Convert snake_case or kebab-case to Title Case
        var words = permission.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w =>
            char.ToUpper(w[0]) + (w.Length > 1 ? w[1..] : string.Empty)));
    }

    private static string GetBaseStyles() => """
        :root {
          --bg: #ffffff;
          --fg: #111111;
          --accent: var(--fg);
          --accent-fg: var(--bg);
          --success: #22c55e;
          --warning: #f59e0b;
          --error: #ef4444;
          --info: #3b82f6;
          --radius: 14px;
          --max-width: 520px;
        }

        @media (prefers-color-scheme: dark) {
          :root {
            --bg: #0f0f0f;
            --fg: #e7e7e7;
          }
        }

        * { box-sizing: border-box; }

        html, body {
          margin: 0;
          padding: 0;
          background: var(--bg);
          color: var(--fg);
          font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
          line-height: 1.5;
        }

        main {
          min-height: 100vh;
          display: flex;
          align-items: center;
          justify-content: center;
          padding: 24px;
        }

        .card {
          width: 100%;
          max-width: var(--max-width);
          border: 1px solid color-mix(in srgb, var(--fg) 15%, transparent);
          border-radius: var(--radius);
          padding: 28px;
        }

        h1 {
          font-size: 1.4rem;
          margin: 0 0 8px;
          font-weight: 600;
          letter-spacing: -0.01em;
          user-select: none;
        }

        p {
          margin: 0 0 24px;
          color: color-mix(in srgb, var(--fg) 75%, transparent);
        }

        .icon {
          font-size: 3rem;
          margin-bottom: 16px;
          user-select: none;
        }

        .icon.success { color: var(--success); }
        .icon.warning { color: var(--warning); }
        .icon.error { color: var(--error); }
        .icon.info { color: var(--info); }

        .status-brand { margin: 0; padding: 0; }
        .status-brand:not(:empty) { margin-bottom: 24px; padding-bottom: 20px; border-bottom: 1px solid color-mix(in srgb, var(--fg) 10%, transparent); }
        .status-body { text-align: center; }

        .brand-logo {
          display: block;
          max-width: 140px;
          max-height: 52px;
          width: auto;
          height: auto;
          margin: 0 0 20px;
          user-select: none;
        }
        """;

    private static Beacon.Localization.FormLocalization.ConsentPageStrings GetConsentPageStrings(string? lang) =>
        Beacon.Localization.FormLocalization.GetConsentPageStrings(lang);

    private static Beacon.Localization.FormLocalization.StatusStrings GetStatusStrings(string? lang) =>
        Beacon.Localization.FormLocalization.GetStatusStrings(lang);

    private static string GetPreferencePage(
        string token,
        string email,
        List<(string permission, bool optedIn)> permissions,
        string antiforgeryToken,
        string formFieldName,
        string language = "en",
        string utmHiddenInputs = "",
        BrandIdentitySettings? brand = null)
    {
        var lang = (language ?? "en").ToLowerInvariant();
        var t = GetConsentPageStrings(lang);

        var title = brand?.PageTitle is { Length: > 0 } pt ? pt : t.Title;
        var description = brand?.PageBody is { Length: > 0 } pb ? pb : t.Description;
        var footerText = brand?.Footer is { Length: > 0 } ft ? ft : t.PreferencesFor;
        var browserTitle = brand?.BrowserTitle is { Length: > 0 } bt ? bt : t.Title;

        var togglesHtml = new StringBuilder();
        foreach (var (permission, optedIn) in permissions)
        {
            var label = WebUtility.HtmlEncode(FormatPermission(permission));
            var name = WebUtility.HtmlEncode(permission);
            var checkedAttr = optedIn ? "checked" : "";
            togglesHtml.Append($$"""
                <div class="toggle">
                  <label for="pref_{{name}}">{{label}}</label>
                  <input type="checkbox" id="pref_{{name}}" name="pref_{{name}}" {{checkedAttr}} />
                </div>
                """);
        }

        var maskedEmail = MaskEmail(email);

        var theme = brand?.Theme ?? "system";
        var colorScheme = theme == "light" ? "light" : theme == "dark" ? "dark" : "light dark";
        var dataThemeAttr = theme is "light" or "dark" ? $" data-theme=\"{theme}\"" : "";

        // Brand CSS overrides injected after GetBaseStyles()
        var brandCss = BuildBrandCssOverrides(brand);
        var logoHtml = BuildPageLogoHtml(brand?.Logo);

        return Minify($$"""
            <!DOCTYPE html>
            <html lang="{{lang}}"{{dataThemeAttr}}>
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <title>{{WebUtility.HtmlEncode(browserTitle)}}</title>
              <meta name="color-scheme" content="{{colorScheme}}" />
              <style>
                {{GetBaseStyles()}}
                {{brandCss}}

                .preferences { margin-bottom: 24px; }

                .toggle {
                  display: flex;
                  align-items: center;
                  justify-content: space-between;
                  padding: 14px 0;
                  border-top: 1px solid color-mix(in srgb, var(--fg) 10%, transparent);
                }

                .toggle:first-child { border-top: none; }

                .toggle label {
                  font-size: 1rem;
                  cursor: pointer;
                }

                .toggle input {
                  appearance: none;
                  width: 44px;
                  height: 26px;
                  border-radius: 999px;
                  background: color-mix(in srgb, var(--fg) 20%, transparent);
                  position: relative;
                  cursor: pointer;
                  transition: background 0.2s, filter 0.15s;
                }

                .toggle input::after {
                  content: "";
                  position: absolute;
                  top: 3px;
                  left: 3px;
                  width: 20px;
                  height: 20px;
                  border-radius: 50%;
                  background: var(--bg);
                  transition: transform 0.2s ease;
                }

                .toggle input:checked { background: var(--accent); }
                .toggle input:checked::after { transform: translateX(18px); }
                .toggle input:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
                .toggle input:not(:checked):hover { background: color-mix(in srgb, var(--fg) 30%, transparent); }
                .toggle input:checked:hover { filter: brightness(0.88); }

                button {
                  appearance: none;
                  border: none;
                  width: 100%;
                  padding: 14px 16px;
                  border-radius: 10px;
                  background: var(--accent);
                  color: var(--accent-fg);
                  font-size: 1rem;
                  font-weight: 500;
                  cursor: pointer;
                  transition: filter 0.15s;
                }

                button:not(.secondary):hover { filter: brightness(0.88); }
                button:not(.secondary):active { filter: brightness(0.78); }

                button.secondary {
                  margin-top: 12px;
                  background: transparent;
                  color: var(--fg);
                  border: 1px solid color-mix(in srgb, var(--fg) 25%, transparent);
                  transition: background 0.15s;
                }

                button.secondary:hover {
                  background: color-mix(in srgb, var(--fg) 8%, transparent);
                }

                button.secondary:active {
                  background: color-mix(in srgb, var(--fg) 14%, transparent);
                }

                footer {
                  margin-top: 24px;
                  font-size: 0.85rem;
                  text-align: center;
                  color: color-mix(in srgb, var(--fg) 60%, transparent);
                }

                .email {
                  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
                  color: color-mix(in srgb, var(--fg) 70%, transparent);
                  word-break: break-all;
                }

                .select-none { user-select: none;}
              </style>
            </head>
            <body>
              <main>
                <section class="card">
                  {{logoHtml}}
                  <h1 class="select-none">{{WebUtility.HtmlEncode(title)}}</h1>
                  <p>{{WebUtility.HtmlEncode(description)}}</p>

                  <form method="post" action="/u/{{WebUtility.HtmlEncode(token)}}">
                    <input name="{{formFieldName}}" type="hidden" value="{{antiforgeryToken}}" />
                    {{utmHiddenInputs}}
                    <div class="preferences">
                      {{togglesHtml}}
                    </div>

                    <button type="submit" name="action" value="update">{{WebUtility.HtmlEncode(t.SaveButton)}}</button>
                    <button type="submit" name="action" value="unsubscribe_all" class="secondary">{{WebUtility.HtmlEncode(t.UnsubscribeButton)}}</button>
                  </form>

                  <footer class="select-none">
                    {{WebUtility.HtmlEncode(footerText)}}<br />
                    <span class="email">{{WebUtility.HtmlEncode(maskedEmail)}}</span>
                  </footer>
                </section>
              </main>
            </body>
            </html>
            """);
    }

    private static string BuildBrandCssOverrides(BrandIdentitySettings? brand)
    {
        if (brand is null) return string.Empty;
        var sb = new StringBuilder();

        var hasAccent = brand.PrimaryAccent is { Length: > 0 };
        var hasSurface = brand.SurfaceColour is { Length: > 0 };
        var hasFont = brand.Font is { Length: > 0 };
        var theme = brand.Theme;

        if (hasAccent || hasSurface)
        {
            sb.AppendLine(":root {");
            if (hasAccent)
            {
                sb.AppendLine($"  --accent: {brand.PrimaryAccent};");
                sb.AppendLine($"  --accent-fg: {ContrastForeground(brand.PrimaryAccent!)};");
            }
            if (hasSurface)
            {
                sb.AppendLine($"  --bg: {brand.SurfaceColour};");
                sb.AppendLine($"  --fg: {ContrastForeground(brand.SurfaceColour!)};");
            }
            sb.AppendLine("}");
        }

        // font-family must target html,body directly — :root alone is overridden by the base body rule
        if (hasFont)
            sb.AppendLine($"html, body {{ font-family: \"{brand.Font}\", system-ui, -apple-system, sans-serif; }}");

        // Force theme: override media query with explicit values
        if (theme == "dark")
        {
            sb.AppendLine("@media (prefers-color-scheme: light) {");
            sb.AppendLine("  :root {");
            sb.AppendLine($"    --bg: {brand.SurfaceColour ?? "#0f0f0f"};");
            sb.AppendLine($"    --fg: {(hasSurface ? ContrastForeground(brand.SurfaceColour!) : "#e7e7e7")};");
            if (hasAccent)
            {
                sb.AppendLine($"    --accent: {brand.PrimaryAccent};");
                sb.AppendLine($"    --accent-fg: {ContrastForeground(brand.PrimaryAccent!)};");
            }
            sb.AppendLine("  }");
            sb.AppendLine("}");
        }
        else if (theme == "light")
        {
            sb.AppendLine("@media (prefers-color-scheme: dark) {");
            sb.AppendLine("  :root {");
            sb.AppendLine($"    --bg: {brand.SurfaceColour ?? "#ffffff"} !important;");
            sb.AppendLine($"    --fg: {(hasSurface ? ContrastForeground(brand.SurfaceColour!) : "#111111")} !important;");
            if (hasAccent)
            {
                sb.AppendLine($"    --accent: {brand.PrimaryAccent} !important;");
                sb.AppendLine($"    --accent-fg: {ContrastForeground(brand.PrimaryAccent!)} !important;");
            }
            sb.AppendLine("  }");
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    internal static string Minify(string content) =>
        string.Join("", content.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));

    private static string ContrastForeground(string hexBg)
    {
        // Simple luminance check: if bg is dark, use light fg; otherwise dark fg
        try
        {
            var hex = hexBg.TrimStart('#');
            if (hex.Length < 6) return "#111111";
            var r = Convert.ToInt32(hex[..2], 16);
            var g = Convert.ToInt32(hex[2..4], 16);
            var b = Convert.ToInt32(hex[4..6], 16);
            var luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
            return luminance > 0.5 ? "#111111" : "#ffffff";
        }
        catch { return "#111111"; }
    }

    private static string BuildPageLogoHtml(AssetObject? logo)
    {
        if (logo is null) return "";
        var src = logo.Type switch
        {
            "base64" => logo.Data,
            "url" or "objectStorage" => logo.Url,
            _ => null
        };
        if (string.IsNullOrEmpty(src)) return "";
        return $"<img src=\"{WebUtility.HtmlEncode(src)}\" class=\"brand-logo\" alt=\"Logo\" />";
    }

    private static async Task<BrandIdentitySettings?> ResolveBrandAsync(IBrandIdentityService brandService, string bucket, CancellationToken ct = default)
    {
        var identity = await brandService.GetForBucketAsync(bucket, ct);
        if (string.IsNullOrEmpty(identity.Settings) || identity.Settings == "{}") return null;
        try { return JsonSerializer.Deserialize<BrandIdentitySettings>(identity.Settings); }
        catch { return null; }
    }

    private static string GetStatusPage(string status, string language = "en", string[]? optedOut = null, string[]? keptIn = null, BrandIdentitySettings? brand = null)
    {
        var lang = (language ?? "en").ToLowerInvariant();
        var t = GetStatusStrings(lang);

        var (icon, iconClass, title, message) = status switch
        {
            "expired" => ("⚠", "warning", t.ExpiredTitle, t.ExpiredMsg),
            "invalid" => ("✗", "error", t.InvalidTitle, t.InvalidMsg),
            "already_processed" => ("ℹ", "info", t.ProcessedTitle, t.ProcessedMsg),
            "unsubscribed" => ("✓", "success", t.UnsubTitle, $"{t.UnsubMsgPrefix} {FormatList(optedOut)}"),
            "updated" => ("✓", "success", t.UpdatedTitle, BuildUpdateMessage(t, optedOut, keptIn)),
            "confirmed" => ("✓", "success",
                brand?.ConfirmTitle is { Length: > 0 } ct ? ct : t.ConfirmedTitle,
                brand?.ConfirmMsg   is { Length: > 0 } cm ? cm : t.ConfirmedMsg),
            _ => ("✓", "success", t.SuccessTitle, t.SuccessMsg)
        };

        var brandCss = BuildBrandCssOverrides(brand);
        var logoHtml = BuildPageLogoHtml(brand?.Logo);
        var dataThemeAttr = brand?.Theme switch { "dark" => " data-theme=\"dark\"", "light" => " data-theme=\"light\"", _ => "" };
        var colorSchemeMeta = brand?.Theme switch { "dark" => "dark", "light" => "light", _ => "light dark" };

        return Minify($$"""
            <!DOCTYPE html>
            <html lang="{{lang}}"{{dataThemeAttr}}>
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <title>{{WebUtility.HtmlEncode(title)}}</title>
              <meta name="color-scheme" content="{{colorSchemeMeta}}" />
              <style>{{GetBaseStyles()}}{{brandCss}}</style>
            </head>
            <body>
              <main>
                <section class="card">
                  <div class="status-brand">{{logoHtml}}</div>
                  <div class="status-body">
                    <div class="icon {{iconClass}}">{{icon}}</div>
                    <h1>{{WebUtility.HtmlEncode(title)}}</h1>
                    <p>{{message}}</p>
                  </div>
                </section>
              </main>
            </body>
            </html>
            """);
    }

    private static string MaskEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 1) return email;

        var local = email[..atIndex];
        var domain = email[atIndex..];

        if (local.Length <= 2)
            return local[0] + "***" + domain;

        return local[0] + "***" + local[^1] + domain;
    }

    private static string FormatList(string[]? items)
    {
        if (items is null || items.Length == 0) return "";
        return string.Join(", ", items.Select(p => FormatPermission(p)));
    }

    private static string BuildUpdateMessage(Beacon.Localization.FormLocalization.StatusStrings t, string[]? optedOut, string[]? keptIn)
    {
        var parts = new List<string>();
        if (optedOut?.Length > 0)
            parts.Add($"{t.UpdatedOptOutPrefix} {FormatList(optedOut)}");
        if (keptIn?.Length > 0)
            parts.Add($"{t.UpdatedOptInPrefix} {FormatList(keptIn)}");
        return string.Join("<br><br>", parts);
    }

    private static readonly string[] UtmKeys = ["utm_source", "utm_medium", "utm_campaign", "utm_content", "utm_term"];

    private static string? BuildUtmCustomFields(HttpContext context)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Read from query string (GET) and form body (POST hidden inputs)
        foreach (var key in UtmKeys)
        {
            var value = context.Request.Query[key].FirstOrDefault()
                ?? context.Request.Form[key].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value))
                fields[key] = value;
        }

        return fields.Count > 0
            ? System.Text.Json.JsonSerializer.Serialize(fields)
            : null;
    }

    private static string BuildUtmHiddenInputs(IQueryCollection query)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var key in UtmKeys)
        {
            var value = query[key].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value))
                sb.Append($"<input type=\"hidden\" name=\"{key}\" value=\"{WebUtility.HtmlEncode(value)}\" />");
        }
        return sb.ToString();
    }
}

public sealed class CheckConsentRequest
{
    public string Bucket { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Permission { get; set; } = string.Empty;
}

public sealed class CheckConsentResponse
{
    public string Status { get; set; } = string.Empty;
}