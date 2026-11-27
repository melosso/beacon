using System.Net;
using Beacon.Core.Validation;
using Beacon.Security;
using Xunit;

namespace Beacon.Tests;

public class LoginLockoutTests
{
    [Fact]
    public void LocksAfterTenFailures()
    {
        var lockout = new LoginLockout();

        for (var i = 0; i < 9; i++)
            lockout.RecordFailure("user:alice");

        Assert.False(lockout.IsLocked("user:alice", out _));

        lockout.RecordFailure("user:alice");

        Assert.True(lockout.IsLocked("user:alice", out var retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
    }

    [Fact]
    public void SuccessResetsTheCounter()
    {
        var lockout = new LoginLockout();

        for (var i = 0; i < 9; i++)
            lockout.RecordFailure("user:bob");

        lockout.Reset("user:bob");

        for (var i = 0; i < 9; i++)
            lockout.RecordFailure("user:bob");

        Assert.False(lockout.IsLocked("user:bob", out _));
    }

    [Fact]
    public void LockoutIsPerIdentity()
    {
        var lockout = new LoginLockout();

        for (var i = 0; i < 10; i++)
            lockout.RecordFailure("user:alice");

        Assert.True(lockout.IsLocked("user:alice", out _));
        Assert.False(lockout.IsLocked("user:bob", out _));
    }
}

public class SsrfGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("fd00::1")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("192.0.0.170")]
    [InlineData("198.18.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("64:ff9b::a9fe:a9fe")]
    [InlineData("64:ff9b:1::a00:1")]
    [InlineData("2002:a9fe:a9fe::1")]
    [InlineData("ff02::1")]
    public void BlocksPrivateAndReserved(string address)
    {
        Assert.True(SsrfGuard.IsPrivateOrReserved(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("192.0.1.1")]
    [InlineData("223.255.255.1")]
    [InlineData("2606:4700:4700::1111")]
    public void AllowsPublic(string address)
    {
        Assert.False(SsrfGuard.IsPrivateOrReserved(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://example.com")]
    [InlineData("not a url")]
    [InlineData("http://127.0.0.1:8080/")]
    public async Task RejectsUnsafeUrls(string url)
    {
        Assert.Null(await SsrfGuard.ResolveAndValidateAsync(url, TestContext.Current.CancellationToken));
    }
}

public class CssFontSanitizerTests
{
    [Theory]
    [InlineData("Inter")]
    [InlineData("Trebuchet MS")]
    [InlineData("Courier New")]
    public void KeepsPlainFontNames(string font)
    {
        Assert.Equal(font, InputValidator.SanitizeCssFontFamily(font, "fallback"));
    }

    [Theory]
    [InlineData("Inter\"; } </style><script>alert(1)</script>")]
    [InlineData("Inter'}")]
    [InlineData("<script>")]
    [InlineData("")]
    [InlineData(null)]
    public void ReplacesAnythingThatCouldEscapeTheDeclaration(string? font)
    {
        Assert.Equal("fallback", InputValidator.SanitizeCssFontFamily(font, "fallback"));
    }
}

public class S3ClientFactoryTests
{
    // AWS SDK v4 throws when neither a region nor a ServiceURL is configured, where v3 probed the
    // environment. Every combination the settings UI can produce must still construct.
    [Theory]
    [InlineData("s3", null, null)]
    [InlineData("s3", null, "eu-west-1")]
    [InlineData("r2", "https://account.r2.cloudflarestorage.com", null)]
    [InlineData("minio", "https://minio.example.com:9000", "us-east-1")]
    [InlineData("s3", "", "")]
    public void ConstructsForEveryConfiguredCombination(string provider, string? endpoint, string? region)
    {
        using var client = Beacon.Storage.S3ObjectStorageService.CreateClient(
            provider, endpoint, region, "access", "secret");

        Assert.NotNull(client.Config.RegionEndpoint ?? (object?)client.Config.ServiceURL);
    }

    [Fact]
    public void PathStyleIsForcedOnlyForR2AndMinio()
    {
        using var s3 = Beacon.Storage.S3ObjectStorageService.CreateClient("s3", null, null, "a", "b");
        using var r2 = Beacon.Storage.S3ObjectStorageService.CreateClient("r2", "https://x.example", null, "a", "b");

        Assert.False(((Amazon.S3.AmazonS3Config)s3.Config).ForcePathStyle);
        Assert.True(((Amazon.S3.AmazonS3Config)r2.Config).ForcePathStyle);
    }

    [Fact]
    public void SettingsResponseMasksSecretsWithoutChangingTheLiveConfig()
    {
        var encryptor = new Beacon.Core.Security.Encryptor("MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE=");
        var stored = encryptor.Encrypt("secret-plain-value");
        var configService = new Beacon.Tests.Fakes.StubSystemConfigurationService(new Beacon.Core.Services.SystemConfig
        {
            EmailSmtpPassword = stored,
            ObjectStorageSecretKey = stored
        });

        var response = (Microsoft.AspNetCore.Http.HttpResults.Ok<Beacon.Core.Services.SystemConfig>)
            Beacon.Api.AdminEndpoints.GetSystemConfiguration(configService, encryptor);

        Assert.Equal("secr********alue", response.Value!.ObjectStorageSecretKey);
        Assert.Equal(stored, configService.Get().EmailSmtpPassword);
        Assert.Equal(stored, configService.Get().ObjectStorageSecretKey);
    }

    [Fact]
    public void SignsWithDecryptedStorageCredentials()
    {
        var encryptor = new Beacon.Core.Security.Encryptor("MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE=");
        var config = new Beacon.Core.Services.SystemConfig
        {
            ObjectStorage = true,
            ObjectStorageProvider = "minio",
            ObjectStorageEndpoint = "https://minio.example.com",
            ObjectStorageBucket = "beacon",
            ObjectStorageAccessKey = encryptor.Encrypt("access-plain"),
            ObjectStorageSecretKey = encryptor.Encrypt("secret-plain")
        };
        var service = new Beacon.Storage.S3ObjectStorageService(new Beacon.Tests.Fakes.StubSystemConfigurationService(config), encryptor);

        var client = typeof(Beacon.Storage.S3ObjectStorageService)
            .GetField("_client", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(service)!;
        var credentials = (Amazon.Runtime.AWSCredentials)typeof(Amazon.Runtime.AmazonServiceClient)
            .GetProperty("ExplicitAWSCredentials", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!.GetValue(client)!;
        var plain = credentials.GetCredentials();

        Assert.Equal("access-plain", plain.AccessKey);
        Assert.Equal("secret-plain", plain.SecretKey);
    }

    [Theory]
    [InlineData(false, "none", "", true)]
    [InlineData(true, "s3", "https://cdn.example.com", true)]
    [InlineData(true, "s3", "", false)]
    [InlineData(true, "s3", "cdn.example.com", false)]
    [InlineData(true, "r2", "javascript:alert(1)", false)]
    [InlineData(true, "none", "https://cdn.example.com", false)]
    public void ObjectStorageRequiresProviderAndPublicUrl(bool enabled, string provider, string publicUrl, bool valid)
    {
        var config = new Beacon.Core.Services.SystemConfig
        {
            ObjectStorage = enabled,
            ObjectStorageProvider = provider,
            ObjectStoragePublicUrl = publicUrl
        };

        Assert.Equal(valid, Beacon.Api.AdminEndpoints.ValidateObjectStorage(config) is null);
    }

    [Fact]
    public void ObjectStorageLegacySettingsPassOnlyWhenUnchanged()
    {
        var legacy = new Beacon.Core.Services.SystemConfig { ObjectStorage = true, ObjectStorageProvider = "s3" };
        var unchanged = new Beacon.Core.Services.SystemConfig { ObjectStorage = true, ObjectStorageProvider = "s3", LoginFooter = "x" };
        var changed = new Beacon.Core.Services.SystemConfig { ObjectStorage = true, ObjectStorageProvider = "r2" };

        Assert.Null(Beacon.Api.AdminEndpoints.ValidateObjectStorage(unchanged, legacy));
        Assert.NotNull(Beacon.Api.AdminEndpoints.ValidateObjectStorage(changed, legacy));
        Assert.NotNull(Beacon.Api.AdminEndpoints.ValidateObjectStorage(legacy));
    }
}

public class StatusLocalizationTests
{
    // Every language Beacon ships must carry the archived-bucket strings (issue #31). A missing
    // constructor argument is a compile error, but an empty or copy-pasted one is not.
    [Theory]
    [InlineData("en")] [InlineData("de")] [InlineData("fr")]
    [InlineData("nl")] [InlineData("pl")] [InlineData("es")]
    [InlineData("it")] [InlineData("pt")] [InlineData("ja")]
    public void ArchivedStringsArePresentAndDistinct(string lang)
    {
        var t = Beacon.Localization.FormLocalization.GetStatusStrings(lang);

        Assert.False(string.IsNullOrWhiteSpace(t.ArchivedTitle));
        Assert.False(string.IsNullOrWhiteSpace(t.ArchivedMsg));
        Assert.NotEqual(t.InvalidTitle, t.ArchivedTitle);
        Assert.NotEqual(t.InvalidMsg, t.ArchivedMsg);
        Assert.NotEqual(t.ExpiredMsg, t.ArchivedMsg);
    }

    [Theory]
    [InlineData("expired")] [InlineData("invalid")] [InlineData("already_processed")] [InlineData("archived")]
    [InlineData("unsubscribed")] [InlineData("updated")] [InlineData("confirmed")]
    public void StatusPage_UsesDrawnIcon(string status)
    {
        var html = Beacon.Api.ConsentEndpoints.GetStatusPage(status, optedOut: ["news"], keptIn: []);

        Assert.Contains("<svg", html);
        Assert.DoesNotContain("✓", html);
        Assert.DoesNotContain("✗", html);
        Assert.DoesNotContain("⚠", html);
        Assert.DoesNotContain("ℹ", html);
    }

    [Fact]
    public void UnknownLanguageFallsBackToEnglish()
    {
        Assert.Equal(
            Beacon.Localization.FormLocalization.GetStatusStrings("en").ArchivedTitle,
            Beacon.Localization.FormLocalization.GetStatusStrings("xx").ArchivedTitle);
    }
}

public class SupportedLanguageTests
{
    // The token API used to keep its own shorter list, so it rejected languages the pages render.
    [Theory]
    [InlineData("it")] [InlineData("pt")] [InlineData("ja")]
    public void LanguagesAddedInV150AreRenderable(string lang)
    {
        Assert.NotEqual(
            Beacon.Localization.FormLocalization.GetStatusStrings("en").ArchivedTitle,
            Beacon.Localization.FormLocalization.GetStatusStrings(lang).ArchivedTitle);
    }

    [Fact]
    public void EverySupportedLanguageHasItsOwnStrings()
    {
        foreach (var lang in Beacon.Localization.FormLocalization.SupportedLanguages)
        {
            var t = Beacon.Localization.FormLocalization.GetStatusStrings(lang);
            Assert.False(string.IsNullOrWhiteSpace(t.ExpiredTitle));
            Assert.False(string.IsNullOrWhiteSpace(t.ArchivedTitle));
        }
    }
}
