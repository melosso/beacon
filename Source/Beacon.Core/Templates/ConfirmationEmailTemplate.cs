using System.Net;
using Beacon.Core.Models;

namespace Beacon.Core.Templates;

public static class ConfirmationEmailTemplate
{
    private static readonly HashSet<string> EmailSafeFonts = new(StringComparer.OrdinalIgnoreCase)
    {
        "georgia", "times new roman", "arial", "helvetica", "tahoma",
        "verdana", "trebuchet ms", "courier new"
    };

    private sealed record Strings(
        string Heading, string Body, string Button, string Footer, string PreferencesFor);

    private static readonly Dictionary<string, Strings> Translations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new("One more step to complete your sign-up",
            "We received a request to sign you up for our <strong>{permission}</strong> email notifications. Click the button below to confirm.",
            "Yes, I want to receive these emails",
            "Didn't sign up? You can safely ignore this email.",
            "Sent to:"),
        ["de"] = new("Nur noch een Schritt, um deine Anmeldung abzuschließen",
            "Wir haben eine Anfrage erhalten, dich für unsere <strong>{permission}</strong>-E-Mail-Benachrichtigungen anzumelden. Klicke auf die Schaltfläche unten, um dies zu bestätigen.",
            "Ja, ich möchte diese E-Mails erhalten",
            "Hast du dich nicht angemeldet? Dann kannst du diese E-Mail einfach ignorieren.",
            "Sendet an:"),
        ["fr"] = new("Plus qu'une étape pour finaliser ton inscription",
            "Nous avons reçu une demande pour t'inscrire à nos notifications par e-mail <strong>{permission}</strong>. Clique sur le bouton ci-dessous pour confirmer.",
            "Oui, je souhaite recevoir ces e-mails",
            "Tu ne t'es pas inscrit ? Tu peux simplement ignorer cet e-mail.",
            "Envoyé à :"),
        ["nl"] = new("Nog één stap om je aanmelding af te ronden",
            "We hebben een verzoek ontvangen om je aan te melden voor onze <strong>{permission}</strong> e-mailmeldingen. Klik op de knop hieronder om dit te bevestigen.",
            "Ja, ik wil deze e-mails ontvangen",
            "Heb jij je niet aangemeld? Dan kun je deze e-mail gewoon negeren.",
            "Bestemd voor:"),
        ["pl"] = new("Jeszcze tylko jeden krok, aby dokończyć rejestrację",
            "Otrzymaliśmy prośbę o zapisanie Cię na nasze powiadomienia e-mail <strong>{permission}</strong>. Kliknij poniższy przycisk, aby potwierdzić.",
            "Tak, chcę otrzymywać te wiadomości",
            "Nie zapisywałeś się? Możesz bezpiecznie zignorować tę wiadomość.",
            "Wysłano do:"),
        ["es"] = new("Solo un paso más para completar tu suscripción",
            "Hemos recibido una solicitud para inscribirte en nuestras notificaciones por correo electrónico de <strong>{permission}</strong>. Haz clic en el botón de abajo para confirmar.",
            "Sí, quiero recibir estos correos",
            "¿No te has apuntado? Puedes ignorar este correo tranquilamente.",
            "Enviado a:"),
    };

    public static string GetSubject(string language) => language?.ToLowerInvariant() switch
    {
        "de" => "Anmeldung bestätigen",
        "fr" => "Confirme ton inscription",
        "nl" => "Nog één stap om je aanmelding af te ronden",
        "pl" => "Potwierdź swoją rejestrację",
        "es" => "Confirma tu suscripción",
        _    => "Confirm your sign-up"
    };

    public static string Render(
        string bucket,
        string permission,
        string confirmationUrl,
        string language,
        string? email = null,
        BrandIdentitySettings? brand = null)
    {
        var lang = Translations.ContainsKey(language ?? "en") ? language!.ToLowerInvariant() : "en";
        var t = Translations[lang];

        var body = (brand?.EmailBody is { Length: > 0 } customBody ? customBody : t.Body)
            .Replace("{permission}", WebUtility.HtmlEncode(FormatPermission(permission)));

        var heading = brand?.EmailTitle is { Length: > 0 } customTitle ? customTitle : t.Heading;
        var footer = brand?.Footer is { Length: > 0 } customFooter ? customFooter : t.Footer;

        var encodedUrl = WebUtility.HtmlEncode(confirmationUrl);
        var maskedEmail = string.IsNullOrEmpty(email) ? "" : MaskEmail(email);

        var theme = brand?.Theme ?? "system";
        var accent = brand?.PrimaryAccent is { Length: > 0 } a ? a : null;
        var surface = brand?.SurfaceColour is { Length: > 0 } s ? s : null;

        var fontStack = BuildEmailFontStack(brand?.Font);
        var logoHtml = BuildLogoHtml(brand?.Logo);

        var wrapperBg = surface ?? "#f5f5f5";
        var cardBg = surface is not null ? AdjustCardBg(surface) : "#ffffff";
        var btnBg = accent ?? "#111111";
        var btnColor = accent is not null ? "#ffffff" : "#ffffff";

        var darkStyles = theme == "light" ? "" : $$"""
                @media (prefers-color-scheme: dark) {
                  body, .wrapper { background-color:#0f0f0f !important; }
                  .card { background-color:#1a1a1a !important; border-color:#2e2e2e !important; }
                  h1 { color:#e7e7e7 !important; }
                  p { color:#aaaaaa !important; }
                  .btn { background-color:{{(accent ?? "#e7e7e7")}} !important; color:{{(accent is not null ? "#ffffff" : "#111111")}} !important; }
                  .footer { color:#666666 !important; }
                  .pref-footer { border-color:#2e2e2e !important; color:#666666 !important; }
                  .email { color:#888888 !important; }
                }
            """;

        string bodyBg, bodyCard, bodyH1, bodyP, bodyBtn, bodyBtnColor;
        if (theme == "dark")
        {
            bodyBg = surface ?? "#0f0f0f";
            bodyCard = "#1a1a1a";
            bodyH1 = "#e7e7e7";
            bodyP = "#aaaaaa";
            bodyBtn = accent ?? "#e7e7e7";
            bodyBtnColor = accent is not null ? "#ffffff" : "#111111";
            darkStyles = "";
        }
        else
        {
            bodyBg = wrapperBg;
            bodyCard = cardBg;
            bodyH1 = "#111111";
            bodyP = "#555555";
            bodyBtn = btnBg;
            bodyBtnColor = btnColor;
        }

        var colorScheme = theme == "light" ? "light" : theme == "dark" ? "dark" : "light dark";

        return $$"""
            <!DOCTYPE html>
            <html lang="{{lang}}">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <meta name="color-scheme" content="{{colorScheme}}" />
              <title>{{WebUtility.HtmlEncode(heading)}}</title>
              <style>
                body { margin:0; padding:0; background-color:{{bodyBg}}; font-family:{{fontStack}}; }
                .wrapper { background-color:{{bodyBg}}; padding:40px 16px; }
                .card { background-color:{{bodyCard}}; border:1px solid #e0e0e0; border-radius:14px; padding:36px 32px; max-width:520px; margin:0 auto; }
                .logo { display:block; max-width:160px; max-height:60px; margin:0 0 20px; }
                h1 { margin:0 0 12px; font-size:1.3rem; font-weight:600; color:{{bodyH1}}; letter-spacing:-0.01em; line-height:1.3; }
                p { margin:0 0 28px; color:{{bodyP}}; font-size:0.9375rem; line-height:1.6; }
                .btn { display:block; background-color:{{bodyBtn}}; color:{{bodyBtnColor}} !important; text-decoration:none; text-align:center; padding:14px 24px; border-radius:10px; font-size:1rem; font-weight:500; }
                .footer { margin-top:24px; text-align:center; font-size:0.8125rem; color:#999999; line-height:1.5; }
                .pref-footer { margin-top:32px; padding-top:24px; border-top:1px solid #eeeeee; text-align:center; font-size:0.8125rem; color:#999999; line-height:1.5; }
                .email { font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace; color:#777777; }
                {{darkStyles}}
              </style>
            </head>
            <body>
              <div class="wrapper">
                <table width="100%" cellpadding="0" cellspacing="0" role="presentation">
                  <tr>
                    <td align="center">
                      <table class="card" width="520" cellpadding="0" cellspacing="0" role="presentation" style="max-width:520px;width:100%">
                        <tr>
                          <td style="padding:36px 32px">
                            {{logoHtml}}
                            <h1>{{WebUtility.HtmlEncode(heading)}}</h1>
                            <p>{{body}}</p>
                            <a href="{{encodedUrl}}" class="btn">{{WebUtility.HtmlEncode(t.Button)}}</a>
                            <p class="footer">{{WebUtility.HtmlEncode(footer)}}</p>
                            {{(string.IsNullOrEmpty(maskedEmail) ? "" : $"""
                            <div class="pref-footer">
                              {WebUtility.HtmlEncode(t.PreferencesFor)}<br />
                              <span class="email">{WebUtility.HtmlEncode(maskedEmail)}</span>
                            </div>
                            """)}}
                          </td>
                        </tr>
                      </table>
                    </td>
                  </tr>
                </table>
              </div>
            </body>
            </html>
            """;
    }

    private static string BuildEmailFontStack(string? font)
    {
        if (string.IsNullOrEmpty(font)) return "system-ui,-apple-system,BlinkMacSystemFont,\"Segoe UI\",Roboto,Helvetica,Arial,sans-serif";
        var lower = font.ToLowerInvariant();
        if (EmailSafeFonts.Contains(lower))
        {
            var quoted = font.Contains(' ') ? $"\"{font}\"" : font;
            return $"{quoted},Helvetica,Arial,sans-serif";
        }
        // Non-email-safe: fall back to system stack silently (warning shown in UI)
        return "system-ui,-apple-system,BlinkMacSystemFont,\"Segoe UI\",Roboto,Helvetica,Arial,sans-serif";
    }

    private static string BuildLogoHtml(AssetObject? logo)
    {
        if (logo is null) return "";
        var src = logo.Type switch
        {
            "base64" => logo.Data,
            "url" or "objectStorage" => logo.Url,
            _ => null
        };
        if (string.IsNullOrEmpty(src)) return "";
        return $"<img src=\"{WebUtility.HtmlEncode(src)}\" class=\"logo\" alt=\"Logo\" />";
    }

    private static string AdjustCardBg(string surfaceHex)
    {
        // If surface is dark-ish keep card slightly lighter; if light keep it white-ish
        // Simple heuristic: if surface looks dark (starts with 0/1 in hex) use a lighter variant
        return "#ffffff";
    }

    private static string FormatPermission(string permission)
    {
        if (string.IsNullOrEmpty(permission)) return string.Empty;
        var parts = permission.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        
        var formattedParts = parts.Select(p => string.Join(" ", p
            .Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpper(w[0]) + (w.Length > 1 ? w[1..] : string.Empty))));

        var list = formattedParts.ToList();
        if (list.Count <= 1) return list.FirstOrDefault() ?? string.Empty;
        if (list.Count == 2) return $"{list[0]} & {list[1]}";
        return $"{string.Join(", ", list.Take(list.Count - 1))} & {list.Last()}";
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
}
