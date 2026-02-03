using System.Security.Cryptography;
using System.Text;

namespace Beacon.Core.Security;

public sealed class EmailHasher
{
    private readonly byte[] _pepper;

    public EmailHasher(string pepper)
    {
        _pepper = Encoding.UTF8.GetBytes(pepper);
    }

    public string Hash(string email)
    {
        var normalizedEmail = NormalizeEmail(email);
        var emailBytes = Encoding.UTF8.GetBytes(normalizedEmail);

        var combined = new byte[emailBytes.Length + _pepper.Length];
        Buffer.BlockCopy(emailBytes, 0, combined, 0, emailBytes.Length);
        Buffer.BlockCopy(_pepper, 0, combined, emailBytes.Length, _pepper.Length);

        var hash = SHA256.HashData(combined);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }
}
