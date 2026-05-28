namespace Beacon.Localization;

public static class FormLocalization
{
    public static readonly string[] SupportedLanguages = ["en", "de", "fr", "nl", "pl", "es", "it", "pt", "ja"];

    public sealed record SubmissionStrings(
        string Subscribe,
        string Success,
        string ConsentText,
        string NamePlaceholder,
        string EmailPlaceholder,
        string PrivacyPolicy);

    private static readonly Dictionary<string, SubmissionStrings> SubmissionTable = new()
    {
        ["en"] = new(
            Subscribe:       "Subscribe",
            Success:         "Thanks for subscribing!",
            ConsentText:     "I agree to receive emails and understand I can unsubscribe at any time.",
            NamePlaceholder: "Your full name",
            EmailPlaceholder:"you@example.com",
            PrivacyPolicy:   "Privacy Policy"),

        ["de"] = new(
            Subscribe:       "Abonnieren",
            Success:         "Danke für deine Anmeldung!",
            ConsentText:     "Ich erkläre mich damit einverstanden, E-Mails zu erhalten, und weiß, dass ich mich jederzeit abmelden kann.",
            NamePlaceholder: "Ihr vollständiger Name",
            EmailPlaceholder:"ihre@email.de",
            PrivacyPolicy:   "Datenschutzerklärung"),

        ["fr"] = new(
            Subscribe:       "S'abonner",
            Success:         "Merci pour votre abonnement !",
            ConsentText:     "J'accepte de recevoir des e-mails et je sais que je peux me désabonner à tout moment.",
            NamePlaceholder: "Votre nom complet",
            EmailPlaceholder:"vous@email.fr",
            PrivacyPolicy:   "Politique de confidentialité"),

        ["nl"] = new(
            Subscribe:       "Inschrijven",
            Success:         "Bedankt voor uw inschrijving!",
            ConsentText:     "Ik ga akkoord met het ontvangen van e-mails en begrijp dat ik me te allen tijde kan afmelden.",
            NamePlaceholder: "Uw volledige naam",
            EmailPlaceholder:"u@email.nl",
            PrivacyPolicy:   "Privacybeleid"),

        ["pl"] = new(
            Subscribe:       "Subskrybuj",
            Success:         "Dziękujemy za subskrypcję!",
            ConsentText:     "Zgadzam się na otrzymywanie e-maili i rozumiem, że mogę się wypisać w dowolnym momencie.",
            NamePlaceholder: "Twoje imię i nazwisko",
            EmailPlaceholder:"ty@email.pl",
            PrivacyPolicy:   "Polityka prywatności"),

        ["es"] = new(
            Subscribe:       "Suscribirse",
            Success:         "¡Gracias por suscribirse!",
            ConsentText:     "Acepto recibir correos electrónicos y entiendo que puedo darme de baja en cualquier momento.",
            NamePlaceholder: "Tu nombre completo",
            EmailPlaceholder:"tu@email.es",
            PrivacyPolicy:   "Política de privacidad"),

        ["it"] = new(
            Subscribe:       "Iscriviti",
            Success:         "Grazie per l'iscrizione!",
            ConsentText:     "Accetto di ricevere e-mail e capisco che posso annullare l'iscrizione in qualsiasi momento.",
            NamePlaceholder: "Il tuo nome completo",
            EmailPlaceholder:"tu@email.it",
            PrivacyPolicy:   "Informativa sulla privacy"),

        ["pt"] = new(
            Subscribe:       "Inscrever-se",
            Success:         "Obrigado pela inscrição!",
            ConsentText:     "Concordo em receber e-mails e entendo que posso cancelar a inscrição a qualquer momento.",
            NamePlaceholder: "Seu nome completo",
            EmailPlaceholder:"voce@email.com.br",
            PrivacyPolicy:   "Política de privacidade"),

        ["ja"] = new(
            Subscribe:       "登録する",
            Success:         "登録ありがとうございます！",
            ConsentText:     "メールの受信に同意します。いつでも登録解除できます。",
            NamePlaceholder: "お名前（フルネーム）",
            EmailPlaceholder:"example@email.jp",
            PrivacyPolicy:   "プライバシーポリシー"),
    };

    public static SubmissionStrings GetSubmissionStrings(string? language)
    {
        var lang = Normalize(language);
        return SubmissionTable.TryGetValue(lang, out var t) ? t : SubmissionTable["en"];
    }

    public sealed record ConsentPageStrings(
        string Title,
        string Description,
        string SaveButton,
        string UnsubscribeButton,
        string PreferencesFor);

    private static readonly Dictionary<string, ConsentPageStrings> ConsentTable = new()
    {
        ["en"] = new(
            Title:               "Email preferences",
            Description:         "You're receiving these emails because you previously opted in. You can change that here.",
            SaveButton:          "Save preferences",
            UnsubscribeButton:   "Unsubscribe from all",
            PreferencesFor:      "Preferences for:"),

        ["de"] = new(
            Title:               "E-Mail-Einstellungen",
            Description:         "Sie erhalten diese E-Mails, weil Sie sich zuvor angemeldet haben. Hier können Sie das ändern.",
            SaveButton:          "Einstellungen speichern",
            UnsubscribeButton:   "Von allem abmelden",
            PreferencesFor:      "Einstellungen für:"),

        ["fr"] = new(
            Title:               "Préférences e-mail",
            Description:         "Vous recevez ces e-mails parce que vous vous êtes inscrit précédemment. Vous pouvez modifier cela ici.",
            SaveButton:          "Enregistrer les préférences",
            UnsubscribeButton:   "Se désabonner de tout",
            PreferencesFor:      "Préférences pour :"),

        ["nl"] = new(
            Title:               "E-mailvoorkeuren",
            Description:         "Je krijgt deze e-mails omdat je je eerder hebt aangemeld. Je kunt hier je voorkeuren aanpassen.",
            SaveButton:          "Voorkeuren opslaan",
            UnsubscribeButton:   "Alles afmelden",
            PreferencesFor:      "Voorkeuren voor:"),

        ["pl"] = new(
            Title:               "Preferencje e-mail",
            Description:         "Otrzymujesz te e-maile, ponieważ wcześniej wyraziłeś zgodę. Możesz to zmienić tutaj.",
            SaveButton:          "Zapisz preferencje",
            UnsubscribeButton:   "Wypisz się ze wszystkiego",
            PreferencesFor:      "Preferencje dla:"),

        ["es"] = new(
            Title:               "Preferencias de correo",
            Description:         "Recibe estos correos porque se suscribió anteriormente. Puede cambiar eso aquí.",
            SaveButton:          "Guardar preferencias",
            UnsubscribeButton:   "Cancelar todas las suscripciones",
            PreferencesFor:      "Preferencias para:"),

        ["it"] = new(
            Title:               "Preferenze email",
            Description:         "Ricevi queste email perché ti sei iscritto in precedenza. Puoi modificarlo qui.",
            SaveButton:          "Salva preferenze",
            UnsubscribeButton:   "Annulla iscrizione a tutto",
            PreferencesFor:      "Preferenze per:"),

        ["pt"] = new(
            Title:               "Preferências de email",
            Description:         "Você recebe esses e-mails porque se inscreveu anteriormente. Você pode alterar isso aqui.",
            SaveButton:          "Salvar preferências",
            UnsubscribeButton:   "Cancelar inscrição de tudo",
            PreferencesFor:      "Preferências para:"),

        ["ja"] = new(
            Title:               "メール設定",
            Description:         "以前にオプトインされたためメールをお送りしています。こちらで変更できます。",
            SaveButton:          "設定を保存",
            UnsubscribeButton:   "すべて配信停止",
            PreferencesFor:      "設定対象:"),
    };

    public static ConsentPageStrings GetConsentPageStrings(string? language)
    {
        var lang = Normalize(language);
        return ConsentTable.TryGetValue(lang, out var t) ? t : ConsentTable["en"];
    }

    public sealed record StatusStrings(
        string ExpiredTitle,   string ExpiredMsg,
        string InvalidTitle,   string InvalidMsg,
        string ProcessedTitle, string ProcessedMsg,
        string UnsubTitle,     string UnsubMsgPrefix,
        string UpdatedTitle,   string UpdatedOptOutPrefix, string UpdatedOptInPrefix,
        string SuccessTitle,   string SuccessMsg,
        string ConfirmedTitle, string ConfirmedMsg);

    private static readonly Dictionary<string, StatusStrings> StatusTable = new()
    {
        ["en"] = new(
            "Link expired",           "This link has expired. Please use the link in a more recent email.",
            "Invalid link",           "This link is invalid. Please use the link from your email.",
            "Already processed",      "Your preferences have already been updated using this link.",
            "Unsubscribed",           "You have been unsubscribed from:",
            "Preferences updated",    "Unsubscribed from:", "Still subscribed to:",
            "Success",                "Your preferences have been updated.",
            "Subscription confirmed", "Your subscription has been confirmed. You're now opted in."),

        ["de"] = new(
            "Link abgelaufen",         "Dieser Link ist abgelaufen. Bitte verwenden Sie den Link in einer aktuelleren E-Mail.",
            "Ungültiger Link",         "Dieser Link ist ungültig. Bitte verwenden Sie den Link aus Ihrer E-Mail.",
            "Bereits bearbeitet",      "Ihre Einstellungen wurden bereits über diesen Link aktualisiert.",
            "Abgemeldet",              "Sie wurden abgemeldet von:",
            "Einstellungen aktualisiert", "Abgemeldet von:", "Noch angemeldet für:",
            "Erfolg",                  "Ihre Einstellungen wurden aktualisiert.",
            "Anmeldung bestätigt",     "Ihre Anmeldung wurde bestätigt. Sie sind jetzt eingetragen."),

        ["fr"] = new(
            "Lien expiré",             "Ce lien a expiré. Veuillez utiliser le lien contenu dans un e-mail plus récent.",
            "Lien invalide",           "Ce lien est invalide. Veuillez utiliser le lien contenu dans votre e-mail.",
            "Déjà traité",             "Vos préférences ont déjà été mises à jour via ce lien.",
            "Désabonné",               "Vous avez été désabonné de :",
            "Préférences mises à jour","Désabonné de :", "Toujours abonné à :",
            "Succès",                  "Vos préférences ont été mises à jour.",
            "Abonnement confirmé",     "Votre abonnement a été confirmé. Vous êtes maintenant inscrit."),

        ["nl"] = new(
            "Link verlopen",           "Deze link is verlopen. Gebruik de link in een recentere e-mail.",
            "Ongeldige link",          "Deze link is ongeldig. Gebruik de link uit je e-mail.",
            "Reeds verwerkt",          "Je voorkeuren zijn al bijgewerkt via deze link.",
            "Afgemeld",                "Je bent afgemeld voor:",
            "Voorkeuren bijgewerkt",   "Afgemeld voor:", "Nog aangemeld voor:",
            "Succes",                  "Je voorkeuren zijn bijgewerkt.",
            "Inschrijving bevestigd",  "Je inschrijving is bevestigd. Je bent nu aangemeld."),

        ["pl"] = new(
            "Link wygasł",             "Ten link wygasł. Proszę użyć linku z nowszej wiadomości e-mail.",
            "Nieprawidłowy link",      "Ten link jest nieprawidłowy. Proszę użyć linku z wiadomości e-mail.",
            "Już przetworzono",        "Twoje preferencje zostały już zaktualizowane przy użyciu tego linku.",
            "Wypisano",                "Wypisano z:",
            "Zaktualizowano preferencje","Wypisano z:", "Nadal subskrybowany do:",
            "Sukces",                  "Twoje preferencje zostały zaktualizowane.",
            "Subskrypcja potwierdzona","Twoja subskrypcja została potwierdzona. Jesteś teraz zapisany."),

        ["es"] = new(
            "Enlace caducado",         "Este enlace ha caducado. Por favor, utilice el enlace de un correo más reciente.",
            "Enlace inválido",         "Este enlace no es válido. Por favor, utilice el enlace de su correo.",
            "Ya procesado",            "Sus preferencias ya han sido actualizadas usando este enlace.",
            "Dado de baja",            "Se ha dado de baja de:",
            "Preferencias actualizadas","Dado de baja de:", "Suscrito todavía a:",
            "Éxito",                   "Sus preferencias han sido actualizadas.",
            "Suscripción confirmada",  "Su suscripción ha sido confirmada. Ya está registrado."),

        ["it"] = new(
            "Link scaduto",            "Questo link è scaduto. Utilizza il link di un'e-mail più recente.",
            "Link non valido",         "Questo link non è valido. Utilizza il link contenuto nella tua e-mail.",
            "Già elaborato",           "Le tue preferenze sono già state aggiornate tramite questo link.",
            "Disiscritto",             "Hai annullato l'iscrizione a:",
            "Preferenze aggiornate",   "Disiscritto da:", "Ancora iscritto a:",
            "Successo",                "Le tue preferenze sono state aggiornate.",
            "Iscrizione confermata",   "La tua iscrizione è stata confermata. Sei ora registrato."),

        ["pt"] = new(
            "Link expirado",           "Este link expirou. Por favor, use o link de um e-mail mais recente.",
            "Link inválido",           "Este link é inválido. Por favor, use o link do seu e-mail.",
            "Já processado",           "Suas preferências já foram atualizadas usando este link.",
            "Desinscrito",             "Você foi desinscrito de:",
            "Preferências atualizadas","Desinscrito de:", "Ainda inscrito em:",
            "Sucesso",                 "Suas preferências foram atualizadas.",
            "Assinatura confirmada",   "Sua assinatura foi confirmada. Agora você está inscrito."),

        ["ja"] = new(
            "リンクの期限切れ",          "このリンクは有効期限が切れています。最新のメールのリンクをご利用ください。",
            "無効なリンク",             "このリンクは無効です。メールに記載されたリンクをご利用ください。",
            "処理済み",                 "このリンクを使用して設定はすでに更新されています。",
            "配信停止完了",             "以下の配信を停止しました：",
            "設定を更新しました",        "配信停止：", "引き続きご登録中：",
            "成功",                     "設定が更新されました。",
            "登録確認完了",             "ご登録が確認されました。オプトインが完了しました。"),
    };

    public static StatusStrings GetStatusStrings(string? language)
    {
        var lang = Normalize(language);
        return StatusTable.TryGetValue(lang, out var t) ? t : StatusTable["en"];
    }

    public static string GetUnavailableMessage(string? language) => Normalize(language) switch
    {
        "de" => "Dieses Formular ist derzeit nicht verfügbar.",
        "fr" => "Ce formulaire est actuellement indisponible.",
        "es" => "Este formulario no está disponible actualmente.",
        "nl" => "Dit formulier is momenteel niet beschikbaar.",
        "pl" => "Ten formularz jest obecnie niedostępny.",
        "it" => "Questo modulo non è attualmente disponibile.",
        "pt" => "Este formulário não está disponível no momento.",
        "ja" => "このフォームは現在利用できません。",
        _    => "This form is currently unavailable.",
    };

    private static string Normalize(string? language) =>
        (language ?? "en").ToLowerInvariant();
}
