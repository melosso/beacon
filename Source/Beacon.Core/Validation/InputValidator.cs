using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Beacon.Core.Validation;

public static partial class InputValidator
{

    public static ValidationResult ValidateEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return ValidationResult.Fail("Email is required");
        }

        if (email.Length > 254)
        {
            return ValidationResult.Fail("Email too long");
        }

        try
        {
            var addr = new MailAddress(email);
            if (addr.Address != email.Trim())
            {
                return ValidationResult.Fail("Invalid email format");
            }
        }
        catch
        {
            return ValidationResult.Fail("Invalid email format");
        }

        return ValidationResult.Ok();
    }

    public static ValidationResult ValidatePermission(string? permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            return ValidationResult.Fail("Permission is required");
        }

        if (permission.Length > 50)
        {
            return ValidationResult.Fail("Permission name too long");
        }

        if (!PermissionPattern().IsMatch(permission))
        {
            return ValidationResult.Fail("Permission contains invalid characters");
        }

        return ValidationResult.Ok();
    }

    public static ValidationResult ValidatePermissions(string[]? permissions)
    {
        if (permissions is null || permissions.Length == 0)
        {
            return ValidationResult.Fail("At least one permission is required");
        }

        if (permissions.Length > 10)
        {
            return ValidationResult.Fail("Too many permissions");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var permission in permissions)
        {
            var result = ValidatePermission(permission);
            if (!result.IsValid)
            {
                return result;
            }

            if (!seen.Add(permission))
            {
                return ValidationResult.Fail($"Duplicate permission: {permission}");
            }
        }

        return ValidationResult.Ok();
    }

    public static bool IsPermissionAllowed(string permission)
    {
        return ValidatePermission(permission).IsValid;
    }

    public static ValidationResult ValidateBucket(string? bucket)
    {
        if (string.IsNullOrWhiteSpace(bucket))
        {
            return ValidationResult.Fail("Bucket is required");
        }

        if (bucket.Length > 100)
        {
            return ValidationResult.Fail("Bucket name too long");
        }

        if (!BucketPattern().IsMatch(bucket))
        {
            return ValidationResult.Fail("Bucket contains invalid characters");
        }

        return ValidationResult.Ok();
    }

    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9_-]*$")]
    private static partial Regex BucketPattern();

    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9_-]*$")]
    private static partial Regex PermissionPattern();
}

public sealed class ValidationResult
{
    public bool IsValid { get; private init; }
    public string? Error { get; private init; }

    private ValidationResult() { }

    public static ValidationResult Ok() => new() { IsValid = true };
    public static ValidationResult Fail(string error) => new() { IsValid = false, Error = error };
}
