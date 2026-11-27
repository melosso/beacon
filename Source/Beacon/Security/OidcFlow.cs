using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Beacon.Core.Models;
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

    public static readonly TimeSpan FlowLifetime = TimeSpan.FromMinutes(10);

    internal const int PruneThreshold = 1024;

    private static readonly ConcurrentDictionary<string, PendingFlow> Pending = new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> Documents = new(StringComparer.Ordinal);

    public sealed record PendingFlow(int ProviderId, string Verifier, string Nonce, string RedirectUri, DateTime ExpiresAt);

    public sealed record Start(string AuthorizeUrl, string State);

    public static string MetadataAddress(string authority) =>
        $"{authority.TrimEnd('/')}/.well-known/openid-configuration";

    public static bool AllowsPlainHttp(string authority) =>
        Uri.TryCreate(authority, UriKind.Absolute, out var uri) && uri.IsLoopback;

    public static Task<OpenIdConnectConfiguration> DocumentAsync(OidcProvider provider, CancellationToken token) =>
        Documents.GetOrAdd($"{provider.Id}|{provider.Authority}", _ => new ConfigurationManager<OpenIdConnectConfiguration>(
            MetadataAddress(provider.Authority),
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = !AllowsPlainHttp(provider.Authority) })).GetConfigurationAsync(token);

    public static void Forget(int providerId)
    {
        foreach (var key in Documents.Keys)
            if (key.StartsWith(providerId + "|", StringComparison.Ordinal))
                Documents.TryRemove(key, out _);
    }

    public static async Task<Start> BeginAsync(OidcProvider provider, string redirectUri, CancellationToken token) =>
        Begin(await DocumentAsync(provider, token), provider, redirectUri, DateTime.UtcNow);

    internal static Start Begin(OpenIdConnectConfiguration document, OidcProvider provider, string redirectUri, DateTime now)
    {
        if (string.IsNullOrEmpty(document.AuthorizationEndpoint))
            throw new InvalidOperationException("The provider's discovery document declares no authorization endpoint.");

        var state = Token(32);
        var nonce = Token(32);
        var verifier = Token(64);
        var challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        if (Pending.Count >= PruneThreshold)
            Prune(now);

        Pending[state] = new PendingFlow(provider.Id, verifier, nonce, redirectUri, now.Add(FlowLifetime));

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

        return new Start(Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(document.AuthorizationEndpoint, query), state);
    }

    public static PendingFlow? Claim(string? state)
    {
        if (string.IsNullOrEmpty(state) || !Pending.TryRemove(state, out var flow)) return null;
        return flow.ExpiresAt <= DateTime.UtcNow ? null : flow;
    }

    public static async Task<OidcIdentity?> CompleteAsync(
        OidcProvider provider, string clientSecret, PendingFlow flow, string code, HttpClient http, CancellationToken token)
    {
        var document = await DocumentAsync(provider, token);
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

        using var response = await http.SendAsync(request, token);
        var payload = await response.Content.ReadAsStringAsync(token);

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

    private static bool PrefersBasic(OpenIdConnectConfiguration document)
    {
        var methods = document.TokenEndpointAuthMethodsSupported;
        if (methods is null || methods.Count == 0) return true;
        if (methods.Contains("client_secret_basic")) return true;
        return !methods.Contains("client_secret_post");
    }

    public static int Prune(DateTime now)
    {
        var removed = 0;
        foreach (var (key, flow) in Pending)
            if (flow.ExpiresAt <= now && Pending.TryRemove(key, out _)) removed++;
        return removed;
    }

    private static string Token(int bytes) => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(bytes));

    private static string Text(IDictionary<string, object> claims, string name) =>
        claims.TryGetValue(name, out var value) ? value as string ?? value.ToString() ?? "" : "";

    internal static bool IsPending(string state) => Pending.ContainsKey(state);
}
