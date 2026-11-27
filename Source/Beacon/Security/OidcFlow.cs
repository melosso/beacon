using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Beacon.Core.Models;
using Beacon.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Serilog;

namespace Beacon.Security;

public sealed record OidcIdentity(string Subject, string Username, string Email, bool EmailVerified);

public static class OidcFlow
{
    public const string Failed = "failed";
    public const string Denied = "denied";
    public const string NoAccount = "no_account";
    public const string Inactive = "inactive";
    public const string Linked = "linked";
    public const string NotLinked = "not_linked";

    public static readonly TimeSpan FlowLifetime = TimeSpan.FromMinutes(10);

    public sealed record Start(string AuthorizeUrl, string State);

    public static string MetadataAddress(string authority) =>
        $"{authority.TrimEnd('/')}/.well-known/openid-configuration";

    public static bool AllowsPlainHttp(string authority) =>
        Uri.TryCreate(authority, UriKind.Absolute, out var uri) && uri.IsLoopback;

    internal static (Start Start, OidcPendingFlow Flow) Begin(OpenIdConnectConfiguration document, OidcProvider provider, string redirectUri, DateTime now, Guid? linkUserId = null)
    {
        if (string.IsNullOrEmpty(document.AuthorizationEndpoint))
            throw new InvalidOperationException("The provider's discovery document declares no authorization endpoint.");

        var state = Token(32);
        var nonce = Token(32);
        var verifier = Token(64);
        var challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var flow = new OidcPendingFlow
        {
            State = state,
            ProviderId = provider.Id,
            Verifier = verifier,
            Nonce = nonce,
            RedirectUri = redirectUri,
            ExpiresAt = now.Add(FlowLifetime),
            LinkUserId = linkUserId
        };

        var query = new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = provider.ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = string.IsNullOrWhiteSpace(provider.Scopes) ? "openid profile email" : provider.Scopes.Trim(),
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256"
        };

        return (new Start(Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(document.AuthorizationEndpoint, query), state), flow);
    }

    internal static Task<int> PruneAsync(BeaconDbContext db, DateTime now, CancellationToken ct = default) =>
        db.OidcPendingFlows.Where(f => f.ExpiresAt <= now).ExecuteDeleteAsync(ct);

    public static async Task<OidcPendingFlow?> ClaimAsync(BeaconDbContext db, string? state, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(state) || state.Length > 64) return null;
        var flow = await db.OidcPendingFlows.AsNoTracking().FirstOrDefaultAsync(f => f.State == state, ct);
        if (flow is null) return null;
        if (await db.OidcPendingFlows.Where(f => f.State == state).ExecuteDeleteAsync(ct) != 1) return null;
        return flow.ExpiresAt <= DateTime.UtcNow ? null : flow;
    }

    private static string Token(int bytes) => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(bytes));
}

public sealed class OidcClient(bool allowPrivateNetwork) : IDisposable
{
    private readonly HttpClient _http = new(SsrfGuard.CreateGuardedHandler(allowPrivateNetwork)) { Timeout = TimeSpan.FromSeconds(15) };
    private readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _documents = new(StringComparer.Ordinal);

    public bool AllowPrivateNetwork { get; } = allowPrivateNetwork;

    public Task<OpenIdConnectConfiguration> DocumentAsync(OidcProvider provider, CancellationToken ct) =>
        _documents.GetOrAdd($"{provider.Id}|{provider.Authority}", _ => new ConfigurationManager<OpenIdConnectConfiguration>(
            OidcFlow.MetadataAddress(provider.Authority),
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(_http) { RequireHttps = !OidcFlow.AllowsPlainHttp(provider.Authority) })).GetConfigurationAsync(ct);

    public void Forget(int providerId)
    {
        foreach (var key in _documents.Keys)
            if (key.StartsWith(providerId + "|", StringComparison.Ordinal))
                _documents.TryRemove(key, out _);
    }

    public async Task<OidcFlow.Start> BeginAsync(BeaconDbContext db, OidcProvider provider, string redirectUri, Guid? linkUserId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var (start, flow) = OidcFlow.Begin(await DocumentAsync(provider, ct), provider, redirectUri, now, linkUserId);
        await OidcFlow.PruneAsync(db, now, ct);
        db.OidcPendingFlows.Add(flow);
        await db.SaveChangesAsync(ct);
        return start;
    }

    public async Task<OidcIdentity?> CompleteAsync(OidcProvider provider, string clientSecret, OidcPendingFlow flow, string code, CancellationToken ct)
    {
        var document = await DocumentAsync(provider, ct);
        if (string.IsNullOrEmpty(document.TokenEndpoint)) return null;

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = flow.RedirectUri,
            ["client_id"] = provider.ClientId,
            ["code_verifier"] = flow.Verifier
        };

        var basic = clientSecret.Length > 0 && PrefersBasic(document);
        if (clientSecret.Length > 0 && !basic) form["client_secret"] = clientSecret;

        using var request = new HttpRequestMessage(HttpMethod.Post, document.TokenEndpoint) { Content = new FormUrlEncodedContent(form) };
        if (basic)
        {
            var credentials = $"{Uri.EscapeDataString(provider.ClientId)}:{Uri.EscapeDataString(clientSecret)}";
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        }

        using var response = await _http.SendAsync(request, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("Token exchange with {Provider} failed with {Status}: {Body}",
                provider.Slug, (int)response.StatusCode, payload.Length <= 400 ? payload : payload[..400]);
            return null;
        }

        var idToken = (JsonNode.Parse(payload) as JsonObject)?["id_token"]?.GetValue<string>();
        if (string.IsNullOrEmpty(idToken))
        {
            Log.Warning("Provider {Provider} returned no id_token. Check that the openid scope is granted.", provider.Slug);
            return null;
        }

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuer = document.Issuer,
            ValidAudience = provider.ClientId,
            IssuerSigningKeys = document.SigningKeys,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        });

        if (!result.IsValid)
        {
            Log.Warning(result.Exception, "The id_token from {Provider} did not validate.", provider.Slug);
            return null;
        }

        if (Text(result.Claims, "nonce") != flow.Nonce)
        {
            Log.Warning("The id_token from {Provider} has the wrong nonce.", provider.Slug);
            return null;
        }

        var subject = Text(result.Claims, "sub");
        if (string.IsNullOrEmpty(subject)) return null;

        var verified = result.Claims.TryGetValue("email_verified", out var v) && v switch
        {
            bool flag => flag,
            string text => text.Equals("true", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

        return new OidcIdentity(subject, Text(result.Claims, provider.UsernameClaim), Text(result.Claims, provider.EmailClaim), verified);
    }

    public void Dispose() => _http.Dispose();

    private static bool PrefersBasic(OpenIdConnectConfiguration document)
    {
        var methods = document.TokenEndpointAuthMethodsSupported;
        if (methods is null || methods.Count == 0) return true;
        if (methods.Contains("client_secret_basic")) return true;
        return !methods.Contains("client_secret_post");
    }

    private static string Text(IDictionary<string, object> claims, string name) =>
        claims.TryGetValue(name, out var value) ? value as string ?? value.ToString() ?? "" : "";
}
