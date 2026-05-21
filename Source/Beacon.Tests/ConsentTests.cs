using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Beacon.Tests.Fakes;
using Xunit;

namespace Beacon.Tests;

public class ConsentTests
{
    private const string TestPepper = "test-pepper-for-hashing";
    private const string TestEncryptionKey = "MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE=";  // 32 bytes
    private const string TestBucket = "test-bucket";

    private ConsentService CreateService(InMemoryConsentRepository repository)
    {
        var emailHasher = new EmailHasher(TestPepper);
        var encryptor = new Encryptor(TestEncryptionKey);
        return new ConsentService(repository, new NullBeaconCacheService(), new Beacon.Tests.Fakes.StubSystemConfigurationService(), emailHasher, encryptor);
    }

    [Fact]
    public async Task ConsentService_CheckAsync_ReturnsOptedInByDefault()
    {
        var repository = new InMemoryConsentRepository();
        var service = CreateService(repository);

        var status = await service.CheckAsync(TestBucket, "new@example.com", "newsletter");

        Assert.Equal(ConsentStatus.OptedIn, status);
    }

    [Fact]
    public async Task ConsentService_ProcessOptOut_SetsOptedOut()
    {
        var repository = new InMemoryConsentRepository();
        var service = CreateService(repository);

        await service.ProcessOptOutAsync(TestBucket, "test@example.com", ["newsletter"], "token123", ConsentSource.Url);

        var status = await service.CheckAsync(TestBucket, "test@example.com", "newsletter");
        Assert.Equal(ConsentStatus.OptedOut, status);
    }

    [Fact]
    public async Task ConsentService_ProcessOptOut_HandlesMultiplePermissions()
    {
        var repository = new InMemoryConsentRepository();
        var service = CreateService(repository);

        await service.ProcessOptOutAsync(TestBucket, "test@example.com", ["newsletter", "alerts"], "token123", ConsentSource.Url);

        var newsletterStatus = await service.CheckAsync(TestBucket, "test@example.com", "newsletter");
        var alertsStatus = await service.CheckAsync(TestBucket, "test@example.com", "alerts");

        Assert.Equal(ConsentStatus.OptedOut, newsletterStatus);
        Assert.Equal(ConsentStatus.OptedOut, alertsStatus);
    }

    [Fact]
    public async Task ConsentService_Override_ChangesStatus()
    {
        var repository = new InMemoryConsentRepository();
        var service = CreateService(repository);

        await service.ProcessOptOutAsync(TestBucket, "test@example.com", ["newsletter"], "token123", ConsentSource.Url);
        var statusBefore = await service.CheckAsync(TestBucket, "test@example.com", "newsletter");

        await service.OverrideAsync(TestBucket, "test@example.com", "newsletter", ConsentStatus.OptedIn);
        var statusAfter = await service.CheckAsync(TestBucket, "test@example.com", "newsletter");

        Assert.Equal(ConsentStatus.OptedOut, statusBefore);
        Assert.Equal(ConsentStatus.OptedIn, statusAfter);
    }

    [Fact]
    public async Task ConsentService_NormalizesEmail()
    {
        var repository = new InMemoryConsentRepository();
        var service = CreateService(repository);

        await service.ProcessOptOutAsync(TestBucket, "TEST@EXAMPLE.COM", ["newsletter"], "token123", ConsentSource.Url);

        var status = await service.CheckAsync(TestBucket, "  test@example.com  ", "newsletter");
        Assert.Equal(ConsentStatus.OptedOut, status);
    }

    [Fact]
    public async Task ConsentService_DifferentBuckets_AreIsolated()
    {
        var repository = new InMemoryConsentRepository();
        var service = CreateService(repository);

        await service.ProcessOptOutAsync("bucket-a", "test@example.com", ["newsletter"], "token123", ConsentSource.Url);

        var statusBucketA = await service.CheckAsync("bucket-a", "test@example.com", "newsletter");
        var statusBucketB = await service.CheckAsync("bucket-b", "test@example.com", "newsletter");

        Assert.Equal(ConsentStatus.OptedOut, statusBucketA);
        Assert.Equal(ConsentStatus.OptedIn, statusBucketB);
    }

}
