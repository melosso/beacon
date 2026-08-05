using System.Net;
using System.Text.Json;
using Beacon.Core.Models;
using Beacon.Core.Services;
using Beacon.Core.Validation;
using Beacon.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Beacon.Storage;

namespace Beacon.Api;

public static class SubmissionEndpoints
{
    public static void MapSubmissionEndpoints(this IEndpointRouteBuilder routes)
    {
        const string SubmissionTag = "Subscriptions";

        // Admin endpoints (require authorization)
        routes.MapGet("/api/admin/submissions", GetAllForms)
            .RequireAuthorization("SubmissionsRead")
            .ExcludeFromDescription();

        routes.MapGet("/api/admin/submissions/{id:guid}", GetForm)
            .RequireAuthorization("SubmissionsRead")
            .ExcludeFromDescription();

        routes.MapPost("/api/admin/submissions", CreateForm)
            .RequireAuthorization("SubmissionsWrite")
            .ExcludeFromDescription();

        routes.MapPut("/api/admin/submissions/{id:guid}", UpdateForm)
            .RequireAuthorization("SubmissionsWrite")
            .ExcludeFromDescription();

        routes.MapDelete("/api/admin/submissions/{id:guid}", DeleteForm)
            .RequireAuthorization("SubmissionsWrite")
            .ExcludeFromDescription();

        // Public endpoints (public, but we'll handle the origin-validation in the handler)
        routes.MapGet("/api/submission/{id:guid}/embed", GetEmbedPage)
            .AllowAnonymous()
            .RequireCors("PublicSubmission")
            .ExcludeFromDescription();

        routes.MapGet("/api/submission/{id:guid}/embed.js", GetEmbedScript)
            .AllowAnonymous()
            .RequireCors("PublicSubmission")
            .ExcludeFromDescription();

        routes.MapPost("/api/submission/{id:guid}/subscribe", Subscribe)
            .AllowAnonymous()
            .RequireCors("PublicSubmission")
            .DisableAntiforgery()
            .WithTags(SubmissionTag)
            .WithName("Subscribe")
            .WithDescription("Endpoint for handling subscription form submissions. Accepts email and optional website and consent fields.");
    }

    // Admin endpoints
    private static async Task<IResult> GetAllForms(
        [FromServices] SubmissionFormService service)
    {
        var forms = await service.GetAllFormsAsync();
        return Results.Ok(forms.Select(f => new
        {
            f.Id,
            f.Name,
            f.Bucket,
            f.Permission,
            allowedOrigins = DeserializeOrigins(f.AllowedOrigins),
            formConfig = DeserializeFormConfig(f.FormConfig),
            f.RateLimitPerMinute,
            f.HoneypotEnabled,
            f.DoubleOptIn,
            f.IsEnabled,
            f.Language,
            f.RedirectSuccess,
            f.RedirectError,
            f.RedirectFormPost,
            f.RedirectJsEmbed,
            f.DisableRedirects,
            f.ConsentRequired,
            f.ConsentText,
            f.PrivacyPolicyUrl,
            f.CollectName,
            f.NameLabel,
            customFields = DeserializeCustomFields(f.CustomFields),
            f.CreatedAt,
            f.UpdatedAt,
            f.SubmissionCount
        }));
    }

    private static async Task<IResult> GetForm(
        Guid id,
        [FromServices] SubmissionFormService service)
    {
        var form = await service.GetFormAsync(id);
        if (form == null)
            return Results.NotFound(new { error = "Submission form not found" });

        return Results.Ok(new
        {
            form.Id,
            form.Name,
            form.Bucket,
            form.Permission,
            allowedOrigins = DeserializeOrigins(form.AllowedOrigins),
            formConfig = DeserializeFormConfig(form.FormConfig),
            form.RateLimitPerMinute,
            form.HoneypotEnabled,
            form.DoubleOptIn,
            form.IsEnabled,
            form.Language,
            form.RedirectSuccess,
            form.RedirectError,
            form.RedirectFormPost,
            form.RedirectJsEmbed,
            form.DisableRedirects,
            form.ConsentRequired,
            form.ConsentText,
            form.PrivacyPolicyUrl,
            form.CollectName,
            form.NameLabel,
            customFields = DeserializeCustomFields(form.CustomFields),
            form.CreatedAt,
            form.UpdatedAt,
            form.SubmissionCount
        });
    }

    private static async Task<IResult> CreateForm(
        [FromBody] CreateSubmissionFormRequest request,
        [FromServices] SubmissionFormService service,
        [FromServices] ISystemConfigurationService configService)
    {
        var defaults = configService.Get();

        // Validate name
        var nameValidation = InputValidator.ValidateSubmissionName(request.Name);
        if (!nameValidation.IsValid)
            return Results.BadRequest(new { error = nameValidation.Error });

        // Validate bucket
        var bucketValidation = InputValidator.ValidateBucket(request.Bucket);
        if (!bucketValidation.IsValid)
            return Results.BadRequest(new { error = bucketValidation.Error });

        // Validate permissions (supports comma-separated)
        var permissions = request.Permission.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (permissions.Length == 0)
            return Results.BadRequest(new { error = "Permission is required" });
        foreach (var perm in permissions)
        {
            var permissionValidation = InputValidator.ValidatePermission(perm);
            if (!permissionValidation.IsValid)
                return Results.BadRequest(new { error = permissionValidation.Error });
        }

        // Validate origins
        if (request.AllowedOrigins == null || request.AllowedOrigins.Count == 0)
            return Results.BadRequest(new { error = "At least one allowed origin is required" });

        foreach (var origin in request.AllowedOrigins)
        {
            var originValidation = InputValidator.ValidateOrigin(origin);
            if (!originValidation.IsValid)
                return Results.BadRequest(new { error = $"Invalid origin '{origin}': {originValidation.Error}" });
        }

        // Validate GDPR fields
        var consentTextValidation = InputValidator.ValidateConsentText(request.ConsentText);
        if (!consentTextValidation.IsValid)
            return Results.BadRequest(new { error = consentTextValidation.Error });

        var privacyUrlValidation = InputValidator.ValidatePrivacyPolicyUrl(request.PrivacyPolicyUrl);
        if (!privacyUrlValidation.IsValid)
            return Results.BadRequest(new { error = privacyUrlValidation.Error });

        // Validate redirect configuration
        if (!request.DisableRedirects && (request.RedirectFormPost || request.RedirectJsEmbed) &&
            string.IsNullOrWhiteSpace(request.RedirectSuccess) && string.IsNullOrWhiteSpace(request.RedirectError))
            return Results.BadRequest(new { error = "Redirect is enabled but no redirect URLs are configured. Add a success or error URL, or disable redirects." });

        // Validate FormConfig CSS fields
        if (request.FormConfig != null)
        {
            var primaryColorValidation = InputValidator.ValidateCssColor(request.FormConfig.PrimaryColor);
            if (!primaryColorValidation.IsValid)
                return Results.BadRequest(new { error = $"Invalid primary color: {primaryColorValidation.Error}" });

            var bgColorValidation = InputValidator.ValidateCssColor(request.FormConfig.BackgroundColor);
            if (!bgColorValidation.IsValid)
                return Results.BadRequest(new { error = $"Invalid background color: {bgColorValidation.Error}" });

            var textColorValidation = InputValidator.ValidateCssColor(request.FormConfig.TextColor);
            if (!textColorValidation.IsValid)
                return Results.BadRequest(new { error = $"Invalid text color: {textColorValidation.Error}" });

            var borderRadiusValidation = InputValidator.ValidateCssBorderRadius(request.FormConfig.BorderRadius);
            if (!borderRadiusValidation.IsValid)
                return Results.BadRequest(new { error = $"Invalid border radius: {borderRadiusValidation.Error}" });
        }

        if (!string.IsNullOrWhiteSpace(request.NameLabel) && request.NameLabel.Length > 100)
            return Results.BadRequest(new { error = "Name label must be 100 characters or fewer" });

        var form = new SubmissionForm
        {
            Name = request.Name.Trim(),
            Bucket = request.Bucket.Trim().ToLowerInvariant(),
            Permission = string.Join(", ", permissions),
            AllowedOrigins = JsonSerializer.Serialize(request.AllowedOrigins.Select(o => o.TrimEnd('/'))),
            FormConfig = request.FormConfig != null ? JsonSerializer.Serialize(request.FormConfig) : null,
            EncryptedApiToken = "", // Will be set by service
            RateLimitPerMinute = request.RateLimitPerMinute is > 0
                ? request.RateLimitPerMinute.Value
                : defaults.SubmissionDefaultRateLimitPerMinute,
            HoneypotEnabled = request.HoneypotEnabled ?? defaults.SubmissionDefaultHoneypotEnabled,
            DoubleOptIn = request.DoubleOptIn,
            Language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language.Trim(),
            RedirectSuccess = string.IsNullOrWhiteSpace(request.RedirectSuccess) ? null : request.RedirectSuccess.Trim(),
            RedirectError = string.IsNullOrWhiteSpace(request.RedirectError) ? null : request.RedirectError.Trim(),
            RedirectFormPost = request.RedirectFormPost,
            RedirectJsEmbed = request.RedirectJsEmbed,
            DisableRedirects = request.DisableRedirects,
            IsEnabled = request.IsEnabled,
            ConsentRequired = request.ConsentRequired ?? defaults.SubmissionDefaultConsentRequired,
            ConsentText = string.IsNullOrWhiteSpace(request.ConsentText) ? null : request.ConsentText.Trim(),
            PrivacyPolicyUrl = string.IsNullOrWhiteSpace(request.PrivacyPolicyUrl) ? null : request.PrivacyPolicyUrl.Trim(),
            CollectName = request.CollectName,
            NameLabel = string.IsNullOrWhiteSpace(request.NameLabel) ? null : request.NameLabel.Trim(),
            CustomFields = request.CustomFields is { Count: > 0 } ? JsonSerializer.Serialize(request.CustomFields) : null
        };

        var (created, plaintextToken) = await service.CreateFormAsync(form);

        return Results.Ok(new
        {
            created.Id,
            created.Name,
            created.Bucket,
            created.Permission,
            allowedOrigins = request.AllowedOrigins,
            apiToken = plaintextToken,
            created.IsEnabled,
            created.CreatedAt
        });
    }

    private static async Task<IResult> UpdateForm(
        Guid id,
        [FromBody] UpdateSubmissionFormRequest request,
        [FromServices] SubmissionFormService service,
        [FromServices] ISystemConfigurationService configService)
    {
        var existing = await service.GetFormAsync(id);
        if (existing == null)
            return Results.NotFound(new { error = "Submission form not found" });

        // Validate fields if provided
        if (request.Name != null)
        {
            var nameValidation = InputValidator.ValidateSubmissionName(request.Name);
            if (!nameValidation.IsValid)
                return Results.BadRequest(new { error = nameValidation.Error });
            existing.Name = request.Name.Trim();
        }

        if (request.Bucket != null)
        {
            var bucketValidation = InputValidator.ValidateBucket(request.Bucket);
            if (!bucketValidation.IsValid)
                return Results.BadRequest(new { error = bucketValidation.Error });
            existing.Bucket = request.Bucket.Trim().ToLowerInvariant();
        }

        if (request.Permission != null)
        {
            var perms = request.Permission.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (perms.Length == 0)
                return Results.BadRequest(new { error = "Permission is required" });
            foreach (var perm in perms)
            {
                var permissionValidation = InputValidator.ValidatePermission(perm);
                if (!permissionValidation.IsValid)
                    return Results.BadRequest(new { error = permissionValidation.Error });
            }
            existing.Permission = string.Join(", ", perms);
        }

        if (request.AllowedOrigins != null)
        {
            foreach (var origin in request.AllowedOrigins)
            {
                var originValidation = InputValidator.ValidateOrigin(origin);
                if (!originValidation.IsValid)
                    return Results.BadRequest(new { error = $"Invalid origin '{origin}': {originValidation.Error}" });
            }
            existing.AllowedOrigins = JsonSerializer.Serialize(request.AllowedOrigins.Select(o => o.TrimEnd('/')));
        }

        if (request.FormConfig != null)
        {
            var primaryColorValidation = InputValidator.ValidateCssColor(request.FormConfig.PrimaryColor);
            if (!primaryColorValidation.IsValid)
                return Results.BadRequest(new { error = $"Invalid primary color: {primaryColorValidation.Error}" });

            var bgColorValidation = InputValidator.ValidateCssColor(request.FormConfig.BackgroundColor);
            if (!bgColorValidation.IsValid)
                return Results.BadRequest(new { error = $"Invalid background color: {bgColorValidation.Error}" });

            var textColorValidation = InputValidator.ValidateCssColor(request.FormConfig.TextColor);
            if (!textColorValidation.IsValid)
                return Results.BadRequest(new { error = $"Invalid text color: {textColorValidation.Error}" });

            var borderRadiusValidation = InputValidator.ValidateCssBorderRadius(request.FormConfig.BorderRadius);
            if (!borderRadiusValidation.IsValid)
                return Results.BadRequest(new { error = $"Invalid border radius: {borderRadiusValidation.Error}" });

            existing.FormConfig = JsonSerializer.Serialize(request.FormConfig);
        }

        if (request.RateLimitPerMinute.HasValue)
            existing.RateLimitPerMinute = request.RateLimitPerMinute.Value > 0
                ? request.RateLimitPerMinute.Value
                : configService.Get().SubmissionDefaultRateLimitPerMinute;

        if (request.HoneypotEnabled.HasValue)
            existing.HoneypotEnabled = request.HoneypotEnabled.Value;

        if (request.DoubleOptIn.HasValue)
            existing.DoubleOptIn = request.DoubleOptIn.Value;

        if (request.Language != null)
            existing.Language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language.Trim();

        if (request.IsEnabled.HasValue)
            existing.IsEnabled = request.IsEnabled.Value;

        if (request.RedirectSuccess != null)
            existing.RedirectSuccess = string.IsNullOrWhiteSpace(request.RedirectSuccess) ? null : request.RedirectSuccess.Trim();

        if (request.RedirectError != null)
            existing.RedirectError = string.IsNullOrWhiteSpace(request.RedirectError) ? null : request.RedirectError.Trim();

        if (request.RedirectFormPost.HasValue)
            existing.RedirectFormPost = request.RedirectFormPost.Value;

        if (request.RedirectJsEmbed.HasValue)
            existing.RedirectJsEmbed = request.RedirectJsEmbed.Value;

        if (request.DisableRedirects.HasValue)
            existing.DisableRedirects = request.DisableRedirects.Value;

        if (request.ConsentRequired.HasValue)
            existing.ConsentRequired = request.ConsentRequired.Value;

        if (request.ConsentText != null)
        {
            var consentTextValidation = InputValidator.ValidateConsentText(request.ConsentText);
            if (!consentTextValidation.IsValid)
                return Results.BadRequest(new { error = consentTextValidation.Error });
            existing.ConsentText = string.IsNullOrWhiteSpace(request.ConsentText) ? null : request.ConsentText.Trim();
        }

        if (request.PrivacyPolicyUrl != null)
        {
            var privacyUrlValidation = InputValidator.ValidatePrivacyPolicyUrl(request.PrivacyPolicyUrl);
            if (!privacyUrlValidation.IsValid)
                return Results.BadRequest(new { error = privacyUrlValidation.Error });
            existing.PrivacyPolicyUrl = string.IsNullOrWhiteSpace(request.PrivacyPolicyUrl) ? null : request.PrivacyPolicyUrl.Trim();
        }

        if (request.CollectName.HasValue)
            existing.CollectName = request.CollectName.Value;

        if (request.NameLabel != null && request.NameLabel.Length > 100)
            return Results.BadRequest(new { error = "Name label must be 100 characters or fewer" });

        if (request.NameLabel != null)
            existing.NameLabel = string.IsNullOrWhiteSpace(request.NameLabel) ? null : request.NameLabel.Trim();

        if (request.CustomFields != null)
            existing.CustomFields = request.CustomFields.Count > 0 ? JsonSerializer.Serialize(request.CustomFields) : null;

        // Validate redirect configuration after applying updates
        if (!existing.DisableRedirects && (existing.RedirectFormPost || existing.RedirectJsEmbed) &&
            string.IsNullOrWhiteSpace(existing.RedirectSuccess) && string.IsNullOrWhiteSpace(existing.RedirectError))
            return Results.BadRequest(new { error = "Redirect is enabled but no redirect URLs are configured. Add a success or error URL, or disable redirects." });

        await service.UpdateFormAsync(existing);

        return Results.Ok(new
        {
            existing.Id,
            existing.Name,
            existing.Bucket,
            existing.Permission,
            allowedOrigins = DeserializeOrigins(existing.AllowedOrigins),
            formConfig = DeserializeFormConfig(existing.FormConfig),
            existing.RateLimitPerMinute,
            existing.HoneypotEnabled,
            existing.DoubleOptIn,
            existing.IsEnabled,
            existing.Language,
            existing.RedirectSuccess,
            existing.RedirectError,
            existing.RedirectFormPost,
            existing.RedirectJsEmbed,
            existing.DisableRedirects,
            existing.ConsentRequired,
            existing.ConsentText,
            existing.PrivacyPolicyUrl,
            existing.CollectName,
            existing.NameLabel,
            customFields = DeserializeCustomFields(existing.CustomFields),
            existing.CreatedAt,
            existing.UpdatedAt,
            existing.SubmissionCount
        });
    }

    private static async Task<IResult> DeleteForm(
        Guid id,
        [FromServices] SubmissionFormService service)
    {
        var form = await service.GetFormAsync(id);
        if (form == null)
            return Results.NotFound(new { error = "Submission form not found" });

        await service.DeleteFormAsync(id);
        return Results.Ok(new { success = true });
    }

    // Public endpoints

    private static async Task<IResult> GetEmbedPage(
        Guid id,
        HttpContext context,
        [FromServices] SubmissionFormService service,
        [FromServices] ISystemConfigurationService configService)
    {
        if (!configService.Get().EnableSubmissionForms)
            return Results.NotFound();

        var form = await service.GetFormAsync(id);
        if (form == null)
            return Results.Content(Minify(FormUnavailableHtml("en", null)), "text/html");
        if (!form.IsEnabled)
            return Results.Content(Minify(FormUnavailableHtml(form.Language, DeserializeFormConfig(form.FormConfig))), "text/html");

        var origins = DeserializeOrigins(form.AllowedOrigins);
        // An empty frame-ancestors list is invalid CSS-wise and browsers ignore it; fail closed instead.
        var cspOrigins = origins.Count > 0 ? string.Join(" ", origins) : "'none'";

        // frame-ancestors is the control here. X-Frame-Options cannot express an allowlist, and a
        // DENY alongside it would block the embedding this page exists for.
        context.Response.Headers["Content-Security-Policy"] = $"frame-ancestors {cspOrigins}";
        context.Response.Headers.Remove("X-Frame-Options");

        var config = DeserializeFormConfig(form.FormConfig) ?? new FormConfigDto();
        var formIdStr = form.Id.ToString();

        var embedPrimary = InputValidator.SanitizeCssColor(config.PrimaryColor, "#2563eb");
        var embedRadius  = InputValidator.SanitizeCssBorderRadius(config.BorderRadius, "8px");
        var embedBg      = !string.IsNullOrWhiteSpace(config.BackgroundColor) ? InputValidator.SanitizeCssColor(config.BackgroundColor, "Canvas") : "Canvas";
        var embedFg      = !string.IsNullOrWhiteSpace(config.TextColor) ? InputValidator.SanitizeCssColor(config.TextColor, "CanvasText") : "CanvasText";
        var embedT       = GetSubmissionStrings(form.Language);

        var honeypotHtml = form.HoneypotEnabled
            ? """<div style="position:absolute;left:-9999px;top:-9999px;" aria-hidden="true"><input type="text" name="website" tabindex="-1" autocomplete="off" /></div>"""
            : "";

        var consentHtml = "";
        if (form.ConsentRequired)
        {
            var consentLabel = WebUtility.HtmlEncode(form.ConsentText ?? embedT.ConsentText);
            consentHtml = $"""<div class="consent-row"><input type="checkbox" id="consentCb" name="consent" value="true" required /><label for="consentCb">{consentLabel}</label></div>""";
        }

        var privacyHtml = "";
        if (InputValidator.IsHttpUrl(form.PrivacyPolicyUrl))
        {
            privacyHtml = $"""<div class="privacy-link"><a href="{WebUtility.HtmlEncode(form.PrivacyPolicyUrl)}" target="_blank" rel="noopener noreferrer">{WebUtility.HtmlEncode(embedT.PrivacyPolicy)}</a></div>""";
        }

        var html = $$"""
            <!DOCTYPE html>
            <html lang="{{WebUtility.HtmlEncode(form.Language)}}">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <meta name="color-scheme" content="light dark" />
              <title>{{WebUtility.HtmlEncode(config.Title ?? embedT.Subscribe)}}</title>
              <style>
                *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
                :root { color-scheme: light dark; }
                body {
                  font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
                  background: {{embedBg}};
                  color: {{embedFg}};
                  padding: 20px;
                }
                .container { max-width: 480px; margin: 0 auto; }
                h2 { font-size: 1.25rem; margin-bottom: 8px; font-weight: 600; }
                .description { font-size: 0.9rem; opacity: 0.7; margin-bottom: 16px; }
                .form-row { display: flex; gap: 8px; }
                input[type="text"], input[type="email"] {
                  padding: 10px 14px;
                  border: 1px solid color-mix(in srgb, currentColor 22%, transparent);
                  border-radius: {{embedRadius}};
                  font-size: 0.95rem;
                  font-family: inherit;
                  background: color-mix(in srgb, currentColor 5%, transparent);
                  color: inherit;
                  outline: none;
                }
                input[type="text"] { width: 100%; display: block; margin-bottom: 8px; }
                input[type="email"] { flex: 1; }
                input[type="text"]:focus, input[type="email"]:focus { border-color: {{embedPrimary}}; }
                button {
                  padding: 10px 20px;
                  background: {{embedPrimary}};
                  color: #fff;
                  border: none;
                  border-radius: {{embedRadius}};
                  font-size: 0.95rem;
                  font-family: inherit;
                  font-weight: 500;
                  cursor: pointer;
                  white-space: nowrap;
                }
                button:hover { opacity: 0.9; }
                button:disabled { opacity: 0.55; cursor: not-allowed; }
                .message { margin-top: 12px; font-size: 0.9rem; }
                .message.success { color: #22c55e; }
                .message.error { color: #ef4444; }
                .consent-row { margin-top: 10px; display: flex; align-items: flex-start; gap: 8px; font-size: 0.85rem; }
                .consent-row input[type="checkbox"] { margin-top: 2px; flex-shrink: 0; width: auto; }
                .consent-row label { line-height: 1.4; }
                .consent-row a { color: inherit; text-decoration: underline; }
                .privacy-link { margin-top: 6px; font-size: 0.8rem; opacity: 0.65; }
                .privacy-link a { color: inherit; text-decoration: underline; }
              </style>
            </head>
            <body>
              <div class="container">
                {{(config.Title != null ? $"<h2>{WebUtility.HtmlEncode(config.Title)}</h2>" : "")}}
                {{(config.Description != null ? $"<p class=\"description\">{WebUtility.HtmlEncode(config.Description)}</p>" : "")}}
                <form id="nlForm">
                  {{honeypotHtml}}
                  {{(form.CollectName ? $"<input type=\"text\" name=\"name\" placeholder=\"{WebUtility.HtmlEncode(form.NameLabel ?? embedT.NamePlaceholder)}\" autocomplete=\"name\" />" : "")}}
                  <div class="form-row">
                    <input type="email" name="email" placeholder="{{WebUtility.HtmlEncode(embedT.EmailPlaceholder)}}" required />
                    <button type="submit">{{WebUtility.HtmlEncode(config.ButtonText ?? embedT.Subscribe)}}</button>
                  </div>
                  {{consentHtml}}
                  {{privacyHtml}}
                </form>
                <div id="msg" class="message"></div>
              </div>
              <script>
                const form = document.getElementById('nlForm');
                const msg = document.getElementById('msg');
                form.addEventListener('submit', async (e) => {
                  e.preventDefault();
                  const btn = form.querySelector('button');
                  btn.disabled = true;
                  msg.textContent = '';
                  msg.className = 'message';
                  try {
                    const fd = new FormData(form);
                    const body = { email: fd.get('email') };
                    const nameVal = fd.get('name');
                    if (nameVal) body.name = nameVal;
                    const hp = fd.get('website');
                    if (hp) body.website = hp;
                    const cb = form.querySelector('input[name="consent"]');
                    if (cb) body.consent = cb.checked ? 'true' : 'false';
                    const res = await fetch('/api/submission/{{formIdStr}}/subscribe', {
                      method: 'POST',
                      headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                      body: JSON.stringify(body)
                    });
                    const data = await res.json();
                    if (data.redirect) { window.location.href = data.redirect; return; }
                    if (res.ok) {
                      msg.textContent = data.message || '{{WebUtility.HtmlEncode(config.SuccessMessage ?? embedT.Success)}}';
                      msg.className = 'message success';
                      form.reset();
                    } else {
                      msg.textContent = data.error || 'Something went wrong.';
                      msg.className = 'message error';
                    }
                  } catch {
                    msg.textContent = 'Network error. Please try again.';
                    msg.className = 'message error';
                  }
                  btn.disabled = false;
                });
              </script>
            </body>
            </html>
            """;

        return Results.Content(Minify(html), "text/html");
    }

    private static async Task<IResult> GetEmbedScript(
        Guid id,
        HttpContext context,
        [FromServices] SubmissionFormService service,
        [FromServices] ISystemConfigurationService configService)
    {
        if (!configService.Get().EnableSubmissionForms)
            return Results.NotFound();

        var form = await service.GetFormAsync(id);
        if (form == null || !form.IsEnabled)
        {
            var lang = form?.Language ?? "en";
            var msg = GetUnavailableMessage(lang);
            var unavailableJs = $$"""
                (function(){
                  var id = 'beacon-nl-{{id}}';
                  var c = document.getElementById(id);
                  if (!c) return;
                  var s = c.attachShadow({mode:'closed'});
                  var style = document.createElement('style');
                  style.textContent = ':host{display:block} .unavailable{display:flex;align-items:center;justify-content:center;min-height:100%;padding:1.5rem;font-family:system-ui,-apple-system,sans-serif;color:color-mix(in srgb,currentColor 50%,transparent);font-size:0.9rem;text-align:center}';
                  s.appendChild(style);
                  var d = document.createElement('div');
                  d.className = 'unavailable';
                  d.textContent = '{{WebUtility.HtmlEncode(msg)}}';
                  s.appendChild(d);
                })();
                """;
            return Results.Content(Minify(unavailableJs), "application/javascript");
        }

        var config = DeserializeFormConfig(form.FormConfig) ?? new FormConfigDto();
        var formIdStr = form.Id.ToString();
        var configJson = JsonSerializer.Serialize(config);
        var jsT         = GetSubmissionStrings(form.Language);
        var jsSubscribe = JsonSerializer.Serialize(jsT.Subscribe).Trim('"');
        var jsSuccess   = JsonSerializer.Serialize(jsT.Success).Trim('"');
        var jsEmail     = JsonSerializer.Serialize(jsT.EmailPlaceholder).Trim('"');

        var honeypotJs = form.HoneypotEnabled
            ? "const hpDiv=document.createElement('div');hpDiv.style.cssText='position:absolute;left:-9999px;top:-9999px;';hpDiv.setAttribute('aria-hidden','true');const hpInput=document.createElement('input');hpInput.type='text';hpInput.name='website';hpInput.tabIndex=-1;hpInput.autocomplete='off';hpDiv.appendChild(hpInput);f.appendChild(hpDiv);"
            : "";

        var escapedNameLabel = JsonSerializer.Serialize(form.NameLabel ?? jsT.NamePlaceholder).Trim('"');
        var nameJs = form.CollectName
            ? $"var ni=document.createElement('input');ni.type='text';ni.name='name';ni.placeholder=\"{escapedNameLabel}\";ni.autocomplete='name';f.appendChild(ni);"
            : "";

        var consentJs = "";
        if (form.ConsentRequired)
        {
            var escapedConsentText = JsonSerializer.Serialize(form.ConsentText ?? jsT.ConsentText).Trim('"');
            consentJs = $"var cRow=document.createElement('div');cRow.className='consent-row';var cCb=document.createElement('input');cCb.type='checkbox';cCb.name='consent';cCb.id='consentCb';cCb.value='true';cCb.required=true;var cLbl=document.createElement('label');cLbl.setAttribute('for','consentCb');cLbl.textContent=\"{escapedConsentText}\";cRow.appendChild(cCb);cRow.appendChild(cLbl);f.appendChild(cRow);";
        }

        var privacyJs = "";
        if (InputValidator.IsHttpUrl(form.PrivacyPolicyUrl))
        {
            var escapedUrl = JsonSerializer.Serialize(form.PrivacyPolicyUrl).Trim('"');
            var escapedPrivacyLabel = JsonSerializer.Serialize(jsT.PrivacyPolicy).Trim('"');
            privacyJs = $"var pDiv=document.createElement('div');pDiv.className='privacy-link';var pA=document.createElement('a');pA.href=\"{escapedUrl}\";pA.target='_blank';pA.rel='noopener noreferrer';pA.textContent=\"{escapedPrivacyLabel}\";pDiv.appendChild(pA);f.appendChild(pDiv);";
        }

        var safePrimary = InputValidator.SanitizeCssColor(config.PrimaryColor, "#2563eb");
        var safeRadius  = InputValidator.SanitizeCssBorderRadius(config.BorderRadius, "8px");
        var containerBg = !string.IsNullOrWhiteSpace(config.BackgroundColor) ? InputValidator.SanitizeCssColor(config.BackgroundColor, "transparent") : "transparent";
        var containerFg = !string.IsNullOrWhiteSpace(config.TextColor) ? InputValidator.SanitizeCssColor(config.TextColor, "inherit") : "inherit";
        var nameInputCss = form.CollectName
            ? $"input[type=\"text\"] {{ width: 100%; padding: 10px 14px; border: 1px solid color-mix(in srgb,currentColor 22%,transparent); border-radius: {safeRadius}; font-size: 0.95rem; font-family: inherit; background: color-mix(in srgb,currentColor 5%,transparent); color: inherit; outline: none; margin-bottom: 8px; }} input[type=\"text\"]:focus {{ border-color: {safePrimary}; }}"
            : "";

        var js = $$"""
            (function(){
              var cfg = {{configJson}};
              var containerId = 'beacon-nl-{{formIdStr}}';
              var container = document.getElementById(containerId);
              if (!container) return;
              var shadow = container.attachShadow({mode:'closed'});
              var style = document.createElement('style');
              style.textContent = `
                * { box-sizing: border-box; margin: 0; padding: 0; }
                :host { display: block; font: inherit; color: inherit; color-scheme: light dark; }
                .container { background: {{containerBg}}; color: {{containerFg}}; padding: 16px; }
                h2 { font-size: 1.25rem; margin-bottom: 8px; font-weight: 600; }
                .description { font-size: 0.9rem; opacity: 0.7; margin-bottom: 16px; }
                .form-row { display: flex; gap: 8px; }
                {{nameInputCss}}
                input[type="email"] { flex: 1; padding: 10px 14px; border: 1px solid color-mix(in srgb,currentColor 22%,transparent); border-radius: {{safeRadius}}; font-size: 0.95rem; font-family: inherit; background: color-mix(in srgb,currentColor 5%,transparent); color: inherit; outline: none; }
                input[type="email"]:focus { border-color: {{safePrimary}}; }
                button { padding: 10px 20px; background: {{safePrimary}}; color: #fff; border: none; border-radius: {{safeRadius}}; font-size: 0.95rem; font-family: inherit; font-weight: 500; cursor: pointer; white-space: nowrap; }
                button:hover { opacity: 0.9; }
                button:disabled { opacity: 0.55; cursor: not-allowed; }
                .message { margin-top: 12px; font-size: 0.9rem; }
                .message.success { color: #22c55e; }
                .message.error { color: #ef4444; }
                .consent-row { margin-top: 10px; display: flex; align-items: flex-start; gap: 8px; font-size: 0.85rem; }
                .consent-row input[type="checkbox"] { margin-top: 2px; flex-shrink: 0; width: auto; }
                .consent-row label { line-height: 1.4; }
                .consent-row a { color: inherit; text-decoration: underline; }
                .privacy-link { margin-top: 6px; font-size: 0.8rem; opacity: 0.65; }
                .privacy-link a { color: inherit; text-decoration: underline; }
              `;
              shadow.appendChild(style);
              var wrapper = document.createElement('div');
              wrapper.className = 'container';
              if (cfg.title) { var h = document.createElement('h2'); h.textContent = cfg.title; wrapper.appendChild(h); }
              if (cfg.description) { var d = document.createElement('p'); d.className = 'description'; d.textContent = cfg.description; wrapper.appendChild(d); }
              var f = document.createElement('form');
              {{honeypotJs}}
              {{nameJs}}
              var row = document.createElement('div');
              row.className = 'form-row';
              var input = document.createElement('input');
              input.type = 'email'; input.name = 'email'; input.placeholder = '{{jsEmail}}'; input.required = true;
              var btn = document.createElement('button');
              btn.type = 'submit'; btn.textContent = cfg.buttonText || '{{jsSubscribe}}';
              row.appendChild(input); row.appendChild(btn);
              f.appendChild(row);
              {{consentJs}}
              {{privacyJs}}
              wrapper.appendChild(f);
              var msg = document.createElement('div');
              msg.className = 'message';
              wrapper.appendChild(msg);
              shadow.appendChild(wrapper);
              var baseUrl = '';
              var scripts = document.getElementsByTagName('script');
              for (var i = scripts.length - 1; i >= 0; i--) {
                var m = scripts[i].src && scripts[i].src.match(/(https?:\/\/[^/]+)\/api\/submission\//);
                if (m) { baseUrl = m[1]; break; }
              }
              f.addEventListener('submit', async function(e) {
                e.preventDefault();
                btn.disabled = true;
                msg.textContent = ''; msg.className = 'message';
                try {
                  var body = { email: input.value };
                  var ni = f.querySelector('input[name="name"]');
                  if (ni && ni.value) body.name = ni.value;
                  var hp = f.querySelector('input[name="website"]');
                  if (hp && hp.value) body.website = hp.value;
                  var ccb = f.querySelector('input[name="consent"]');
                  if (ccb) body.consent = ccb.checked ? 'true' : 'false';
                  var res = await fetch(baseUrl + '/api/submission/{{formIdStr}}/subscribe', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                    body: JSON.stringify(body)
                  });
                  var data = await res.json();
                  if (data.redirect) { window.location.href = data.redirect; return; }
                  if (res.ok) {
                    msg.textContent = data.message || cfg.successMessage || '{{jsSuccess}}';
                    msg.className = 'message success';
                    f.reset();
                  } else {
                    msg.textContent = data.error || 'Something went wrong.';
                    msg.className = 'message error';
                  }
                } catch(err) {
                  msg.textContent = 'Network error. Please try again.';
                  msg.className = 'message error';
                }
                btn.disabled = false;
              });
            })();
            """;

        return Results.Content(Minify(js), "application/javascript");
    }

    private static async Task<IResult> Subscribe(
        Guid id,
        HttpContext context,
        [FromServices] SubmissionFormService service,
        [FromServices] BucketRepository bucketRepository,
        [FromServices] SubmissionRateLimiter rateLimiter,
        [FromServices] AdminNotificationService notifications,
        [FromServices] ISystemConfigurationService configService)
    {
        if (!configService.Get().EnableSubmissionForms)
            return Results.NotFound(new { error = "Form not found or disabled" });

        // 1. Check form exists and is enabled
        var form = await service.GetFormAsync(id);
        if (form == null || !form.IsEnabled)
            return Results.NotFound(new { error = "Form not found or disabled" });

        if (await bucketRepository.IsArchivedAsync(form.Bucket.Trim().ToLowerInvariant()))
            return Results.Conflict(new { error = "Bucket is archived" });

        // Parse request body (JSON or form-encoded)
        string? email = null;
        string? name = null;
        string? website = null;
        string? consent = null;
        string? redirectSuccess = null;
        string? redirectError = null;
        var contentType = context.Request.ContentType ?? "";
        var isFormPost = contentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);

        // If the client explicitly accepts JSON, always respond with JSON (defense-in-depth)
        if (context.Request.Headers.Accept.Any(a =>
            a != null && a.Contains("application/json", StringComparison.OrdinalIgnoreCase)))
            isFormPost = false;

        if (isFormPost)
        {
            var formData = await context.Request.ReadFormAsync();
            email = formData["email"].FirstOrDefault();
            name = formData["name"].FirstOrDefault();
            website = formData["website"].FirstOrDefault();
            consent = formData["consent"].FirstOrDefault();
            redirectSuccess = formData["redirect_success"].FirstOrDefault();
            redirectError = formData["redirect_error"].FirstOrDefault();
        }
        else
        {
            var request = await context.Request.ReadFromJsonAsync<SubmissionSubscribeRequest>();
            email = request?.Email;
            name = request?.Name;
            website = request?.Website;
            consent = request?.Consent;
        }

        if (name != null && name.Length > 250)
            name = name[..250];

        // Validate POST-supplied redirects against allowed origins; fall back to form-level redirects
        if (isFormPost)
        {
            if (redirectSuccess != null && !IsRedirectAllowedForForm(form, redirectSuccess))
                redirectSuccess = null;
            if (redirectError != null && !IsRedirectAllowedForForm(form, redirectError))
                redirectError = null;

            redirectSuccess ??= form.RedirectSuccess;
            redirectError ??= form.RedirectError;

            if (form.DisableRedirects || !form.RedirectFormPost)
            {
                redirectSuccess = null;
                redirectError = null;
            }
        }

        // Validate origin
        var origin = context.Request.Headers.Origin.FirstOrDefault()
                  ?? context.Request.Headers.Referer.FirstOrDefault();

        // Extract origin from Referer if it's a full URL
        if (origin != null && Uri.TryCreate(origin, UriKind.Absolute, out var refererUri))
        {
            origin = $"{refererUri.Scheme}://{refererUri.Authority}";
        }

        if (!service.ValidateOrigin(form, origin))
        {
            Serilog.Log.Warning("Blocked subscription attempt for form {FormId} from disallowed origin: {Origin}", form.Id, origin);
            if (isFormPost && IsValidRedirectUrl(redirectError))
                return Results.Redirect(AppendQuery(redirectError!, "error", "origin_not_allowed"));
            if (isFormPost)
                return FormPostHtmlResult(form, "Origin not allowed.", true);
            return Results.Json(BuildJsonError("Origin not allowed", form, "origin_not_allowed"), statusCode: 403);
        }

        // CORS headers come from the PublicSubmission policy. Writing them here too produced a duplicate
        // Access-Control-Allow-Origin, which browsers reject. The allowlist above is the real control.

        // Rate limiting
        var clientIp = SubmissionRateLimiter.GetClientIp(context);
        if (rateLimiter.IsRateLimited(form.Id, clientIp, form.RateLimitPerMinute))
        {
            if (isFormPost && IsValidRedirectUrl(redirectError))
                return Results.Redirect(AppendQuery(redirectError!, "error", "rate_limited"));
            if (isFormPost)
                return FormPostHtmlResult(form, "Too many requests. Please try again later.", true);
            return Results.Json(BuildJsonError("Too many requests. Please try again later.", form, "rate_limited"), statusCode: 429);
        }

        // Honeypot check
        if (form.HoneypotEnabled && !string.IsNullOrEmpty(website))
        {
            // Bot detected, return success silently
            if (isFormPost && IsValidRedirectUrl(redirectSuccess))
                return Results.Redirect(redirectSuccess!);
            var hpConfig = DeserializeFormConfig(form.FormConfig);
            var hpT = GetSubmissionStrings(form.Language);
            if (isFormPost)
                return FormPostHtmlResult(form, hpConfig?.SuccessMessage ?? hpT.Success, false);
            return Results.Ok(BuildJsonSuccess(hpConfig?.SuccessMessage ?? hpT.Success, form));
        }

        // Consent validation
        var subT = GetSubmissionStrings(form.Language);
        if (form.ConsentRequired)
        {
            var consentGiven = string.Equals(consent, "true", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(consent, "on", StringComparison.OrdinalIgnoreCase);
            if (!consentGiven)
            {
                if (isFormPost && IsValidRedirectUrl(redirectError))
                    return Results.Redirect(AppendQuery(redirectError!, "error", "consent_required"));
                if (isFormPost)
                    return FormPostHtmlResult(form, "Consent is required to subscribe.", true);
                return Results.BadRequest(BuildJsonError("Consent is required to subscribe", form, "consent_required"));
            }
        }

        // Email validation
        var emailValidation = InputValidator.ValidateEmail(email);
        if (!emailValidation.IsValid)
        {
            if (isFormPost && IsValidRedirectUrl(redirectError))
                return Results.Redirect(AppendQuery(redirectError!, "error", "invalid_email"));
            if (isFormPost)
                return FormPostHtmlResult(form, emailValidation.Error!, true);
            return Results.BadRequest(BuildJsonError(emailValidation.Error!, form, "invalid_email"));
        }

        // Subscribe
        var effectiveConsentText = form.ConsentText ?? subT.ConsentText;
        var effectiveName = form.CollectName && !string.IsNullOrWhiteSpace(name) ? name.Trim() : null;
        await service.SubscribeAsync(form, email!, form.ConsentRequired ? effectiveConsentText : null, origin, effectiveName);
        await notifications.PublishConsentUpdateAsync(new ConsentUpdateNotification(form.Bucket));

        if (isFormPost && IsValidRedirectUrl(redirectSuccess))
            return Results.Redirect(redirectSuccess!);

        var config = DeserializeFormConfig(form.FormConfig);
        var successMessage = config?.SuccessMessage ?? subT.Success;
        if (isFormPost)
            return FormPostHtmlResult(form, successMessage, false);
        return Results.Ok(BuildJsonSuccess(successMessage, form));
    }

    private static IResult FormPostHtmlResult(SubmissionForm form, string message, bool isError)
    {
        var config = DeserializeFormConfig(form.FormConfig) ?? new FormConfigDto();
        var primary = InputValidator.SanitizeCssColor(config.PrimaryColor, "#2563eb");
        var radius  = InputValidator.SanitizeCssBorderRadius(config.BorderRadius, "8px");
        var bg      = !string.IsNullOrWhiteSpace(config.BackgroundColor) ? InputValidator.SanitizeCssColor(config.BackgroundColor, "Canvas") : "Canvas";
        var fg      = !string.IsNullOrWhiteSpace(config.TextColor) ? InputValidator.SanitizeCssColor(config.TextColor, "CanvasText") : "CanvasText";
        var msgColor = isError ? "#ef4444" : "#22c55e";
        var encodedMsg = WebUtility.HtmlEncode(message);
        var lang = WebUtility.HtmlEncode(form.Language);

        var html = $$"""
            <!DOCTYPE html>
            <html lang="{{lang}}">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <meta name="color-scheme" content="light dark" />
              <title>{{WebUtility.HtmlEncode(isError ? "Error" : "Success")}}</title>
              <style>
                *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
                :root { color-scheme: light dark; }
                body {
                  font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
                  background: {{bg}}; color: {{fg}};
                  display: flex; align-items: center; justify-content: center; min-height: 100vh; padding: 2rem;
                }
                .card { max-width: 480px; width: 100%; text-align: center; }
                .icon { font-size: 2.5rem; margin-bottom: 1rem; }
                .message { font-size: 1.1rem; line-height: 1.5; color: {{msgColor}}; margin-bottom: 1.5rem; }
                a {
                  display: inline-block; padding: 10px 24px;
                  background: {{primary}}; color: #fff; text-decoration: none;
                  border-radius: {{radius}}; font-size: 0.95rem; font-weight: 500;
                }
                a:hover { opacity: 0.9; }
              </style>
            </head>
            <body>
              <div class="card">
                <div class="icon">{{(isError ? "&#9888;" : "&#10003;")}}</div>
                <p class="message">{{encodedMsg}}</p>
                <a href="javascript:history.back()">&#8592; Go back</a>
              </div>
            </body>
            </html>
            """;

        return Results.Content(Minify(html), "text/html");
    }

    private static string Minify(string content) =>
        ConsentEndpoints.Minify(content);

    private static bool IsValidRedirectUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && (uri.Scheme == "http" || uri.Scheme == "https");
    }

    internal static bool IsRedirectAllowedForForm(SubmissionForm form, string? url)
    {
        if (!IsValidRedirectUrl(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var redirectOrigin = $"{uri.Scheme}://{uri.Authority}".TrimEnd('/').ToLowerInvariant();

        try
        {
            var allowedOrigins = System.Text.Json.JsonSerializer.Deserialize<List<string>>(form.AllowedOrigins);
            if (allowedOrigins == null || allowedOrigins.Count == 0)
                return false;

            return allowedOrigins.Any(o => o.TrimEnd('/').ToLowerInvariant() == redirectOrigin);
        }
        catch
        {
            return false;
        }
    }

    private static object BuildJsonSuccess(string message, SubmissionForm form)
    {
        if (!form.DisableRedirects && form.RedirectJsEmbed && IsValidRedirectUrl(form.RedirectSuccess))
            return new { message, redirect = form.RedirectSuccess };
        return new { message };
    }

    private static object BuildJsonError(string error, SubmissionForm form, string errorCode)
    {
        if (!form.DisableRedirects && form.RedirectJsEmbed && IsValidRedirectUrl(form.RedirectError))
            return new { error, redirect = AppendQuery(form.RedirectError!, "error", errorCode) };
        return new { error };
    }

    private static string AppendQuery(string url, string key, string value)
    {
        var separator = url.Contains('?') ? "&" : "?";
        return $"{url}{separator}{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
    }

    // Helpers

    private static List<string> DeserializeOrigins(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }

    private static FormConfigDto? DeserializeFormConfig(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<FormConfigDto>(json); }
        catch { return null; }
    }

    private static Dictionary<string, string>? DeserializeCustomFields(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json); }
        catch { return null; }
    }

    private static Beacon.Localization.FormLocalization.SubmissionStrings GetSubmissionStrings(string? language) =>
        Beacon.Localization.FormLocalization.GetSubmissionStrings(language);

    private static string GetUnavailableMessage(string language) =>
        Beacon.Localization.FormLocalization.GetUnavailableMessage(language);
    private static string FormUnavailableHtml(string language, FormConfigDto? config)
    {
        var lang = WebUtility.HtmlEncode(language);
        var bg = WebUtility.HtmlEncode(config?.BackgroundColor ?? "transparent");
        var text = WebUtility.HtmlEncode(config?.TextColor ?? "inherit");
        var msg = WebUtility.HtmlEncode(GetUnavailableMessage(language));

        return $$"""
            <!DOCTYPE html>
            <html lang="{{lang}}">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <title>Unavailable</title>
              <style>
                *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
                html, body { height: 100%; overflow: hidden; }
                body {
                  font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
                  background: {{bg}};
                  color: {{text}};
                  display: flex;
                  align-items: center;
                  justify-content: center;
                  padding: 1.5rem;
                }
                .message {
                  font-size: 0.9rem;
                  opacity: 0.5;
                  text-align: center;
                  line-height: 1.5;
                }
              </style>
            </head>
            <body>
              <p class="message">{{msg}}</p>
            </body>
            </html>
            """;
    }
}

// DTOs

public sealed class FormConfigDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? ButtonText { get; set; }
    public string? SuccessMessage { get; set; }
    public string? PrimaryColor { get; set; }
    public string? BackgroundColor { get; set; }
    public string? TextColor { get; set; }
    public string? BorderRadius { get; set; }
}

public sealed class CreateSubmissionFormRequest
{
    public string Name { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public string Permission { get; set; } = string.Empty;
    public List<string> AllowedOrigins { get; set; } = [];
    public FormConfigDto? FormConfig { get; set; }
    /// <summary>Omit to use the system default from settings.</summary>
    public int? RateLimitPerMinute { get; set; }
    /// <summary>Omit to use the system default from settings.</summary>
    public bool? HoneypotEnabled { get; set; }
    public bool DoubleOptIn { get; set; } = false;
    public string Language { get; set; } = "en";
    public bool IsEnabled { get; set; } = true;
    public string? RedirectSuccess { get; set; }
    public string? RedirectError { get; set; }
    public bool RedirectFormPost { get; set; } = true;
    public bool RedirectJsEmbed { get; set; } = false;
    public bool DisableRedirects { get; set; } = false;
    /// <summary>Omit to use the system default from settings.</summary>
    public bool? ConsentRequired { get; set; }
    public string? ConsentText { get; set; }
    public string? PrivacyPolicyUrl { get; set; }
    public bool CollectName { get; set; } = false;
    public string? NameLabel { get; set; }
    public Dictionary<string, string>? CustomFields { get; set; }
}

public sealed class UpdateSubmissionFormRequest
{
    public string? Name { get; set; }
    public string? Bucket { get; set; }
    public string? Permission { get; set; }
    public List<string>? AllowedOrigins { get; set; }
    public FormConfigDto? FormConfig { get; set; }
    public int? RateLimitPerMinute { get; set; }
    public bool? HoneypotEnabled { get; set; }
    public bool? DoubleOptIn { get; set; }
    public string? Language { get; set; }
    public bool? IsEnabled { get; set; }
    public string? RedirectSuccess { get; set; }
    public string? RedirectError { get; set; }
    public bool? RedirectFormPost { get; set; }
    public bool? RedirectJsEmbed { get; set; }
    public bool? DisableRedirects { get; set; }
    public bool? ConsentRequired { get; set; }
    public string? ConsentText { get; set; }
    public string? PrivacyPolicyUrl { get; set; }
    public bool? CollectName { get; set; }
    public string? NameLabel { get; set; }
    public Dictionary<string, string>? CustomFields { get; set; }
}

public sealed class SubmissionSubscribeRequest
{
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Website { get; set; } // Honeypot field
    public string? Consent { get; set; }
}
