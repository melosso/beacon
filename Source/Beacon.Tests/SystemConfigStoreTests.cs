using Beacon.Core.Services;
using Xunit;

namespace Beacon.Tests;

public sealed class SystemConfigStoreTests
{
    [Fact]
    public void Read_StoredBeforeDefaults_EnablesPoliciesWithApproval()
    {
        var config = SystemConfigStore.Read("""{"dataPoliciesEnabled":false,"retentionPurgeEnabled":false,"pendingConfirmationPurgeEnabled":false,"retentionPurgeRequireApproval":false}""");

        Assert.True(config.DataPoliciesEnabled);
        Assert.True(config.RetentionPurgeEnabled);
        Assert.True(config.PendingConfirmationPurgeEnabled);
        Assert.True(config.RetentionPurgeRequireApproval);
        Assert.True(config.PendingConfirmationPurgeRequireApproval);
    }

    [Fact]
    public void Read_StoredBeforeDefaults_KeepsConfiguredPolicies()
    {
        var config = SystemConfigStore.Read("""{"dataPoliciesEnabled":false,"retentionPurgeEnabled":true,"pendingConfirmationPurgeEnabled":false,"retentionPurgeRequireApproval":false}""");

        Assert.False(config.DataPoliciesEnabled);
        Assert.False(config.PendingConfirmationPurgeEnabled);
        Assert.False(config.RetentionPurgeRequireApproval);
    }

    [Fact]
    public void Read_StoredAfterDefaults_KeepsDisabledPolicies()
    {
        var config = SystemConfigStore.Read("""{"dataPoliciesEnabled":false,"retentionPurgeEnabled":false,"pendingConfirmationPurgeEnabled":false,"erasureNoticeEnabled":true}""");

        Assert.False(config.DataPoliciesEnabled);
        Assert.False(config.RetentionPurgeEnabled);
        Assert.False(config.PendingConfirmationPurgeEnabled);
    }
}
