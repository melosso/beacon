using Beacon.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Beacon.Tests;

public class EndpointAuthorizationTests
{
    private static readonly Dictionary<string, string> Expected = new()
    {
        ["DELETE /api/admin/api-keys/{id:guid}"] = "Admin",
        ["DELETE /api/admin/brand-identities/{id:int}"] = "Admin",
        ["DELETE /api/admin/buckets/{bucket}"] = "Admin",
        ["DELETE /api/admin/buckets/{bucket}/permissions/{permission}"] = "BucketsWrite",
        ["DELETE /api/admin/buckets/{bucket}/records/{emailHash}"] = "ConsentWrite",
        ["DELETE /api/admin/buckets/{bucket}/webhook"] = "Admin",
        ["DELETE /api/admin/buckets/{bucket}/webhook/errors"] = "Admin",
        ["DELETE /api/admin/buckets/{bucket}/webhook/errors/{id}"] = "Admin",
        ["DELETE /api/admin/cache"] = "Admin",
        ["DELETE /api/admin/identities/{emailHash}"] = "ConsentWrite",
        ["DELETE /api/admin/oidc/link"] = "authenticated",
        ["DELETE /api/admin/oidc/providers/{id:int}"] = "Admin",
        ["DELETE /api/admin/submissions/{id:guid}"] = "SubmissionsWrite",
        ["DELETE /api/admin/users/{id:guid}"] = "Admin",
        ["GET /api/admin/api-keys"] = "Admin",
        ["GET /api/admin/audit"] = "AuditRead",
        ["GET /api/admin/auth/me"] = "authenticated",
        ["GET /api/admin/auth/oidc/{slug}/callback"] = "none",
        ["GET /api/admin/auth/oidc/{slug}/start"] = "none",
        ["GET /api/admin/auth/providers"] = "none",
        ["GET /api/admin/brand-identities"] = "Admin",
        ["GET /api/admin/buckets"] = "BucketsRead",
        ["GET /api/admin/buckets/{bucket}"] = "BucketsRead",
        ["GET /api/admin/buckets/{bucket}/options"] = "BucketsRead",
        ["GET /api/admin/buckets/{bucket}/records"] = "ConsentRead",
        ["GET /api/admin/buckets/{bucket}/webhook"] = "WebhooksRead",
        ["GET /api/admin/buckets/{bucket}/webhook/errors"] = "WebhooksRead",
        ["GET /api/admin/cache/stats"] = "Admin",
        ["GET /api/admin/data-policies/tasks"] = "Admin",
        ["GET /api/admin/events"] = "authenticated",
        ["GET /api/admin/identities"] = "ConsentRead",
        ["GET /api/admin/identities/{emailHash}"] = "ConsentRead",
        ["GET /api/admin/oidc/link"] = "authenticated",
        ["GET /api/admin/oidc/providers"] = "Admin",
        ["GET /api/admin/settings"] = "Admin",
        ["GET /api/admin/submissions"] = "SubmissionsRead",
        ["GET /api/admin/submissions/{id:guid}"] = "SubmissionsRead",
        ["GET /api/admin/users"] = "Admin",
        ["GET /api/admin/users/me"] = "authenticated",
        ["GET /api/admin/webhooks"] = "WebhooksRead",
        ["GET /api/admin/webhooks/buckets"] = "WebhooksRead",
        ["GET /api/bucket/{bucket}/records"] = "ConsentExport",
        ["GET /api/submission/{id:guid}/embed.js"] = "anonymous",
        ["GET /api/submission/{id:guid}/embed"] = "anonymous",
        ["GET /confirm/{token}"] = "none",
        ["GET /u/{token}"] = "none",
        ["PATCH /api/admin/api-keys/{id:guid}/dates"] = "Admin",
        ["PATCH /api/admin/api-keys/{id:guid}/enabled"] = "Admin",
        ["PATCH /api/admin/api-keys/{id:guid}/permissions"] = "Admin",
        ["PATCH /api/admin/users/{id:guid}/password"] = "authenticated",
        ["PATCH /api/admin/users/{id:guid}/role"] = "Admin",
        ["PATCH /api/admin/users/{id:guid}/username"] = "Admin",
        ["POST /api/admin/api-keys"] = "Admin",
        ["POST /api/admin/api-keys/{id:guid}/rotate"] = "Admin",
        ["POST /api/admin/assets/logo"] = "Admin",
        ["POST /api/admin/auth"] = "none",
        ["POST /api/admin/auth/logout"] = "none",
        ["POST /api/admin/auth/refresh"] = "authenticated",
        ["POST /api/admin/brand-identities"] = "Admin",
        ["POST /api/admin/buckets/{bucket}/archive"] = "BucketsWrite",
        ["POST /api/admin/buckets/{bucket}/check-email"] = "ConsentRead",
        ["POST /api/admin/buckets/{bucket}/override"] = "ConsentWrite",
        ["POST /api/admin/buckets/{bucket}/permissions"] = "BucketsWrite",
        ["POST /api/admin/buckets/{bucket}/records/export"] = "ConsentExport",
        ["POST /api/admin/buckets/{bucket}/tokens/export"] = "Admin",
        ["POST /api/admin/buckets/{bucket}/unarchive"] = "BucketsWrite",
        ["POST /api/admin/buckets/{bucket}/webhook"] = "Admin",
        ["POST /api/admin/buckets/{bucket}/webhook/secret"] = "Admin",
        ["POST /api/admin/data-policies/run"] = "Admin",
        ["POST /api/admin/data-policies/tasks/{id}/approve"] = "Admin",
        ["POST /api/admin/data-policies/tasks/{id}/reject"] = "Admin",
        ["POST /api/admin/identities/delete"] = "ConsentWrite",
        ["POST /api/admin/identities/export"] = "ConsentExport",
        ["POST /api/admin/oidc/link/{slug}"] = "authenticated",
        ["POST /api/admin/oidc/providers"] = "Admin",
        ["POST /api/admin/settings/object-storage/test"] = "Admin",
        ["POST /api/admin/submissions"] = "SubmissionsWrite",
        ["POST /api/admin/users"] = "Admin",
        ["POST /api/admin/users/{id:guid}/api-key"] = "authenticated",
        ["POST /api/consent/check"] = "ConsentRead",
        ["POST /api/consent/override"] = "ConsentWrite",
        ["POST /api/submission/{id:guid}/subscribe"] = "anonymous",
        ["POST /api/tokens/generate"] = "TokensWrite",
        ["POST /u/{token}"] = "none",
        ["PUT /api/admin/brand-identities/{id:int}"] = "Admin",
        ["PUT /api/admin/brand-identities/{id:int}/buckets"] = "Admin",
        ["PUT /api/admin/buckets/{bucket}/options"] = "BucketsWrite",
        ["PUT /api/admin/buckets/{bucket}/webhook/enabled"] = "Admin",
        ["PUT /api/admin/identities/{emailHash}/name"] = "ConsentWrite",
        ["PUT /api/admin/oidc/providers/{id:int}"] = "Admin",
        ["PUT /api/admin/settings"] = "Admin",
        ["PUT /api/admin/submissions/{id:guid}"] = "SubmissionsWrite",
    };

    [Fact]
    public void EveryEndpoint_HasTheApprovedPolicy()
    {
        var actual = MapAll();

        Assert.Empty(actual.Keys.Except(Expected.Keys));
        Assert.Empty(Expected.Keys.Except(actual.Keys));
        Assert.All(Expected, e => Assert.Equal((e.Key, e.Value), (e.Key, actual[e.Key])));
    }

    internal static Dictionary<string, string> MapAll()
    {
        var builder = WebApplication.CreateBuilder();
        foreach (var t in new[] { typeof(Beacon.Storage.BeaconDbContext).Assembly, typeof(Beacon.Core.Models.User).Assembly, typeof(AdminEndpoints).Assembly }
                     .SelectMany(a => a.GetTypes())
                     .Where(t => t.IsPublic && !t.IsGenericTypeDefinition && !t.IsEnum && !(t.IsAbstract && t.IsSealed) && !t.IsValueType
                         && (t.Namespace ?? "") is var n
                         && (n.StartsWith("Beacon.Storage") || n.StartsWith("Beacon.Core") || n.StartsWith("Beacon.Security")
                             || n.StartsWith("Beacon.Middleware") || (n.StartsWith("Beacon.Api") && t.Name.EndsWith("Service")))))
            builder.Services.AddScoped(t, _ => null!);

        var app = builder.Build();
        app.MapAdminEndpoints();
        app.MapConsentEndpoints();
        app.MapAuthEndpoints();
        app.MapUserEndpoints();
        app.MapApiKeyEndpoints();
        app.MapSubmissionEndpoints();
        app.MapOidcEndpoints();

        return ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
            .ToDictionary(
                e => $"{string.Join(",", e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])} {e.RoutePattern.RawText}",
                e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null ? "anonymous"
                    : e.Metadata.GetOrderedMetadata<IAuthorizeData>() is { Count: > 0 } auth ? string.Join("+", auth.Select(a => a.Policy ?? "authenticated"))
                    : "none");
    }
}
