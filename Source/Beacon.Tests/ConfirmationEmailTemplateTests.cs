using Beacon.Core.Templates;
using Xunit;

namespace Beacon.Tests;

public class ConfirmationEmailTemplateTests
{
    // ── GetSubject ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("en", "Confirm your sign-up")]
    [InlineData("de", "Anmeldung bestätigen")]
    [InlineData("fr", "Confirme ton inscription")]
    [InlineData("nl", "Nog één stap om je aanmelding af te ronden")]
    [InlineData("pl", "Potwierdź swoją rejestrację")]
    [InlineData("es", "Confirma tu suscripción")]
    public void GetSubject_ReturnsCorrectStringPerLanguage(string lang, string expected)
    {
        Assert.Equal(expected, ConfirmationEmailTemplate.GetSubject(lang));
    }

    [Theory]
    [InlineData("EN")]
    [InlineData("De")]
    public void GetSubject_IsCaseInsensitive(string lang)
    {
        var lower = ConfirmationEmailTemplate.GetSubject(lang.ToLowerInvariant());
        var mixed = ConfirmationEmailTemplate.GetSubject(lang);
        Assert.Equal(lower, mixed);
    }

    [Fact]
    public void GetSubject_FallsBackToEnglish_ForUnknownLanguage()
    {
        var result = ConfirmationEmailTemplate.GetSubject("xx");
        Assert.Equal("Confirm your sign-up", result);
    }

    [Fact]
    public void GetSubject_FallsBackToEnglish_ForNull()
    {
        var result = ConfirmationEmailTemplate.GetSubject(null!);
        Assert.Equal("Confirm your sign-up", result);
    }

    // ── Render ────────────────────────────────────────────────────────────────

    [Fact]
    public void Render_ContainsConfirmationUrl()
    {
        var html = ConfirmationEmailTemplate.Render("my-bucket", "newsletter", "https://example.com/confirm/abc", "en");

        Assert.Contains("https://example.com/confirm/abc", html);
    }

    [Fact]
    public void Render_DoesNotContainBucketName()
    {
        var html = ConfirmationEmailTemplate.Render("acme-corp", "newsletter", "https://example.com/confirm/x", "en");

        Assert.DoesNotContain("acme-corp", html);
    }

    [Fact]
    public void Render_FormatsPermissionAsTitleCase()
    {
        var html = ConfirmationEmailTemplate.Render("bucket", "weekly_digest", "https://example.com/confirm/x", "en");

        Assert.Contains("Weekly Digest", html);
    }

    [Fact]
    public void Render_FormatsKebabCasePermission()
    {
        var html = ConfirmationEmailTemplate.Render("bucket", "product-updates", "https://example.com/confirm/x", "en");

        Assert.Contains("Product Updates", html);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("nl")]
    [InlineData("pl")]
    [InlineData("es")]
    public void Render_SetsCorrectLangAttribute(string lang)
    {
        var html = ConfirmationEmailTemplate.Render("bucket", "newsletter", "https://example.com/c", lang);

        Assert.Contains($"lang=\"{lang}\"", html);
    }

    [Fact]
    public void Render_FallsBackToEnglish_ForUnknownLanguage()
    {
        var html = ConfirmationEmailTemplate.Render("bucket", "newsletter", "https://example.com/c", "xx");

        Assert.Contains("lang=\"en\"", html);
        Assert.Contains("Yes, I want to receive these emails", html);
    }

    [Fact]
    public void Render_HtmlEncodesSpecialCharsInPermission()
    {
        var html = ConfirmationEmailTemplate.Render("bucket", "<xss>", "https://example.com/c", "en");

        Assert.DoesNotContain("<xss>", html);
    }

    [Fact]
    public void Render_HtmlEncodesAmpersandInUrl()
    {
        var html = ConfirmationEmailTemplate.Render("bucket", "newsletter", "https://example.com/c?a=1&b=2", "en");

        Assert.Contains("&amp;", html);
        Assert.DoesNotContain("\"https://example.com/c?a=1&b=2\"", html);
    }

    [Fact]
    public void Render_IsValidHtmlDocument()
    {
        var html = ConfirmationEmailTemplate.Render("bucket", "newsletter", "https://example.com/c", "en");

        Assert.StartsWith("<!DOCTYPE html>", html.TrimStart());
        Assert.Contains("</html>", html);
    }

    [Fact]
    public void Render_ContainsButtonLinkTag()
    {
        var html = ConfirmationEmailTemplate.Render("bucket", "newsletter", "https://example.com/confirm/tok", "en");

        Assert.Contains("<a href=", html);
        Assert.Contains("class=\"btn\"", html);
    }

    [Fact]
    public void Render_ContainsMaskedEmailInFooter()
    {
        var email = "john.doe@example.com";
        var html = ConfirmationEmailTemplate.Render("bucket", "newsletter", "https://example.com/confirm/tok", "en", email);

        Assert.Contains("Preferences for:", html);
        Assert.Contains("j***e@example.com", html);
    }
}
