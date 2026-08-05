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
using Microsoft.Extensions.Logging;

namespace Beacon.Storage;

public sealed class EmailSenderService {
    // A single, static instance prevents socket exhaustion under concurrent load.
    private static readonly HttpClient _httpClient = new HttpClient();

    private readonly Encryptor _encryptor;
    private readonly ILogger<EmailSenderService> _logger;
    private readonly BrandIdentityService _brandService;

    public EmailSenderService(Encryptor encryptor, ILogger<EmailSenderService> logger, BrandIdentityService brandService)
    {
        _encryptor = encryptor;
        _logger = logger;
        _brandService = brandService;
    }

    public async Task<bool> SendConfirmationAsync(string toEmail, EmailQueueEntry entry, SystemConfig config, CancellationToken cancellationToken = default)
    {
        var provider = config.EmailProvider?.Trim().ToLowerInvariant() ?? string.Empty;
        _logger.LogInformation("Email queue: sending confirmation email via {Provider} to {Email} (queue={Id})", provider, toEmail[..3] + "...", entry.Id);

        try
        {
            var result = await (provider switch
            {
                "resend" => SendViaResendAsync(toEmail, entry, config, cancellationToken),
                "smtp"   => SendViaSmtpAsync(toEmail, entry, config, cancellationToken),
                _        => Task.FromResult(false)
            });

            if (result)
            {
                _logger.LogDebug("Email queue: {Provider} accepted email for delivery (queue={Id})", provider, entry.Id);
            }
            else
            {
                _logger.LogDebug("Email queue: {Provider} returned false without error (queue={Id})", provider, entry.Id);
            }

            return result;
        }
        catch (System.Net.Mail.SmtpException ex)
        {
            _logger.LogError("Email queue: SMTP error sending confirmation email via {Provider} (queue={Id}): {Message}", provider, entry.Id, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email queue: unexpected error sending confirmation email via {Provider} (queue={Id})", provider, entry.Id);
            throw;
        }
    }

    private async Task<BrandIdentitySettings?> GetBrandSettingsAsync(string bucket, CancellationToken ct)
    {
        var identity = await _brandService.GetForBucketAsync(bucket, ct);
        if (string.IsNullOrEmpty(identity.Settings) || identity.Settings == "{}") return null;
        try { return JsonSerializer.Deserialize<BrandIdentitySettings>(identity.Settings); }
        catch { return null; }
    }

    private async Task<bool> SendViaResendAsync(string toEmail, EmailQueueEntry entry, SystemConfig config, CancellationToken cancellationToken)
    {
        var apiKey = _encryptor.Decrypt(config.EmailResendApiKey);
        var brand = await GetBrandSettingsAsync(entry.Bucket, cancellationToken);

        var payload = JsonSerializer.Serialize(new
        {
            from    = BuildFrom(config),
            to      = new[] { toEmail },
            subject = ConfirmationEmailTemplate.GetSubject(entry.Language),
            html    = ConfirmationEmailTemplate.Render(entry.Bucket, entry.Permission, entry.ConfirmationUrl, entry.Language, toEmail, brand)
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        try
        {
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
        catch (HttpRequestException ex)
        {
            _logger.LogError("Email queue: Resend API error (queue={Id}): {Message}", entry.Id, ex.Message);
            throw;
        }
    }

    private async Task<bool> SendViaSmtpAsync(string toEmail, EmailQueueEntry entry, SystemConfig config, CancellationToken cancellationToken)
    {
        var password = _encryptor.Decrypt(config.EmailSmtpPassword);
        var brand = await GetBrandSettingsAsync(entry.Bucket, cancellationToken);

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
            Body       = ConfirmationEmailTemplate.Render(entry.Bucket, entry.Permission, entry.ConfirmationUrl, entry.Language, toEmail, brand),
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
