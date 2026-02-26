using Beacon.Core.Models;

namespace Beacon.Core.Services;

public interface IEmailSenderService
{
    /// <param name="toEmail">Decrypted recipient email address.</param>
    Task<bool> SendConfirmationAsync(string toEmail, EmailQueueEntry entry, SystemConfig config, CancellationToken cancellationToken = default);
}
