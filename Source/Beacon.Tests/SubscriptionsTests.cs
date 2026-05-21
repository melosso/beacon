using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Beacon.Tests.Fakes;
using Xunit;

namespace Beacon.Tests;

public class SubscriptionsTests
{
    private const string TestPepper = "test-pepper-for-hashing";
    private const string TestEncryptionKey = "MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE=";

    [Fact]
    public async Task GetIdentityDetailsAsync_ReturnsPermissionsAcrossBuckets()
    {
        // Arrange
        var repository = new InMemoryConsentRepository();
        var emailHasher = new EmailHasher(TestPepper);
        var encryptor = new Encryptor(TestEncryptionKey);
        var service = new ConsentService(repository, new NullBeaconCacheService(), new StubSystemConfigurationService(), emailHasher, encryptor);

        var email = "user@example.com";
        var emailHash = emailHasher.Hash(email);

        // Bucket A
        await service.OverrideAsync("bucket-a", email, "news", ConsentStatus.OptedIn);
        await service.OverrideAsync("bucket-a", email, "marketing", ConsentStatus.OptedOut);

        // Bucket B
        await service.OverrideAsync("bucket-b", email, "alerts", ConsentStatus.OptedIn);

        // Act
        var details = await repository.GetIdentityDetailsAsync(emailHash);

        // Assert
        Assert.NotNull(details);
        Assert.Equal(2, details.Subscriptions.Count);

        var subA = details.Subscriptions.First(s => s.Bucket == "bucket-a");
        Assert.True(subA.Permissions["news"]);
        Assert.False(subA.Permissions["marketing"]);

        var subB = details.Subscriptions.First(s => s.Bucket == "bucket-b");
        Assert.True(subB.Permissions["alerts"]);
    }
}
