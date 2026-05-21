namespace Beacon.Core.Services;

public static class CacheKeys
{
    public const string BucketPermissions = "buckets";

    public static string Consent(string bucket, string emailHash, string permission)
        => $"consent:{bucket}:{emailHash}:{permission}";
}
