using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Beacon.Core.Templates;

namespace Beacon.Storage;

public sealed class EmailSenderService : IEmailSenderService
{
    // A single, static instance prevents socket exhaustion under concurrent load.
    private static readonly HttpClient _httpClient = new HttpClient();

    private readonly Encryptor _encryptor;

    public EmailSenderService(Encryptor encryptor)
    {
        _encryptor = encryptor;
    }

    public Task<bool> SendConfirmationAsync(string toEmail, EmailQueueEntry entry, SystemConfig config, CancellationToken cancellationToken = default)
    {
        return config.EmailProvider.ToLowerInvariant() switch
        {
            "resend" => SendViaResendAsync(toEmail, entry, config, cancellationToken),
            "smtp"   => SendViaSmtpAsync(toEmail, entry, config, cancellationToken),
            _        => Task.FromResult(false)
        };
    }

    private async Task<bool> SendViaResendAsync(string toEmail, EmailQueueEntry entry, SystemConfig config, CancellationToken cancellationToken)
    {
        var apiKey = _encryptor.Decrypt(config.EmailResendApiKey);

        var payload = JsonSerializer.Serialize(new
        {
            from    = BuildFrom(config),
            to      = new[] { toEmail },
            subject = ConfirmationEmailTemplate.GetSubject(entry.Language),
            html    = ConfirmationEmailTemplate.Render(entry.Bucket, entry.Permission, entry.ConfirmationUrl, entry.Language)
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Resend API returned {(int)response.StatusCode} {response.StatusCode}: {body}",
                null,
                response.StatusCode);
        }

        return true;
    }

    private async Task<bool> SendViaSmtpAsync(string toEmail, EmailQueueEntry entry, SystemConfig config, CancellationToken cancellationToken)
    {
        var password = _encryptor.Decrypt(config.EmailSmtpPassword);

        using var client = new SmtpClient(config.EmailSmtpHost, config.EmailSmtpPort)
        {
            EnableSsl   = config.EmailSmtpUseTls,
            Credentials = new NetworkCredential(config.EmailSmtpUsername, password)
        };

        var from = string.IsNullOrWhiteSpace(config.EmailFromName)
            ? new MailAddress(config.EmailFromAddress)
            : new MailAddress(config.EmailFromAddress, config.EmailFromName);

        using var message = new MailMessage
        {
            From       = from,
            Subject    = ConfirmationEmailTemplate.GetSubject(entry.Language),
            Body       = ConfirmationEmailTemplate.Render(entry.Bucket, entry.Permission, entry.ConfirmationUrl, entry.Language),
            IsBodyHtml = true
        };
        message.To.Add(toEmail);

        await client.SendMailAsync(message, cancellationToken);
        return true;
    }

    private static string BuildFrom(SystemConfig config) =>
        string.IsNullOrWhiteSpace(config.EmailFromName)
            ? config.EmailFromAddress
            : $"{config.EmailFromName} <{config.EmailFromAddress}>";
}
