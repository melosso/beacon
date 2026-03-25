using Beacon.Api;
using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Xunit;

namespace Beacon.Tests;

public class WebhookTests
{
    private const string TestEncryptionKey = "MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE=";
    private const string TestBucket = "test-bucket";

    // WebhookService

    [Fact]
    public async Task WebhookService_SaveAndGet_RoundTrips()
    {
        var (service, _) = CreateWebhookService();

        var secret = await service.SaveWebhookConfigAsync(TestBucket, "https://example.com/hook", "POST", null, null);

        Assert.NotEmpty(secret);

        var config = await service.GetWebhookConfigAsync(TestBucket);

        Assert.NotNull(config);
        Assert.Equal("https://example.com/hook", config.EncryptedUrl);
        Assert.Equal("POST", config.EncryptedMethod);
        Assert.True(config.IsEnabled);
    }

    [Fact]
    public async Task WebhookService_Save_NormalizesBucket()
    {
        var (service, repo) = CreateWebhookService();

        await service.SaveWebhookConfigAsync("  My-Bucket  ", "https://example.com/hook", "POST", null, null);

        var config = await repo.GetByBucketAsync("my-bucket");
        Assert.NotNull(config);
    }

    [Fact]
    public async Task WebhookService_Save_EncryptsHeadersAndUrl()
    {
        var (service, repo) = CreateWebhookService();
        var headers = new Dictionary<string, string> { ["Authorization"] = "Bearer secret123" };

        await service.SaveWebhookConfigAsync(TestBucket, "https://example.com/hook", "POST", headers, null);

        var raw = await repo.GetByBucketAsync(TestBucket);
        Assert.NotNull(raw);
        // The stored values should be encrypted (not plaintext)
        Assert.NotEqual("https://example.com/hook", raw.EncryptedUrl);
        Assert.NotEqual("POST", raw.EncryptedMethod);
        Assert.NotNull(raw.EncryptedHeaders);
        Assert.NotEqual("Bearer secret123", raw.EncryptedHeaders);
    }

    [Fact]
    public async Task WebhookService_Delete_RemovesConfig()
    {
        var (service, _) = CreateWebhookService();

        await service.SaveWebhookConfigAsync(TestBucket, "https://example.com/hook", "POST", null, null);
        await service.DeleteWebhookConfigAsync(TestBucket);

        var config = await service.GetWebhookConfigAsync(TestBucket);
        Assert.Null(config);
    }

    [Fact]
    public async Task WebhookService_GetWebhookBuckets_ReturnsOnlyEnabled()
    {
        var (service, repo) = CreateWebhookService();

        await service.SaveWebhookConfigAsync("bucket-a", "https://a.com", "POST", null, null);
        await service.SaveWebhookConfigAsync("bucket-b", "https://b.com", "POST", null, null);

        // Disable bucket-b directly in repo
        var configB = await repo.GetByBucketAsync("bucket-b");
        configB!.IsEnabled = false;
        await repo.UpsertAsync(configB);

        var buckets = await service.GetWebhookBucketsAsync();
        Assert.Contains("bucket-a", buckets);
        Assert.DoesNotContain("bucket-b", buckets);
    }

    [Fact]
    public async Task WebhookService_Trigger_EnqueuesMessage()
    {
        var (service, _) = CreateWebhookService();

        await service.SaveWebhookConfigAsync(TestBucket, "https://example.com/hook", "POST", null, """{"bucket":"{{bucket}}"}""");

        var data = CreateTriggerData();
        await service.TriggerWebhookAsync(TestBucket, data);

        var queue = GetQueue(service);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var message = await queue.DequeueAllAsync(cts.Token).FirstAsync(cts.Token);

        Assert.Equal("https://example.com/hook", message.Url);
        Assert.Equal("POST", message.Method);
        Assert.Contains(TestBucket, message.Body);
    }

    [Fact]
    public async Task WebhookService_Trigger_SkipsDisabledConfig()
    {
        var (service, repo) = CreateWebhookService();

        await service.SaveWebhookConfigAsync(TestBucket, "https://example.com/hook", "POST", null, null);

        var config = await repo.GetByBucketAsync(TestBucket);
        config!.IsEnabled = false;
        await repo.UpsertAsync(config);

        await service.TriggerWebhookAsync(TestBucket, CreateTriggerData());

        // Queue should be empty
        var queue = GetQueue(service);
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var items = new List<WebhookDeliveryMessage>();
        try
        {
            await foreach (var msg in queue.DequeueAllAsync(cts.Token))
                items.Add(msg);
        }
        catch (OperationCanceledException) { }

        Assert.Empty(items);
    }

    [Fact]
    public async Task WebhookService_Trigger_NoBucketConfig_DoesNothing()
    {
        var (service, _) = CreateWebhookService();

        // Should not throw
        await service.TriggerWebhookAsync("nonexistent", CreateTriggerData());
    }

    [Fact]
    public async Task WebhookService_Trigger_IncludesSignature()
    {
        var (service, _) = CreateWebhookService();

        await service.SaveWebhookConfigAsync(TestBucket, "https://example.com/hook", "POST", null, """{"data": true}""");

        await service.TriggerWebhookAsync(TestBucket, CreateTriggerData());

        var queue = GetQueue(service);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var message = await queue.DequeueAllAsync(cts.Token).FirstAsync(cts.Token);

        Assert.NotNull(message.SignatureHeader);
        Assert.StartsWith("sha256=", message.SignatureHeader);
    }

    // SubstituteVariables

    [Fact]
    public void SubstituteVariables_ReplacesAllPlaceholders()
    {
        var data = CreateTriggerData();
        var template = """{"bucket":"{{bucket}}","email":"{{email}}","hash":"{{emailHash}}","permissions":{{permissions}}}""";

        var result = WebhookService.SubstituteVariables(template, data);

        Assert.NotNull(result);
        Assert.Contains(TestBucket, result);
        Assert.Contains("test@example.com", result);
        Assert.Contains("abc123hash", result);
        Assert.Contains("newsletter", result);
    }

    [Fact]
    public void SubstituteVariables_NullTemplate_ReturnsNull()
    {
        var result = WebhookService.SubstituteVariables(null, CreateTriggerData());
        Assert.Null(result);
    }

    [Fact]
    public void SubstituteVariables_EmptyTemplate_ReturnsEmpty()
    {
        var result = WebhookService.SubstituteVariables("", CreateTriggerData());
        Assert.Equal("", result);
    }

    [Fact]
    public void SubstituteVariables_CaseInsensitive()
    {
        var data = CreateTriggerData();

        var result = WebhookService.SubstituteVariables("{{BUCKET}} {{Bucket}} {{bucket}}", data);

        Assert.NotNull(result);
        Assert.Equal($"{TestBucket} {TestBucket} {TestBucket}", result);
    }

    [Fact]
    public void SubstituteVariables_ChangesAlias()
    {
        var data = CreateTriggerData();

        var result = WebhookService.SubstituteVariables("{{changes}}", data);

        Assert.NotNull(result);
        Assert.Contains("newsletter", result);
    }

    [Fact]
    public void SubstituteVariables_CustomFields()
    {
        var data = CreateTriggerData();
        data.CustomFields = """{"company":"Acme"}""";

        var result = WebhookService.SubstituteVariables("{{customFields}}", data);

        Assert.NotNull(result);
        Assert.Contains("Acme", result);
    }

    // WebhookDeliveryQueue

    [Fact]
    public async Task DeliveryQueue_EnqueueDequeue_RoundTrips()
    {
        var queue = new WebhookDeliveryQueue();
        var message = CreateDeliveryMessage();

        await queue.EnqueueAsync(message);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var dequeued = await queue.DequeueAllAsync(cts.Token).FirstAsync(cts.Token);

        Assert.Equal(message.Url, dequeued.Url);
        Assert.Equal(message.Bucket, dequeued.Bucket);
    }

    [Fact]
    public async Task DeliveryQueue_MultipleMessages_PreservesOrder()
    {
        var queue = new WebhookDeliveryQueue();

        for (var i = 0; i < 5; i++)
        {
            await queue.EnqueueAsync(new WebhookDeliveryMessage
            {
                WebhookConfigId = Guid.NewGuid(),
                Url = $"https://example.com/{i}",
                Method = "POST",
                Bucket = TestBucket
            });
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var items = new List<WebhookDeliveryMessage>();
        await foreach (var msg in queue.DequeueAllAsync(cts.Token))
        {
            items.Add(msg);
            if (items.Count == 5) break;
        }

        Assert.Equal(5, items.Count);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal($"https://example.com/{i}", items[i].Url);
        }
    }

    // AdminNotificationService

    [Fact]
    public async Task NotificationService_PublishReachesSubscriber()
    {
        var service = new AdminNotificationService();
        var notification = new WebhookErrorNotification(TestBucket, "Connection refused", 0, DateTime.UtcNow);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        WebhookErrorNotification? received = null;

        var readTask = Task.Run(async () =>
        {
            await foreach (var n in service.SubscribeAsync(cts.Token))
            {
                received = n;
                break;
            }
        }, cts.Token);

        // Small delay to let the subscriber register
        await Task.Delay(50);
        await service.PublishAsync(notification);
        await readTask;

        Assert.NotNull(received);
        Assert.Equal(TestBucket, received.Bucket);
        Assert.Equal("Connection refused", received.ErrorMessage);
    }

    [Fact]
    public async Task NotificationService_MultipleSubscribers_AllReceive()
    {
        var service = new AdminNotificationService();
        var notification = new WebhookErrorNotification(TestBucket, "Timeout", 504, DateTime.UtcNow);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var received1 = (WebhookErrorNotification?)null;
        var received2 = (WebhookErrorNotification?)null;

        var task1 = Task.Run(async () =>
        {
            await foreach (var n in service.SubscribeAsync(cts.Token))
            {
                received1 = n;
                break;
            }
        }, cts.Token);

        var task2 = Task.Run(async () =>
        {
            await foreach (var n in service.SubscribeAsync(cts.Token))
            {
                received2 = n;
                break;
            }
        }, cts.Token);

        await Task.Delay(50);
        await service.PublishAsync(notification);
        await Task.WhenAll(task1, task2);

        Assert.NotNull(received1);
        Assert.NotNull(received2);
        Assert.Equal("Timeout", received1.ErrorMessage);
        Assert.Equal("Timeout", received2.ErrorMessage);
    }

    [Fact]
    public async Task NotificationService_UnsubscribesOnCancellation()
    {
        var service = new AdminNotificationService();
        var cts = new CancellationTokenSource();

        var items = new List<WebhookErrorNotification>();
        var readTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var n in service.SubscribeAsync(cts.Token))
                    items.Add(n);
            }
            catch (OperationCanceledException) { }
        });

        await Task.Delay(50);
        await service.PublishAsync(new WebhookErrorNotification(TestBucket, "err1", 0, DateTime.UtcNow));
        await Task.Delay(50);
        await cts.CancelAsync();
        await readTask;

        Assert.Single(items);

        // After cancellation, new publishes should not throw
        await service.PublishAsync(new WebhookErrorNotification(TestBucket, "err2", 0, DateTime.UtcNow));
    }

    [Fact]
    public async Task NotificationService_NoSubscribers_PublishDoesNotThrow()
    {
        var service = new AdminNotificationService();

        await service.PublishAsync(new WebhookErrorNotification(TestBucket, "orphan", 500, DateTime.UtcNow));
    }

    // WebhookRepository (in-memory)

    [Fact]
    public async Task WebhookRepository_AddAndGetErrors()
    {
        var repo = new InMemoryWebhookRepository();
        var error = new WebhookDeliveryError
        {
            Id = Guid.NewGuid(),
            Bucket = TestBucket,
            ErrorMessage = "Connection refused",
            StatusCode = 0,
            OccurredAt = DateTime.UtcNow
        };

        await repo.AddErrorAsync(error);
        var errors = await repo.GetRecentErrorsAsync(TestBucket);

        Assert.Single(errors);
        Assert.Equal("Connection refused", errors[0].ErrorMessage);
    }

    [Fact]
    public async Task WebhookRepository_GetRecentErrors_RespectsCount()
    {
        var repo = new InMemoryWebhookRepository();

        for (var i = 0; i < 10; i++)
        {
            await repo.AddErrorAsync(new WebhookDeliveryError
            {
                Id = Guid.NewGuid(),
                Bucket = TestBucket,
                ErrorMessage = $"Error {i}",
                StatusCode = 500,
                OccurredAt = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        var errors = await repo.GetRecentErrorsAsync(TestBucket, 3);
        Assert.Equal(3, errors.Count);
    }

    [Fact]
    public async Task WebhookRepository_GetRecentErrors_IsolatesByBucket()
    {
        var repo = new InMemoryWebhookRepository();

        await repo.AddErrorAsync(new WebhookDeliveryError
        {
            Id = Guid.NewGuid(), Bucket = "bucket-a", ErrorMessage = "err-a", StatusCode = 0, OccurredAt = DateTime.UtcNow
        });
        await repo.AddErrorAsync(new WebhookDeliveryError
        {
            Id = Guid.NewGuid(), Bucket = "bucket-b", ErrorMessage = "err-b", StatusCode = 0, OccurredAt = DateTime.UtcNow
        });

        var errorsA = await repo.GetRecentErrorsAsync("bucket-a");
        var errorsB = await repo.GetRecentErrorsAsync("bucket-b");

        Assert.Single(errorsA);
        Assert.Equal("err-a", errorsA[0].ErrorMessage);
        Assert.Single(errorsB);
        Assert.Equal("err-b", errorsB[0].ErrorMessage);
    }

    [Fact]
    public async Task WebhookRepository_DeleteError_RemovesSingle()
    {
        var repo = new InMemoryWebhookRepository();
        var id = Guid.NewGuid();

        await repo.AddErrorAsync(new WebhookDeliveryError
        {
            Id = id, Bucket = TestBucket, ErrorMessage = "to-delete", StatusCode = 0, OccurredAt = DateTime.UtcNow
        });
        await repo.AddErrorAsync(new WebhookDeliveryError
        {
            Id = Guid.NewGuid(), Bucket = TestBucket, ErrorMessage = "to-keep", StatusCode = 0, OccurredAt = DateTime.UtcNow
        });

        await repo.DeleteErrorAsync(id);
        var errors = await repo.GetRecentErrorsAsync(TestBucket);

        Assert.Single(errors);
        Assert.Equal("to-keep", errors[0].ErrorMessage);
    }

    [Fact]
    public async Task WebhookRepository_ClearErrors_RemovesAllForBucket()
    {
        var repo = new InMemoryWebhookRepository();

        await repo.AddErrorAsync(new WebhookDeliveryError
        {
            Id = Guid.NewGuid(), Bucket = TestBucket, ErrorMessage = "err1", StatusCode = 0, OccurredAt = DateTime.UtcNow
        });
        await repo.AddErrorAsync(new WebhookDeliveryError
        {
            Id = Guid.NewGuid(), Bucket = TestBucket, ErrorMessage = "err2", StatusCode = 0, OccurredAt = DateTime.UtcNow
        });
        await repo.AddErrorAsync(new WebhookDeliveryError
        {
            Id = Guid.NewGuid(), Bucket = "other-bucket", ErrorMessage = "other", StatusCode = 0, OccurredAt = DateTime.UtcNow
        });

        await repo.ClearErrorsAsync(TestBucket);

        Assert.Empty(await repo.GetRecentErrorsAsync(TestBucket));
        Assert.Single(await repo.GetRecentErrorsAsync("other-bucket"));
    }

    [Fact]
    public async Task WebhookRepository_PruneErrors_RemovesOldEntries()
    {
        var repo = new InMemoryWebhookRepository();

        await repo.AddErrorAsync(new WebhookDeliveryError
        {
            Id = Guid.NewGuid(), Bucket = TestBucket, ErrorMessage = "old", StatusCode = 0,
            OccurredAt = DateTime.UtcNow.AddDays(-30)
        });
        await repo.AddErrorAsync(new WebhookDeliveryError
        {
            Id = Guid.NewGuid(), Bucket = TestBucket, ErrorMessage = "recent", StatusCode = 0,
            OccurredAt = DateTime.UtcNow
        });

        await repo.PruneErrorsAsync(14);
        var errors = await repo.GetRecentErrorsAsync(TestBucket);

        Assert.Single(errors);
        Assert.Equal("recent", errors[0].ErrorMessage);
    }

    [Fact]
    public async Task WebhookRepository_UpsertConfig_UpdatesExisting()
    {
        var repo = new InMemoryWebhookRepository();
        var config = new WebhookConfig
        {
            Id = Guid.NewGuid(),
            Bucket = TestBucket,
            EncryptedUrl = "enc-url-1",
            EncryptedMethod = "enc-post",
            IsEnabled = true
        };

        await repo.UpsertAsync(config);

        config.EncryptedUrl = "enc-url-2";
        await repo.UpsertAsync(config);

        var result = await repo.GetByBucketAsync(TestBucket);
        Assert.NotNull(result);
        Assert.Equal("enc-url-2", result.EncryptedUrl);

        // Should still be one config, not two
        var all = await repo.GetAllAsync();
        Assert.Single(all);
    }

    [Fact]
    public async Task WebhookRepository_UpdateTriggerStats()
    {
        var repo = new InMemoryWebhookRepository();
        var id = Guid.NewGuid();
        await repo.UpsertAsync(new WebhookConfig
        {
            Id = id, Bucket = TestBucket, EncryptedUrl = "x", EncryptedMethod = "y", IsEnabled = true
        });

        var now = DateTime.UtcNow;
        await repo.UpdateTriggerStatsAsync(id, now);

        var config = await repo.GetByBucketAsync(TestBucket);
        Assert.Equal(1, config!.TriggerCount);
        Assert.Equal(now, config.LastTriggeredAt);
    }

    // Helpers

    private static (WebhookService service, InMemoryWebhookRepository repo) CreateWebhookService()
    {
        var encryptor = new Encryptor(TestEncryptionKey);
        var repo = new InMemoryWebhookRepository();
        var queue = new WebhookDeliveryQueue();
        var service = new WebhookService(repo, encryptor, queue);
        return (service, repo);
    }

    private static WebhookDeliveryQueue GetQueue(WebhookService service)
    {
        // Access the queue via reflection since it's a private field
        var field = typeof(WebhookService).GetField("_deliveryQueue",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (WebhookDeliveryQueue)field.GetValue(service)!;
    }

    private static WebhookTriggerData CreateTriggerData() => new()
    {
        Bucket = TestBucket,
        Email = "test@example.com",
        EmailHash = "abc123hash",
        Permissions =
        [
            new PermissionState { Permission = "newsletter", Status = ConsentStatus.OptedIn }
        ]
    };

    private static WebhookDeliveryMessage CreateDeliveryMessage() => new()
    {
        WebhookConfigId = Guid.NewGuid(),
        Url = "https://example.com/hook",
        Method = "POST",
        Bucket = TestBucket,
        Body = """{"test": true}"""
    };

    // In-Memory Test Double

    private sealed class InMemoryWebhookRepository : IWebhookRepository
    {
        private readonly List<WebhookConfig> _configs = [];
        private readonly List<WebhookDeliveryError> _errors = [];

        public Task<WebhookConfig?> GetByBucketAsync(string bucket)
        {
            return Task.FromResult(_configs.FirstOrDefault(c => c.Bucket == bucket));
        }

        public Task<List<WebhookConfig>> GetAllAsync()
        {
            return Task.FromResult(_configs.ToList());
        }

        public Task UpsertAsync(WebhookConfig config)
        {
            _configs.RemoveAll(c => c.Bucket == config.Bucket);
            _configs.Add(config);
            return Task.CompletedTask;
        }

        public Task DeleteByBucketAsync(string bucket)
        {
            _configs.RemoveAll(c => c.Bucket == bucket);
            return Task.CompletedTask;
        }

        public Task UpdateTriggerStatsAsync(Guid id, DateTime triggeredAt)
        {
            var config = _configs.FirstOrDefault(c => c.Id == id);
            if (config != null)
            {
                config.TriggerCount++;
                config.LastTriggeredAt = triggeredAt;
            }
            return Task.CompletedTask;
        }

        public Task AddErrorAsync(WebhookDeliveryError error)
        {
            _errors.Add(error);
            return Task.CompletedTask;
        }

        public Task<List<WebhookDeliveryError>> GetRecentErrorsAsync(string bucket, int count = 5)
        {
            return Task.FromResult(
                _errors.Where(e => e.Bucket == bucket)
                    .OrderByDescending(e => e.OccurredAt)
                    .Take(count)
                    .ToList());
        }

        public Task DeleteErrorAsync(Guid id)
        {
            _errors.RemoveAll(e => e.Id == id);
            return Task.CompletedTask;
        }

        public Task ClearErrorsAsync(string bucket)
        {
            _errors.RemoveAll(e => e.Bucket == bucket);
            return Task.CompletedTask;
        }

        public Task PruneErrorsAsync(int retentionDays = 14)
        {
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            _errors.RemoveAll(e => e.OccurredAt < cutoff);
            return Task.CompletedTask;
        }
    }
}
