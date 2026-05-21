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

    public static ValidationResult ValidateOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return ValidationResult.Fail("Origin is required");
        }

        if (!Uri.TryCreate(origin.Trim(), UriKind.Absolute, out var uri))
        {
            return ValidationResult.Fail("Origin must be a valid absolute URL");
        }

        if (uri.Scheme != "http" && uri.Scheme != "https")
        {
            return ValidationResult.Fail("Origin must use http or https");
        }

        if (uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return ValidationResult.Fail("Origin must not contain a path, query, or fragment");
        }

        return ValidationResult.Ok();
    }

    public static ValidationResult ValidateSubmissionName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ValidationResult.Fail("Name is required");
        }

        if (name.Trim().Length > 200)
        {
            return ValidationResult.Fail("Name is too long (max 200 characters)");
        }

        return ValidationResult.Ok();
    }

    public static ValidationResult ValidatePrivacyPolicyUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return ValidationResult.Ok();

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return ValidationResult.Fail("Privacy policy URL must be a valid absolute URL");

        if (uri.Scheme != "https")
            return ValidationResult.Fail("Privacy policy URL must use HTTPS");

        return ValidationResult.Ok();
    }

    public static ValidationResult ValidateConsentText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ValidationResult.Ok();

        if (text.Trim().Length > 500)
            return ValidationResult.Fail("Consent text is too long (max 500 characters)");

        return ValidationResult.Ok();
    }

    private static readonly HashSet<string> AllowedCssColorNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "transparent", "currentcolor",
        "black", "white", "red", "green", "blue", "yellow", "orange", "purple",
        "pink", "gray", "grey", "brown", "cyan", "magenta", "lime", "navy",
        "teal", "aqua", "maroon", "olive", "silver", "fuchsia",
        "indigo", "violet", "coral", "salmon", "tomato", "gold",
        "crimson", "darkblue", "darkgreen", "darkred", "lightblue", "lightgreen",
        "lightgray", "lightgrey", "darkgray", "darkgrey", "whitesmoke", "aliceblue",
        "inherit", "initial", "unset"
    };

    public static ValidationResult ValidateCssColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ValidationResult.Ok();

        var trimmed = value.Trim();

        // Hex colors: #rgb, #rgba, #rrggbb, #rrggbbaa
        if (HexColorPattern().IsMatch(trimmed))
            return ValidationResult.Ok();

        // Named colors
        if (AllowedCssColorNames.Contains(trimmed))
            return ValidationResult.Ok();

        // rgb() / rgba() with only digits, commas, spaces, dots, percent
        if (RgbFunctionPattern().IsMatch(trimmed))
            return ValidationResult.Ok();

        return ValidationResult.Fail("Invalid CSS color value");
    }

    public static ValidationResult ValidateCssBorderRadius(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ValidationResult.Ok();

        var trimmed = value.Trim();

        if (BorderRadiusPattern().IsMatch(trimmed))
            return ValidationResult.Ok();

        return ValidationResult.Fail("Invalid CSS border-radius value");
    }

    public static ValidationResult ValidateEmailHash(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return ValidationResult.Fail("Hash is required");
        }

        if (hash.Length != 64)
        {
            return ValidationResult.Fail("Invalid hash length. Must be 64 characters.");
        }

        if (!EmailHashPattern().IsMatch(hash))
        {
            return ValidationResult.Fail("Invalid hash format. Must be a hex string.");
        }

        return ValidationResult.Ok();
    }

    [GeneratedRegex("^[a-fA-F0-9]{64}$")]
    private static partial Regex EmailHashPattern();

    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9_-]*$")]
    private static partial Regex BucketPattern();

    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9_-]*$")]
    private static partial Regex PermissionPattern();

    /// <summary>
    /// Returns true only when the URL uses http or https. Rejects javascript:, data:, and any other scheme
    /// that could execute code when placed in an href attribute.
    /// </summary>
    public static bool IsHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>
    /// Returns <paramref name="value"/> if it passes the CSS color or border-radius allow-list regex;
    /// otherwise returns <paramref name="fallback"/>. Use at render time to defend against stored CSS injection.
    /// </summary>
    public static string SanitizeCssColor(string? value, string fallback) =>
        !string.IsNullOrWhiteSpace(value) && (HexColorPattern().IsMatch(value) || RgbFunctionPattern().IsMatch(value))
            ? value
            : fallback;

    public static string SanitizeCssBorderRadius(string? value, string fallback) =>
        !string.IsNullOrWhiteSpace(value) && BorderRadiusPattern().IsMatch(value)
            ? value
            : fallback;

    [GeneratedRegex(@"^#[0-9a-fA-F]{3,8}$")]
    private static partial Regex HexColorPattern();

    [GeneratedRegex(@"^rgba?\(\s*[\d\s,.%]+\s*\)$")]
    private static partial Regex RgbFunctionPattern();

    [GeneratedRegex(@"^\d+(\.\d+)?(px|rem|em|%)$")]
    private static partial Regex BorderRadiusPattern();
}

public sealed class ValidationResult
{
    public bool IsValid { get; private init; }
    public string? Error { get; private init; }

    private ValidationResult() { }

    public static ValidationResult Ok() => new() { IsValid = true };
    public static ValidationResult Fail(string error) => new() { IsValid = false, Error = error };
}
