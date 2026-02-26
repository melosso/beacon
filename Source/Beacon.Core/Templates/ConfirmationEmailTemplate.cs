using System.Net;

namespace Beacon.Core.Templates;

public static class ConfirmationEmailTemplate
{
    private sealed record Strings(
        string Heading, string Body, string Button, string Footer);

    private static readonly Dictionary<string, Strings> Translations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new("Confirm your subscription",
            "You (or someone on your behalf) subscribed to <strong>{permission}</strong> communications for <strong>{bucket}</strong>. Click the button below to confirm your subscription.",
            "Confirm subscription",
            "If you did not request this, you can safely ignore this email."),
        ["de"] = new("Abonnement bestätigen",
            "Sie (oder jemand in Ihrem Namen) haben sich für <strong>{permission}</strong>-Mitteilungen von <strong>{bucket}</strong> angemeldet. Klicken Sie auf die Schaltfläche, um zu bestätigen.",
            "Abonnement bestätigen",
            "Falls Sie diese Anfrage nicht gestellt haben, können Sie diese E-Mail ignorieren."),
        ["fr"] = new("Confirmez votre abonnement",
            "Vous (ou quelqu'un en votre nom) vous êtes abonné aux communications <strong>{permission}</strong> pour <strong>{bucket}</strong>. Cliquez sur le bouton ci-dessous pour confirmer.",
            "Confirmer l'abonnement",
            "Si vous n'avez pas demandé cela, vous pouvez ignorer cet e-mail."),
        ["nl"] = new("Bevestig uw abonnement",
            "U (of iemand namens u) heeft zich aangemeld voor <strong>{permission}</strong>-communicatie van <strong>{bucket}</strong>. Klik op de knop hieronder om te bevestigen.",
            "Abonnement bevestigen",
            "Als u dit niet heeft aangevraagd, kunt u deze e-mail veilig negeren."),
        ["pl"] = new("Potwierdź zapis",
            "Ty (lub ktoś w Twoim imieniu) zapisałeś się na komunikację <strong>{permission}</strong> dla <strong>{bucket}</strong>. Kliknij przycisk poniżej, aby potwierdzić.",
            "Potwierdź zapis",
            "Jeśli nie prosiłeś o to, możesz zignorować tę wiadomość."),
        ["es"] = new("Confirma tu suscripción",
            "Tú (o alguien en tu nombre) te has suscrito a las comunicaciones de <strong>{permission}</strong> para <strong>{bucket}</strong>. Haz clic en el botón de abajo para confirmar.",
            "Confirmar suscripción",
            "Si no solicitaste esto, puedes ignorar este correo."),
    };

    public static string GetSubject(string language) => language?.ToLowerInvariant() switch
    {
        "de" => "Bitte bestätigen Sie Ihr Abonnement",
        "fr" => "Veuillez confirmer votre abonnement",
        "nl" => "Bevestig uw abonnement",
        "pl" => "Potwierdź swój zapis",
        "es" => "Confirma tu suscripción",
        _    => "Please confirm your subscription"
    };

    public static string Render(string bucket, string permission, string confirmationUrl, string language)
    {
        var lang = Translations.ContainsKey(language ?? "en") ? language!.ToLowerInvariant() : "en";
        var t = Translations[lang];

        var body = t.Body
            .Replace("{permission}", WebUtility.HtmlEncode(FormatPermission(permission)))
            .Replace("{bucket}", WebUtility.HtmlEncode(bucket));

        var encodedUrl = WebUtility.HtmlEncode(confirmationUrl);

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
                @media (prefers-color-scheme: dark) {
                  body, .wrapper { background-color:#0f0f0f !important; }
                  .card { background-color:#1a1a1a !important; border-color:#2e2e2e !important; }
                  h1 { color:#e7e7e7 !important; }
                  p { color:#aaaaaa !important; }
                  .btn { background-color:#e7e7e7 !important; color:#111111 !important; }
                  .footer { color:#666666 !important; }
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
        return string.Join(" ", permission
            .Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpper(w[0]) + (w.Length > 1 ? w[1..] : string.Empty)));
    }
}
