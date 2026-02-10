using System.Net;
using System.Text.Json;
using Beacon.Core.Models;
using Beacon.Core.Services;
using Beacon.Core.Validation;
using Beacon.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Beacon.Api;

public static class SubmissionEndpoints
{
    public static void MapSubmissionEndpoints(this IEndpointRouteBuilder routes)
    {
        // Admin endpoints (require authorization)
        routes.MapGet("/api/admin/submissions", GetAllForms)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapGet("/api/admin/submissions/{id:guid}", GetForm)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapPost("/api/admin/submissions", CreateForm)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapPut("/api/admin/submissions/{id:guid}", UpdateForm)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapDelete("/api/admin/submissions/{id:guid}", DeleteForm)
            .RequireAuthorization()
            .ExcludeFromDescription();

        // Public endpoints (no auth, origin-validated by the handler)
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
            .ExcludeFromDescription();

        routes.MapMethods("/api/submission/{id:guid}/subscribe", ["OPTIONS"], HandleCorsPreflightSubscribe)
            .AllowAnonymous()
            .RequireCors("PublicSubmission")
            .ExcludeFromDescription();
    }

    // Admin endpoints

    private static async Task<IResult> GetAllForms(
        [FromServices] ISubmissionFormService service)
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
            f.CreatedAt,
            f.UpdatedAt,
            f.SubmissionCount
        }));
    }

    private static async Task<IResult> GetForm(
        Guid id,
        [FromServices] ISubmissionFormService service)
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
            form.CreatedAt,
            form.UpdatedAt,
            form.SubmissionCount
        });
    }

    private static async Task<IResult> CreateForm(
        [FromBody] CreateSubmissionFormRequest request,
        [FromServices] ISubmissionFormService service)
    {
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

        var form = new SubmissionForm
        {
            Name = request.Name.Trim(),
            Bucket = request.Bucket.Trim().ToLowerInvariant(),
            Permission = string.Join(", ", permissions),
            AllowedOrigins = JsonSerializer.Serialize(request.AllowedOrigins.Select(o => o.TrimEnd('/'))),
            FormConfig = request.FormConfig != null ? JsonSerializer.Serialize(request.FormConfig) : null,
            EncryptedApiToken = "", // Will be set by service
            RateLimitPerMinute = request.RateLimitPerMinute > 0 ? request.RateLimitPerMinute : 10,
            HoneypotEnabled = request.HoneypotEnabled,
            DoubleOptIn = request.DoubleOptIn,
            Language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language.Trim(),
            RedirectSuccess = string.IsNullOrWhiteSpace(request.RedirectSuccess) ? null : request.RedirectSuccess.Trim(),
            RedirectError = string.IsNullOrWhiteSpace(request.RedirectError) ? null : request.RedirectError.Trim(),
            IsEnabled = request.IsEnabled
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
        [FromServices] ISubmissionFormService service)
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
            existing.FormConfig = JsonSerializer.Serialize(request.FormConfig);
        }

        if (request.RateLimitPerMinute.HasValue)
            existing.RateLimitPerMinute = request.RateLimitPerMinute.Value > 0 ? request.RateLimitPerMinute.Value : 10;

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
            existing.CreatedAt,
            existing.UpdatedAt,
            existing.SubmissionCount
        });
    }

    private static async Task<IResult> DeleteForm(
        Guid id,
        [FromServices] ISubmissionFormService service)
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
        [FromServices] ISubmissionFormService service)
    {
        var form = await service.GetFormAsync(id);
        if (form == null || !form.IsEnabled)
            return Results.NotFound();

        var origins = DeserializeOrigins(form.AllowedOrigins);
        var cspOrigins = string.Join(" ", origins);

        context.Response.Headers["Content-Security-Policy"] = $"frame-ancestors {cspOrigins}";
        context.Response.Headers["X-Frame-Options"] = "DENY"; // Fallback, overridden by CSP

        var config = DeserializeFormConfig(form.FormConfig) ?? new FormConfigDto();
        var formIdStr = form.Id.ToString();

        var honeypotHtml = form.HoneypotEnabled
            ? """<div style="position:absolute;left:-9999px;top:-9999px;" aria-hidden="true"><input type="text" name="website" tabindex="-1" autocomplete="off" /></div>"""
            : "";

        var html = $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <title>{{WebUtility.HtmlEncode(config.Title ?? "Subscribe")}}</title>
              <style>
                * { box-sizing: border-box; margin: 0; padding: 0; }
                body {
                  font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
                  background: {{WebUtility.HtmlEncode(config.BackgroundColor ?? "#ffffff")}};
                  color: {{WebUtility.HtmlEncode(config.TextColor ?? "#111111")}};
                  padding: 20px;
                }
                .container { max-width: 480px; margin: 0 auto; }
                h2 { font-size: 1.25rem; margin-bottom: 8px; font-weight: 600; }
                .description { font-size: 0.9rem; opacity: 0.75; margin-bottom: 16px; }
                .form-row { display: flex; gap: 8px; }
                input[type="email"] {
                  flex: 1;
                  padding: 10px 14px;
                  border: 1px solid #d1d5db;
                  border-radius: {{WebUtility.HtmlEncode(config.BorderRadius ?? "8px")}};
                  font-size: 0.95rem;
                  outline: none;
                }
                input[type="email"]:focus { border-color: {{WebUtility.HtmlEncode(config.PrimaryColor ?? "#2563eb")}}; }
                button {
                  padding: 10px 20px;
                  background: {{WebUtility.HtmlEncode(config.PrimaryColor ?? "#2563eb")}};
                  color: #fff;
                  border: none;
                  border-radius: {{WebUtility.HtmlEncode(config.BorderRadius ?? "8px")}};
                  font-size: 0.95rem;
                  font-weight: 500;
                  cursor: pointer;
                  white-space: nowrap;
                }
                button:hover { opacity: 0.9; }
                button:disabled { opacity: 0.6; cursor: not-allowed; }
                .message { margin-top: 12px; font-size: 0.9rem; }
                .message.success { color: #16a34a; }
                .message.error { color: #dc2626; }
              </style>
            </head>
            <body>
              <div class="container">
                {{(config.Title != null ? $"<h2>{WebUtility.HtmlEncode(config.Title)}</h2>" : "")}}
                {{(config.Description != null ? $"<p class=\"description\">{WebUtility.HtmlEncode(config.Description)}</p>" : "")}}
                <form id="nlForm">
                  {{honeypotHtml}}
                  <div class="form-row">
                    <input type="email" name="email" placeholder="you@example.com" required />
                    <button type="submit">{{WebUtility.HtmlEncode(config.ButtonText ?? "Subscribe")}}</button>
                  </div>
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
                    const hp = fd.get('website');
                    if (hp) body.website = hp;
                    const res = await fetch('/api/submission/{{formIdStr}}/subscribe', {
                      method: 'POST',
                      headers: { 'Content-Type': 'application/json' },
                      body: JSON.stringify(body)
                    });
                    const data = await res.json();
                    if (res.ok) {
                      msg.textContent = data.message || '{{WebUtility.HtmlEncode(config.SuccessMessage ?? "Thanks for subscribing!")}}';
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

        return Results.Content(html, "text/html");
    }

    private static async Task<IResult> GetEmbedScript(
        Guid id,
        HttpContext context,
        [FromServices] ISubmissionFormService service)
    {
        var form = await service.GetFormAsync(id);
        if (form == null || !form.IsEnabled)
            return Results.NotFound();

        var config = DeserializeFormConfig(form.FormConfig) ?? new FormConfigDto();
        var formIdStr = form.Id.ToString();
        var configJson = JsonSerializer.Serialize(config);

        var honeypotJs = form.HoneypotEnabled
            ? "const hpDiv=document.createElement('div');hpDiv.style.cssText='position:absolute;left:-9999px;top:-9999px;';hpDiv.setAttribute('aria-hidden','true');const hpInput=document.createElement('input');hpInput.type='text';hpInput.name='website';hpInput.tabIndex=-1;hpInput.autocomplete='off';hpDiv.appendChild(hpInput);f.appendChild(hpDiv);"
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
                :host { display: block; font-family: system-ui, -apple-system, sans-serif; }
                .container { background: ${cfg.backgroundColor||'#ffffff'}; color: ${cfg.textColor||'#111111'}; padding: 16px; }
                h2 { font-size: 1.25rem; margin-bottom: 8px; font-weight: 600; }
                .description { font-size: 0.9rem; opacity: 0.75; margin-bottom: 16px; }
                .form-row { display: flex; gap: 8px; }
                input[type="email"] { flex: 1; padding: 10px 14px; border: 1px solid #d1d5db; border-radius: ${cfg.borderRadius||'8px'}; font-size: 0.95rem; outline: none; }
                input[type="email"]:focus { border-color: ${cfg.primaryColor||'#2563eb'}; }
                button { padding: 10px 20px; background: ${cfg.primaryColor||'#2563eb'}; color: #fff; border: none; border-radius: ${cfg.borderRadius||'8px'}; font-size: 0.95rem; font-weight: 500; cursor: pointer; white-space: nowrap; }
                button:hover { opacity: 0.9; }
                button:disabled { opacity: 0.6; cursor: not-allowed; }
                .message { margin-top: 12px; font-size: 0.9rem; }
                .message.success { color: #16a34a; }
                .message.error { color: #dc2626; }
              `;
              shadow.appendChild(style);
              var wrapper = document.createElement('div');
              wrapper.className = 'container';
              if (cfg.title) { var h = document.createElement('h2'); h.textContent = cfg.title; wrapper.appendChild(h); }
              if (cfg.description) { var d = document.createElement('p'); d.className = 'description'; d.textContent = cfg.description; wrapper.appendChild(d); }
              var f = document.createElement('form');
              {{honeypotJs}}
              var row = document.createElement('div');
              row.className = 'form-row';
              var input = document.createElement('input');
              input.type = 'email'; input.name = 'email'; input.placeholder = 'you@example.com'; input.required = true;
              var btn = document.createElement('button');
              btn.type = 'submit'; btn.textContent = cfg.buttonText || 'Subscribe';
              row.appendChild(input); row.appendChild(btn);
              f.appendChild(row);
              wrapper.appendChild(f);
              var msg = document.createElement('div');
              msg.className = 'message';
              wrapper.appendChild(msg);
              shadow.appendChild(wrapper);
              var baseUrl = document.currentScript ? document.currentScript.src.replace(/\/api\/submission\/.*/, '') : '';
              f.addEventListener('submit', async function(e) {
                e.preventDefault();
                btn.disabled = true;
                msg.textContent = ''; msg.className = 'message';
                try {
                  var body = { email: input.value };
                  var hp = f.querySelector('input[name="website"]');
                  if (hp && hp.value) body.website = hp.value;
                  var res = await fetch(baseUrl + '/api/submission/{{formIdStr}}/subscribe', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(body)
                  });
                  var data = await res.json();
                  if (res.ok) {
                    msg.textContent = data.message || cfg.successMessage || 'Thanks for subscribing!';
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

        return Results.Content(js, "application/javascript");
    }

    private static async Task<IResult> Subscribe(
        Guid id,
        HttpContext context,
        [FromServices] ISubmissionFormService service,
        [FromServices] SubmissionRateLimiter rateLimiter)
    {
        // 1. Check form exists and is enabled
        var form = await service.GetFormAsync(id);
        if (form == null || !form.IsEnabled)
            return Results.NotFound(new { error = "Form not found or disabled" });

        // Parse request body (JSON or form-encoded)
        string? email = null;
        string? website = null;
        string? redirectSuccess = null;
        string? redirectError = null;
        var contentType = context.Request.ContentType ?? "";
        var isFormPost = contentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);

        if (isFormPost)
        {
            var formData = await context.Request.ReadFormAsync();
            email = formData["email"].FirstOrDefault();
            website = formData["website"].FirstOrDefault();
            redirectSuccess = formData["redirect_success"].FirstOrDefault();
            redirectError = formData["redirect_error"].FirstOrDefault();
        }
        else
        {
            var request = await context.Request.ReadFromJsonAsync<SubmissionSubscribeRequest>();
            email = request?.Email;
            website = request?.Website;
        }

        // Fall back to form-level redirect URLs if not provided in the POST body
        if (isFormPost)
        {
            redirectSuccess ??= form.RedirectSuccess;
            redirectError ??= form.RedirectError;
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
            return Results.Json(new { error = "Origin not allowed" }, statusCode: 403);
        }

        // Set dynamic CORS headers
        if (origin != null)
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = origin;
            context.Response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
            context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
        }

        // Rate limiting
        var clientIp = SubmissionRateLimiter.GetClientIp(context);
        if (rateLimiter.IsRateLimited(form.Id, clientIp, form.RateLimitPerMinute))
        {
            if (isFormPost && IsValidRedirectUrl(redirectError))
                return Results.Redirect(AppendQuery(redirectError!, "error", "rate_limited"));
            return Results.Json(new { error = "Too many requests. Please try again later." }, statusCode: 429);
        }

        // Honeypot check
        if (form.HoneypotEnabled && !string.IsNullOrEmpty(website))
        {
            // Bot detected, return success silently
            if (isFormPost && IsValidRedirectUrl(redirectSuccess))
                return Results.Redirect(redirectSuccess!);
            return Results.Ok(new { message = "Thanks for subscribing!" });
        }

        // Email validation
        var emailValidation = InputValidator.ValidateEmail(email);
        if (!emailValidation.IsValid)
        {
            if (isFormPost && IsValidRedirectUrl(redirectError))
                return Results.Redirect(AppendQuery(redirectError!, "error", "invalid_email"));
            return Results.BadRequest(new { error = emailValidation.Error });
        }

        // Subscribe
        await service.SubscribeAsync(form, email!);

        if (isFormPost && IsValidRedirectUrl(redirectSuccess))
            return Results.Redirect(redirectSuccess!);

        var config = DeserializeFormConfig(form.FormConfig);
        return Results.Ok(new { message = config?.SuccessMessage ?? "Thanks for subscribing!" });
    }

    private static bool IsValidRedirectUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && (uri.Scheme == "http" || uri.Scheme == "https");
    }

    private static string AppendQuery(string url, string key, string value)
    {
        var separator = url.Contains('?') ? "&" : "?";
        return $"{url}{separator}{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
    }

    private static async Task<IResult> HandleCorsPreflightSubscribe(
        Guid id,
        HttpContext context,
        [FromServices] ISubmissionFormService service)
    {
        var form = await service.GetFormAsync(id);
        if (form == null || !form.IsEnabled)
            return Results.NotFound();

        var origin = context.Request.Headers.Origin.FirstOrDefault();
        if (origin != null && service.ValidateOrigin(form, origin))
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = origin;
            context.Response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
            context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
            context.Response.Headers["Access-Control-Max-Age"] = "86400";
        }

        return Results.Ok();
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
    public int RateLimitPerMinute { get; set; } = 10;
    public bool HoneypotEnabled { get; set; } = true;
    public bool DoubleOptIn { get; set; } = false;
    public string Language { get; set; } = "en";
    public bool IsEnabled { get; set; } = true;
    public string? RedirectSuccess { get; set; }
    public string? RedirectError { get; set; }
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
}

public sealed class SubmissionSubscribeRequest
{
    public string Email { get; set; } = string.Empty;
    public string? Website { get; set; } // Honeypot field
}
