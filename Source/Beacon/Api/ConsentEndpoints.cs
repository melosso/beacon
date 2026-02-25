using System.Net;
using System.Security.Cryptography;
using System.Text;
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
            .WithOpenApi()
            .WithDescription("Display the email preference management page for a user. Token is generated via /api/tokens/generate.");

        routes.MapPost("/u/{token}", ProcessPreferenceUpdate)
            .WithName("ProcessPreferenceUpdate")
            .WithTags(PermissionControlTag)
            .WithOpenApi()
            .WithDescription("Process user preference updates from the form submission.");

        routes.MapPost("/api/consent/check", CheckConsent)
            .WithName("CheckConsent")
            .WithTags(PermissionTag)
            .WithOpenApi()
            .RequireAuthorization()
            .WithDescription("Check if an email is opted-in or opted-out for a specific permission.");
    }

    private static async Task<IResult> ShowPreferencePage(
        string token,
        HttpContext context,
        [FromServices] IAntiforgery antiforgery,
        [FromServices] TokenValidator validator,
        [FromServices] IConsentService consentService,
        [FromServices] ITokenUsageRepository tokenUsageRepository,
        [FromServices] ISystemConfigurationService configService)
    {
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

        // Only check token usage if replay is not allowed
        if (!result.Payload.AllowReplay)
        {
            var tokenHash = ComputeTokenHash(token);
            if (await tokenUsageRepository.IsTokenUsedAsync(tokenHash))
            {
                return Results.Content(GetStatusPage("already_processed", lang), "text/html");
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

        return Results.Content(GetPreferencePage(
            token,
            result.Payload.Email,
            permissionStates,
            tokens.RequestToken!,
            tokens.FormFieldName,
            result.Payload.Language), "text/html");
    }

    private static async Task<IResult> ProcessPreferenceUpdate(
        string token,
        HttpContext context,
        [FromServices] IAntiforgery antiforgery,
        [FromServices] TokenValidator validator,
        [FromServices] IConsentService consentService,
        [FromServices] IConsentRepository consentRepository,
        [FromServices] IWebhookService webhookService,
        [FromServices] IBucketRepository bucketRepository,
        [FromServices] EmailHasher emailHasher,
        [FromServices] ITokenUsageRepository tokenUsageRepository,
        [FromServices] IAdminNotificationService notifications)
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

        // Only check token usage if replay is not allowed
        if (!result.Payload.AllowReplay)
        {
            var tokenHash = ComputeTokenHash(token);
            if (await tokenUsageRepository.IsTokenUsedAsync(tokenHash))
            {
                return Results.Content(GetStatusPage("already_processed", lang), "text/html");
            }
        }

        if (await bucketRepository.IsArchivedAsync(result.Payload.Bucket.Trim().ToLowerInvariant()))
        {
            return Results.Content(GetStatusPage("invalid", lang), "text/html");
        }

        var form = await context.Request.ReadFormAsync();
        var action = form["action"].ToString();

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
                    ConsentSource.Url);

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
                return Results.Content(GetStatusPage("unsubscribed", lang, optedOut: validPermissions), "text/html");
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
                        ConsentSource.Url);
                    optedOut.Add(permission);
                }
                else
                {
                    await consentService.OverrideAsync(
                        result.Payload.Bucket,
                        result.Payload.Email,
                        permission,
                        ConsentStatus.OptedIn);
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
            return Results.Content(GetStatusPage("updated", lang, [.. optedOut], [.. keptIn]), "text/html");
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
        catch
        {
            // Silently ignore webhook failures to prevent disrupt
        }
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
          --accent: #2563eb;
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
            --accent: #7aa2ff;
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
        }

        p {
          margin: 0 0 24px;
          color: color-mix(in srgb, var(--fg) 75%, transparent);
        }

        .icon {
          font-size: 3rem;
          margin-bottom: 16px;
        }

        .icon.success { color: var(--success); }
        .icon.warning { color: var(--warning); }
        .icon.error { color: var(--error); }
        .icon.info { color: var(--info); }

        .center { text-align: center; }
        """;

    private static readonly Dictionary<string, (string Title, string Description, string SaveButton, string UnsubscribeButton, string PreferencesFor)> Translations = new()
    {
        ["en"] = (
            Title: "Email preferences",
            Description: "You're receiving these emails because you previously opted in. You can change that here.",
            SaveButton: "Save preferences",
            UnsubscribeButton: "Unsubscribe from all",
            PreferencesFor: "Preferences for:"
        ),
        ["de"] = (
            Title: "E-Mail-Einstellungen",
            Description: "Sie erhalten diese E-Mails, weil Sie sich zuvor angemeldet haben. Hier können Sie das ändern.",
            SaveButton: "Einstellungen speichern",
            UnsubscribeButton: "Von allem abmelden",
            PreferencesFor: "Einstellungen für:"
        ),
        ["fr"] = (
            Title: "Préférences e-mail",
            Description: "Vous recevez ces e-mails parce que vous vous êtes inscrit précédemment. Vous pouvez modifier cela ici.",
            SaveButton: "Enregistrer les préférences",
            UnsubscribeButton: "Se désabonner de tout",
            PreferencesFor: "Préférences pour :"
        ),
        ["nl"] = (
            Title: "E-mailvoorkeuren",
            Description: "Je krijgt deze e-mails omdat je je eerder hebt aangemeld. Je kunt hier je voorkeuren aanpassen.",
            SaveButton: "Voorkeuren opslaan",
            UnsubscribeButton: "Alles afmelden",
            PreferencesFor: "Voorkeuren voor:"
        ),
        ["pl"] = (
            Title: "Preferencje e-mail",
            Description: "Otrzymujesz te e-maile, ponieważ wcześniej wyraziłeś zgodę. Możesz to zmienić tutaj.",
            SaveButton: "Zapisz preferencje",
            UnsubscribeButton: "Wypisz się ze wszystkiego",
            PreferencesFor: "Preferencje dla:"
        ),
        ["es"] = (
            Title: "Preferencias de correo",
            Description: "Recibe estos correos porque se suscribió anteriormente. Puede cambiar eso aquí.",
            SaveButton: "Guardar preferencias",
            UnsubscribeButton: "Cancelar todas las suscripciones",
            PreferencesFor: "Preferencias para:"
        )
    };

    // New record to hold status page translations
    private record StatusStrings(
        string ExpiredTitle, string ExpiredMsg,
        string InvalidTitle, string InvalidMsg,
        string ProcessedTitle, string ProcessedMsg,
        string UnsubTitle, string UnsubMsgPrefix,
        string UpdatedTitle, string UpdatedOptOutPrefix, string UpdatedOptInPrefix,
        string SuccessTitle, string SuccessMsg
    );

    private static readonly Dictionary<string, StatusStrings> StatusTranslations = new()
    {
        ["en"] = new(
            "Link expired", "This link has expired. Please use the link in a more recent email.",
            "Invalid link", "This link is invalid. Please use the link from your email.",
            "Already processed", "Your preferences have already been updated using this link.",
            "Unsubscribed", "You have been unsubscribed from:",
            "Preferences updated", "Unsubscribed from:", "Still subscribed to:",
            "Success", "Your preferences have been updated."
        ),
        ["de"] = new(
            "Link abgelaufen", "Dieser Link ist abgelaufen. Bitte verwenden Sie den Link in einer aktuelleren E-Mail.",
            "Ungültiger Link", "Dieser Link ist ungültig. Bitte verwenden Sie den Link aus Ihrer E-Mail.",
            "Bereits bearbeitet", "Ihre Einstellungen wurden bereits über diesen Link aktualisiert.",
            "Abgemeldet", "Sie wurden abgemeldet von:",
            "Einstellungen aktualisiert", "Abgemeldet von:", "Noch angemeldet für:",
            "Erfolg", "Ihre Einstellungen wurden aktualisiert."
        ),
        ["fr"] = new(
            "Lien expiré", "Ce lien a expiré. Veuillez utiliser le lien contenu dans un e-mail plus récent.",
            "Lien invalide", "Ce lien est invalide. Veuillez utiliser le lien contenu dans votre e-mail.",
            "Déjà traité", "Vos préférences ont déjà été mises à jour via ce lien.",
            "Désabonné", "Vous avez été désabonné de :",
            "Préférences mises à jour", "Désabonné de :", "Toujours abonné à :",
            "Succès", "Vos préférences ont été mises à jour."
        ),
        ["nl"] = new(
            "Link verlopen", "Deze link is verlopen. Gebruik de link in een recentere e-mail.",
            "Ongeldige link", "Deze link is ongeldig. Gebruik de link uit je e-mail.",
            "Reeds verwerkt", "Je voorkeuren zijn al bijgewerkt via deze link.",
            "Afgemeld", "Je bent afgemeld voor:",
            "Voorkeuren bijgewerkt", "Afgemeld voor:", "Nog aangemeld voor:",
            "Succes", "Je voorkeuren zijn bijgewerkt."
        ),
        ["pl"] = new(
            "Link wygasł", "Ten link wygasł. Proszę użyć linku z nowszej wiadomości e-mail.",
            "Nieprawidłowy link", "Ten link jest nieprawidłowy. Proszę użyć linku z wiadomości e-mail.",
            "Już przetworzono", "Twoje preferencje zostały już zaktualizowane przy użyciu tego linku.",
            "Wypisano", "Wypisano z:",
            "Zaktualizowano preferencje", "Wypisano z:", "Nadal subskrybowany do:",
            "Sukces", "Twoje preferencje zostały zaktualizowane."
        ),
        ["es"] = new(
            "Enlace caducado", "Este enlace ha caducado. Por favor, utilice el enlace de un correo más reciente.",
            "Enlace inválido", "Este enlace no es válido. Por favor, utilice el enlace de su correo.",
            "Ya procesado", "Sus preferencias ya han sido actualizadas usando este enlace.",
            "Dado de baja", "Se ha dado de baja de:",
            "Preferencias actualizadas", "Dado de baja de:", "Suscrito todavía a:",
            "Éxito", "Sus preferencias han sido actualizadas."
        )
    };

    private static string GetPreferencePage(string token, string email, List<(string permission, bool optedIn)> permissions, string antiforgeryToken, string formFieldName, string language = "en")
    {
        var lang = language?.ToLowerInvariant() ?? "en";
        if (!Translations.ContainsKey(lang))
        {
            lang = "en";
        }
        var t = Translations[lang];

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

        return $$"""
            <!DOCTYPE html>
            <html lang="{{lang}}">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <title>{{WebUtility.HtmlEncode(t.Title)}}</title>
              <meta name="color-scheme" content="light dark" />
              <style>
                {{GetBaseStyles()}}

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

                .toggle input:checked { background: var(--fg); }
                .toggle input:checked::after { transform: translateX(18px); }
                .toggle input:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }

                button {
                  appearance: none;
                  border: none;
                  width: 100%;
                  padding: 14px 16px;
                  border-radius: 10px;
                  background: var(--fg);
                  color: var(--bg);
                  font-size: 1rem;
                  font-weight: 500;
                  cursor: pointer;
                }

                button.secondary {
                  margin-top: 12px;
                  background: transparent;
                  color: var(--fg);
                  border: 1px solid color-mix(in srgb, var(--fg) 25%, transparent);
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
                  <h1 class="select-none">{{WebUtility.HtmlEncode(t.Title)}}</h1>
                  <p>{{WebUtility.HtmlEncode(t.Description)}}</p>

                  <form method="post" action="/u/{{WebUtility.HtmlEncode(token)}}">
                    <input name="{{formFieldName}}" type="hidden" value="{{antiforgeryToken}}" />
                    <div class="preferences">
                      {{togglesHtml}}
                    </div>

                    <button type="submit" name="action" value="update">{{WebUtility.HtmlEncode(t.SaveButton)}}</button>
                    <button type="submit" name="action" value="unsubscribe_all" class="secondary">{{WebUtility.HtmlEncode(t.UnsubscribeButton)}}</button>
                  </form>

                  <footer class="select-none">
                    {{WebUtility.HtmlEncode(t.PreferencesFor)}}<br />
                    <span class="email">{{WebUtility.HtmlEncode(maskedEmail)}}</span>
                  </footer>
                </section>
              </main>
            </body>
            </html>
            """;
    }

    private static string GetStatusPage(string status, string language = "en", string[]? optedOut = null, string[]? keptIn = null)
    {
        var lang = language?.ToLowerInvariant() ?? "en";
        if (!StatusTranslations.ContainsKey(lang))
        {
            lang = "en";
        }
        var t = StatusTranslations[lang];

        var (icon, iconClass, title, message) = status switch
        {
            "expired" => ("⚠", "warning", t.ExpiredTitle, t.ExpiredMsg),
            "invalid" => ("✗", "error", t.InvalidTitle, t.InvalidMsg),
            "already_processed" => ("ℹ", "info", t.ProcessedTitle, t.ProcessedMsg),
            "unsubscribed" => ("✓", "success", t.UnsubTitle, $"{t.UnsubMsgPrefix} {FormatList(optedOut)}"),
            "updated" => ("✓", "success", t.UpdatedTitle, BuildUpdateMessage(t, optedOut, keptIn)),
            _ => ("✓", "success", t.SuccessTitle, t.SuccessMsg)
        };

        return $$"""
            <!DOCTYPE html>
            <html lang="{{lang}}">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <title>{{WebUtility.HtmlEncode(title)}}</title>
              <meta name="color-scheme" content="light dark" />
              <style>{{GetBaseStyles()}}</style>
            </head>
            <body>
              <main>
                <section class="card center">
                  <div class="icon {{iconClass}}">{{icon}}</div>
                  <h1>{{WebUtility.HtmlEncode(title)}}</h1>
                  <p>{{message}}</p>
                </section>
              </main>
            </body>
            </html>
            """;
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

    private static string BuildUpdateMessage(StatusStrings t, string[]? optedOut, string[]? keptIn)
    {
        var parts = new List<string>();
        if (optedOut?.Length > 0)
            parts.Add($"{t.UpdatedOptOutPrefix} {FormatList(optedOut)}");
        if (keptIn?.Length > 0)
            parts.Add($"{t.UpdatedOptInPrefix} {FormatList(keptIn)}");
        return string.Join("<br><br>", parts);
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