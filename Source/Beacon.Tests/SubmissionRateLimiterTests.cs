using Beacon.Security;
using Xunit;

namespace Beacon.Tests;

public class SubmissionRateLimiterTests
{
    [Fact]
    public void IsRateLimited_FirstRequest_NotLimited()
    {
        var limiter = new SubmissionRateLimiter();
        var formId = Guid.NewGuid();

        var result = limiter.IsRateLimited(formId, "127.0.0.1", 10);

        Assert.False(result);
    }

    [Fact]
    public void IsRateLimited_UnderLimit_NotLimited()
    {
        var limiter = new SubmissionRateLimiter();
        var formId = Guid.NewGuid();

        for (int i = 0; i < 5; i++)
        {
            limiter.IsRateLimited(formId, "127.0.0.1", 10);
        }

        var result = limiter.IsRateLimited(formId, "127.0.0.1", 10);
        Assert.False(result);
    }

    [Fact]
    public void IsRateLimited_ExceedsLimit_ReturnsTrue()
    {
        var limiter = new SubmissionRateLimiter();
        var formId = Guid.NewGuid();

        // Each call adds a timestamp, so make maxRequestsPerMinute+1 calls
        bool wasLimited = false;
        for (int i = 0; i < 15; i++)
        {
            if (limiter.IsRateLimited(formId, "127.0.0.1", 5))
            {
                wasLimited = true;
                break;
            }
        }

        Assert.True(wasLimited);
    }

    [Fact]
    public void IsRateLimited_DifferentIps_TrackedSeparately()
    {
        var limiter = new SubmissionRateLimiter();
        var formId = Guid.NewGuid();

        // Exhaust limit for IP A
        for (int i = 0; i < 6; i++)
        {
            limiter.IsRateLimited(formId, "10.0.0.1", 5);
        }

        // IP B should not be limited
        var result = limiter.IsRateLimited(formId, "10.0.0.2", 5);
        Assert.False(result);
    }

    [Fact]
    public void IsRateLimited_DifferentForms_TrackedSeparately()
    {
        var limiter = new SubmissionRateLimiter();
        var formA = Guid.NewGuid();
        var formB = Guid.NewGuid();

        // Exhaust limit for form A
        for (int i = 0; i < 6; i++)
        {
            limiter.IsRateLimited(formA, "127.0.0.1", 5);
        }

        // Form B should not be limited
        var result = limiter.IsRateLimited(formB, "127.0.0.1", 5);
        Assert.False(result);
    }

    [Fact]
    public void IsRateLimited_ExactlyAtLimit_NotLimited()
    {
        var limiter = new SubmissionRateLimiter();
        var formId = Guid.NewGuid();

        // Make exactly maxRequestsPerMinute calls (limit is > not >=)
        bool limited = false;
        for (int i = 0; i < 3; i++)
        {
            limited = limiter.IsRateLimited(formId, "127.0.0.1", 3);
        }

        // At exactly 3 requests with limit 3, should not be limited (count == max, not count > max)
        Assert.False(limited);
    }

    [Fact]
    public void IsRateLimited_OneOverLimit_IsLimited()
    {
        var limiter = new SubmissionRateLimiter();
        var formId = Guid.NewGuid();

        bool lastResult = false;
        for (int i = 0; i < 4; i++)
        {
            lastResult = limiter.IsRateLimited(formId, "127.0.0.1", 3);
        }

        Assert.True(lastResult);
    }
}
