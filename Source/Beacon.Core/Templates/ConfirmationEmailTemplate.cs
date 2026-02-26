using System.Net;

namespace Beacon.Core.Templates;

public static class ConfirmationEmailTemplate
{
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
            "Einstellungen für:"),
        ["fr"] = new("Plus qu'une étape pour finaliser ton inscription",
            "Nous avons reçu une demande pour t'inscrire à nos notifications par e-mail <strong>{permission}</strong>. Clique sur le bouton ci-dessous pour confirmer.",
            "Oui, je souhaite recevoir ces e-mails",
            "Tu ne t'es pas inscrit ? Tu peux simplement ignorer cet e-mail.",
            "Préférences pour :"),
        ["nl"] = new("Nog één stap om je aanmelding af te ronden",
            "We hebben een verzoek ontvangen om je aan te melden voor onze <strong>{permission}</strong> e-mailmeldingen. Klik op de knop hieronder om dit te bevestigen.",
            "Ja, ik wil deze e-mails ontvangen",
            "Heb jij je niet aangemeld? Dan kun je deze e-mail gewoon negeren.",
            "Bestemd voor:"),
        ["pl"] = new("Jeszcze tylko jeden krok, aby dokończyć rejestrację",
            "Otrzymaliśmy prośbę o zapisanie Cię na nasze powiadomienia e-mail <strong>{permission}</strong>. Kliknij poniższy przycisk, aby potwierdzić.",
            "Tak, chcę otrzymywać te wiadomości",
            "Nie zapisywałeś się? Możesz bezpiecznie zignorować tę wiadomość.",
            "Preferencje dla:"),
        ["es"] = new("Solo un paso más para completar tu suscripción",
            "Hemos recibido una solicitud para inscribirte en nuestras notificaciones por correo electrónico de <strong>{permission}</strong>. Haz clic en el botón de abajo para confirmar.",
            "Sí, quiero recibir estos correos",
            "¿No te has apuntado? Puedes ignorar este correo tranquilamente.",
            "Preferencias para:"),
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

    public static string Render(string bucket, string permission, string confirmationUrl, string language, string? email = null)
    {
        var lang = Translations.ContainsKey(language ?? "en") ? language!.ToLowerInvariant() : "en";
        var t = Translations[lang];

        var body = t.Body
            .Replace("{permission}", WebUtility.HtmlEncode(FormatPermission(permission)));

        var encodedUrl = WebUtility.HtmlEncode(confirmationUrl);
        var maskedEmail = string.IsNullOrEmpty(email) ? "" : MaskEmail(email);

        return $$"""
            <!DOCTYPE html>
            <html lang="{{lang}}">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <meta name="color-scheme" content="light dark" />
              <title>{{WebUtility.HtmlEncode(t.Heading)}}</title>
              <style>
                body { margin:0; padding:0; background-color:#f5f5f5; font-family:system-ui,-apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,Helvetica,Arial,sans-serif; }
                .wrapper { background-color:#f5f5f5; padding:40px 16px; }
                .card { background-color:#ffffff; border:1px solid #e0e0e0; border-radius:14px; padding:36px 32px; max-width:520px; margin:0 auto; }
                h1 { margin:0 0 12px; font-size:1.3rem; font-weight:600; color:#111111; letter-spacing:-0.01em; line-height:1.3; }
                p { margin:0 0 28px; color:#555555; font-size:0.9375rem; line-height:1.6; }
                .btn { display:block; background-color:#111111; color:#ffffff !important; text-decoration:none; text-align:center; padding:14px 24px; border-radius:10px; font-size:1rem; font-weight:500; }
                .footer { margin-top:24px; text-align:center; font-size:0.8125rem; color:#999999; line-height:1.5; }
                .pref-footer { margin-top:32px; padding-top:24px; border-top:1px solid #eeeeee; text-align:center; font-size:0.8125rem; color:#999999; line-height:1.5; }
                .email { font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace; color:#777777; }
                @media (prefers-color-scheme: dark) {
                  body, .wrapper { background-color:#0f0f0f !important; }
                  .card { background-color:#1a1a1a !important; border-color:#2e2e2e !important; }
                  h1 { color:#e7e7e7 !important; }
                  p { color:#aaaaaa !important; }
                  .btn { background-color:#e7e7e7 !important; color:#111111 !important; }
                  .footer { color:#666666 !important; }
                  .pref-footer { border-color:#2e2e2e !important; color:#666666 !important; }
                  .email { color:#888888 !important; }
                }
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
                            <h1>{{WebUtility.HtmlEncode(t.Heading)}}</h1>
                            <p>{{body}}</p>
                            <a href="{{encodedUrl}}" class="btn">{{WebUtility.HtmlEncode(t.Button)}}</a>
                            <p class="footer">{{WebUtility.HtmlEncode(t.Footer)}}</p>
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
