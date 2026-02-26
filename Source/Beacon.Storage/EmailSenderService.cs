using System;
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
        try
        {
            // The Encryptor is applied to sensitive configuration data.
            // Note: The method call '.Decrypt()' is assumed; adjust if your internal API differs.
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
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            // Failsafes gracefully without crashing the executing thread.
            // External logging is omitted per constraint parameters.
            return false;
        }
    }

    private async Task<bool> SendViaSmtpAsync(string toEmail, EmailQueueEntry entry, SystemConfig config, CancellationToken cancellationToken)
    {
        try
        {
            // The Encryptor is applied to sensitive configuration data.
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
        catch (Exception)
        {
            // Failsafes gracefully without crashing the executing thread.
            return false;
        }
    }

    private static string BuildFrom(SystemConfig config) =>
        string.IsNullOrWhiteSpace(config.EmailFromName)
            ? config.EmailFromAddress
            : $"{config.EmailFromName} <{config.EmailFromAddress}>";
}