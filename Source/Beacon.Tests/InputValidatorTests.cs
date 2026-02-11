using Beacon.Core.Validation;
using Xunit;

namespace Beacon.Tests;

public class InputValidatorTests
{
    // --- ValidateEmail ---

    [Fact]
    public void ValidateEmail_ValidEmail_ReturnsOk()
    {
        var result = InputValidator.ValidateEmail("user@example.com");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateEmail_Null_ReturnsFail()
    {
        var result = InputValidator.ValidateEmail(null);
        Assert.False(result.IsValid);
        Assert.Equal("Email is required", result.Error);
    }

    [Fact]
    public void ValidateEmail_Empty_ReturnsFail()
    {
        var result = InputValidator.ValidateEmail("");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateEmail_Whitespace_ReturnsFail()
    {
        var result = InputValidator.ValidateEmail("   ");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateEmail_TooLong_ReturnsFail()
    {
        var longEmail = new string('a', 251) + "@b.c"; // 255 chars, exceeds 254 limit
        var result = InputValidator.ValidateEmail(longEmail);
        Assert.False(result.IsValid);
        Assert.Equal("Email too long", result.Error);
    }

    [Fact]
    public void ValidateEmail_InvalidFormat_ReturnsFail()
    {
        var result = InputValidator.ValidateEmail("not-an-email");
        Assert.False(result.IsValid);
        Assert.Equal("Invalid email format", result.Error);
    }

    [Fact]
    public void ValidateEmail_MissingDomain_ReturnsFail()
    {
        var result = InputValidator.ValidateEmail("user@");
        Assert.False(result.IsValid);
    }

    // --- ValidatePermission ---

    [Fact]
    public void ValidatePermission_Valid_ReturnsOk()
    {
        var result = InputValidator.ValidatePermission("newsletter");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidatePermission_WithHyphensAndUnderscores_ReturnsOk()
    {
        var result = InputValidator.ValidatePermission("my-news_letter");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidatePermission_Null_ReturnsFail()
    {
        var result = InputValidator.ValidatePermission(null);
        Assert.False(result.IsValid);
        Assert.Equal("Permission is required", result.Error);
    }

    [Fact]
    public void ValidatePermission_TooLong_ReturnsFail()
    {
        var result = InputValidator.ValidatePermission(new string('a', 51));
        Assert.False(result.IsValid);
        Assert.Equal("Permission name too long", result.Error);
    }

    [Fact]
    public void ValidatePermission_StartsWithNumber_ReturnsFail()
    {
        var result = InputValidator.ValidatePermission("1newsletter");
        Assert.False(result.IsValid);
        Assert.Equal("Permission contains invalid characters", result.Error);
    }

    [Fact]
    public void ValidatePermission_ContainsSpaces_ReturnsFail()
    {
        var result = InputValidator.ValidatePermission("news letter");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidatePermission_ContainsSpecialChars_ReturnsFail()
    {
        var result = InputValidator.ValidatePermission("news@letter");
        Assert.False(result.IsValid);
    }

    // --- ValidatePermissions (array) ---

    [Fact]
    public void ValidatePermissions_ValidArray_ReturnsOk()
    {
        var result = InputValidator.ValidatePermissions(["newsletter", "alerts"]);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidatePermissions_Null_ReturnsFail()
    {
        var result = InputValidator.ValidatePermissions(null);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidatePermissions_Empty_ReturnsFail()
    {
        var result = InputValidator.ValidatePermissions([]);
        Assert.False(result.IsValid);
        Assert.Equal("At least one permission is required", result.Error);
    }

    [Fact]
    public void ValidatePermissions_TooMany_ReturnsFail()
    {
        var permissions = Enumerable.Range(0, 11).Select(i => $"perm{(char)('a' + i)}").ToArray();
        var result = InputValidator.ValidatePermissions(permissions);
        Assert.False(result.IsValid);
        Assert.Equal("Too many permissions", result.Error);
    }

    [Fact]
    public void ValidatePermissions_Duplicates_ReturnsFail()
    {
        var result = InputValidator.ValidatePermissions(["newsletter", "newsletter"]);
        Assert.False(result.IsValid);
        Assert.Contains("Duplicate permission", result.Error);
    }

    [Fact]
    public void ValidatePermissions_InvalidPermissionInArray_ReturnsFail()
    {
        var result = InputValidator.ValidatePermissions(["newsletter", "1invalid"]);
        Assert.False(result.IsValid);
    }

    // --- ValidateBucket ---

    [Fact]
    public void ValidateBucket_Valid_ReturnsOk()
    {
        var result = InputValidator.ValidateBucket("my-bucket");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateBucket_WithUnderscores_ReturnsOk()
    {
        var result = InputValidator.ValidateBucket("my_bucket");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateBucket_Null_ReturnsFail()
    {
        var result = InputValidator.ValidateBucket(null);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateBucket_TooLong_ReturnsFail()
    {
        var result = InputValidator.ValidateBucket(new string('a', 101));
        Assert.False(result.IsValid);
        Assert.Equal("Bucket name too long", result.Error);
    }

    [Fact]
    public void ValidateBucket_StartsWithNumber_ReturnsFail()
    {
        var result = InputValidator.ValidateBucket("1bucket");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateBucket_ContainsSpaces_ReturnsFail()
    {
        var result = InputValidator.ValidateBucket("my bucket");
        Assert.False(result.IsValid);
    }

    // --- ValidateOrigin ---

    [Fact]
    public void ValidateOrigin_ValidHttps_ReturnsOk()
    {
        var result = InputValidator.ValidateOrigin("https://example.com");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateOrigin_ValidHttp_ReturnsOk()
    {
        var result = InputValidator.ValidateOrigin("http://localhost");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateOrigin_Null_ReturnsFail()
    {
        var result = InputValidator.ValidateOrigin(null);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateOrigin_NotAbsoluteUrl_ReturnsFail()
    {
        var result = InputValidator.ValidateOrigin("example.com");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateOrigin_FtpScheme_ReturnsFail()
    {
        var result = InputValidator.ValidateOrigin("ftp://example.com");
        Assert.False(result.IsValid);
        Assert.Equal("Origin must use http or https", result.Error);
    }

    [Fact]
    public void ValidateOrigin_WithPath_ReturnsFail()
    {
        var result = InputValidator.ValidateOrigin("https://example.com/path");
        Assert.False(result.IsValid);
        Assert.Equal("Origin must not contain a path, query, or fragment", result.Error);
    }

    [Fact]
    public void ValidateOrigin_WithQuery_ReturnsFail()
    {
        var result = InputValidator.ValidateOrigin("https://example.com?key=val");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateOrigin_WithFragment_ReturnsFail()
    {
        var result = InputValidator.ValidateOrigin("https://example.com#section");
        Assert.False(result.IsValid);
    }

    // --- ValidateSubmissionName ---

    [Fact]
    public void ValidateSubmissionName_Valid_ReturnsOk()
    {
        var result = InputValidator.ValidateSubmissionName("My Form");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateSubmissionName_Null_ReturnsFail()
    {
        var result = InputValidator.ValidateSubmissionName(null);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateSubmissionName_TooLong_ReturnsFail()
    {
        var result = InputValidator.ValidateSubmissionName(new string('a', 201));
        Assert.False(result.IsValid);
        Assert.Contains("max 200", result.Error);
    }

    // --- ValidatePrivacyPolicyUrl ---

    [Fact]
    public void ValidatePrivacyPolicyUrl_Null_ReturnsOk()
    {
        var result = InputValidator.ValidatePrivacyPolicyUrl(null);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidatePrivacyPolicyUrl_ValidHttps_ReturnsOk()
    {
        var result = InputValidator.ValidatePrivacyPolicyUrl("https://example.com/privacy");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidatePrivacyPolicyUrl_Http_ReturnsFail()
    {
        var result = InputValidator.ValidatePrivacyPolicyUrl("http://example.com/privacy");
        Assert.False(result.IsValid);
        Assert.Equal("Privacy policy URL must use HTTPS", result.Error);
    }

    [Fact]
    public void ValidatePrivacyPolicyUrl_NotAbsolute_ReturnsFail()
    {
        var result = InputValidator.ValidatePrivacyPolicyUrl("not-a-url");
        Assert.False(result.IsValid);
    }

    // --- ValidateConsentText ---

    [Fact]
    public void ValidateConsentText_Null_ReturnsOk()
    {
        var result = InputValidator.ValidateConsentText(null);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateConsentText_ValidText_ReturnsOk()
    {
        var result = InputValidator.ValidateConsentText("I agree to receive emails");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateConsentText_TooLong_ReturnsFail()
    {
        var result = InputValidator.ValidateConsentText(new string('a', 501));
        Assert.False(result.IsValid);
        Assert.Contains("max 500", result.Error);
    }

    // --- ValidateCssColor ---

    [Theory]
    [InlineData("#fff")]
    [InlineData("#ffffff")]
    [InlineData("#FF0000")]
    [InlineData("#ff000080")]
    [InlineData("#abc")]
    public void ValidateCssColor_HexColors_ReturnsOk(string color)
    {
        var result = InputValidator.ValidateCssColor(color);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("blue")]
    [InlineData("transparent")]
    [InlineData("inherit")]
    [InlineData("WhiteSmoke")]
    public void ValidateCssColor_NamedColors_ReturnsOk(string color)
    {
        var result = InputValidator.ValidateCssColor(color);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("rgb(255, 0, 0)")]
    [InlineData("rgba(255, 0, 0, 0.5)")]
    [InlineData("rgb(100%, 0%, 50%)")]
    public void ValidateCssColor_RgbFunctions_ReturnsOk(string color)
    {
        var result = InputValidator.ValidateCssColor(color);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateCssColor_Null_ReturnsOk()
    {
        var result = InputValidator.ValidateCssColor(null);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateCssColor_Empty_ReturnsOk()
    {
        var result = InputValidator.ValidateCssColor("  ");
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("red; } body { background: url(evil)")]
    [InlineData("expression(alert(1))")]
    [InlineData("url(javascript:alert(1))")]
    [InlineData("#fff; } .injected { display: block")]
    [InlineData("</style><script>alert(1)</script>")]
    [InlineData("var(--custom)")]
    public void ValidateCssColor_InjectionPayloads_ReturnsFail(string color)
    {
        var result = InputValidator.ValidateCssColor(color);
        Assert.False(result.IsValid);
        Assert.Equal("Invalid CSS color value", result.Error);
    }

    [Fact]
    public void ValidateCssColor_InvalidHex_ReturnsFail()
    {
        var result = InputValidator.ValidateCssColor("#gggggg");
        Assert.False(result.IsValid);
    }

    // --- ValidateCssBorderRadius ---

    [Theory]
    [InlineData("8px")]
    [InlineData("0.5rem")]
    [InlineData("50%")]
    [InlineData("1em")]
    [InlineData("12px")]
    public void ValidateCssBorderRadius_ValidValues_ReturnsOk(string value)
    {
        var result = InputValidator.ValidateCssBorderRadius(value);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateCssBorderRadius_Null_ReturnsOk()
    {
        var result = InputValidator.ValidateCssBorderRadius(null);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateCssBorderRadius_Empty_ReturnsOk()
    {
        var result = InputValidator.ValidateCssBorderRadius("  ");
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("8px; } body { background: url(evil)")]
    [InlineData("expression(alert(1))")]
    [InlineData("calc(100% - 10px)")]
    [InlineData("8px 4px")]
    [InlineData("abc")]
    public void ValidateCssBorderRadius_InjectionPayloads_ReturnsFail(string value)
    {
        var result = InputValidator.ValidateCssBorderRadius(value);
        Assert.False(result.IsValid);
        Assert.Equal("Invalid CSS border-radius value", result.Error);
    }
}
