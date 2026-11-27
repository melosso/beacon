#!/usr/bin/env dotnet
#:sdk Microsoft.NET.Sdk.Web
#:property PublishAot=false

using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const string ClientId = "beacon";
const string ClientSecret = "beacon-dev-secret";
var port = int.TryParse(Environment.GetEnvironmentVariable("OIDC_STUB_PORT"), out var p) ? p : 9555;
var issuer = $"http://127.0.0.1:{port}";

(string Sub, string Email, string Name)[] identities =
[
    ("stub-analyst", "analyst@melosso.example", "Analyst (existing user)"),
    ("stub-operations", "operations@melosso.example", "Operations (existing admin)"),
    ("stub-newcomer", "newcomer@melosso.example", "Newcomer (no account)"),
];

using var rsa = RSA.Create(2048);
var key = rsa.ExportParameters(false);
var codes = new ConcurrentDictionary<string, (string Sub, string Email, string Nonce, string Challenge, string RedirectUri)>();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(issuer);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var app = builder.Build();

app.MapGet("/.well-known/openid-configuration", () => Results.Json(new Dictionary<string, object>
{
    ["issuer"] = issuer,
    ["authorization_endpoint"] = $"{issuer}/authorize",
    ["token_endpoint"] = $"{issuer}/token",
    ["jwks_uri"] = $"{issuer}/jwks",
    ["response_types_supported"] = new[] { "code" },
    ["subject_types_supported"] = new[] { "public" },
    ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
    ["code_challenge_methods_supported"] = new[] { "S256" },
    ["token_endpoint_auth_methods_supported"] = new[] { "client_secret_basic", "client_secret_post" },
}));

app.MapGet("/jwks", () => Results.Json(new
{
    keys = new[] { new { kty = "RSA", use = "sig", alg = "RS256", kid = "stub", n = B64(key.Modulus!), e = B64(key.Exponent!) } }
}));

app.MapGet("/authorize", (HttpRequest request) =>
{
    var q = request.Query;
    if (q["client_id"] != ClientId || q["response_type"] != "code" || q["code_challenge_method"] != "S256")
        return Results.BadRequest("Expected client_id=beacon, response_type=code and code_challenge_method=S256.");

    var hidden = string.Concat(new[] { "redirect_uri", "state", "nonce", "code_challenge" }
        .Select(n => $"<input type=\"hidden\" name=\"{n}\" value=\"{WebUtility.HtmlEncode(q[n])}\">"));
    var buttons = string.Concat(identities.Select(i =>
        $"<button name=\"sub\" value=\"{i.Sub}\">{WebUtility.HtmlEncode(i.Name)}<small>{i.Email}</small></button>"));
    return Results.Content($$"""
        <!doctype html><meta charset="utf-8"><title>OIDC stub</title>
        <style>body{font:15px system-ui;max-width:380px;margin:12vh auto}button{display:block;width:100%;margin:8px 0;padding:12px;text-align:left;border:1px solid #ccc;border-radius:8px;background:#fff;cursor:pointer}button:hover{border-color:#059669}small{display:block;color:#666}</style>
        <h1>Sign in to Beacon</h1><p>Development stub. Pick an identity.</p>
        <form method="post" action="/authorize">{{hidden}}{{buttons}}</form>
        """, "text/html");
});

app.MapPost("/authorize", async (HttpRequest request) =>
{
    var form = await request.ReadFormAsync();
    var identity = identities.FirstOrDefault(i => i.Sub == form["sub"]);
    var redirectUri = form["redirect_uri"].ToString();
    if (identity.Sub is null || !Uri.TryCreate(redirectUri, UriKind.Absolute, out _))
        return Results.BadRequest("Unknown identity or redirect_uri.");

    var code = B64(RandomNumberGenerator.GetBytes(24));
    codes[code] = (identity.Sub, identity.Email, form["nonce"].ToString(), form["code_challenge"].ToString(), redirectUri);
    var separator = redirectUri.Contains('?') ? '&' : '?';
    return Results.Redirect($"{redirectUri}{separator}code={code}&state={Uri.EscapeDataString(form["state"].ToString())}");
});

app.MapPost("/token", async (HttpRequest request) =>
{
    var form = await request.ReadFormAsync();
    var (id, secret) = ClientCredentials(request, form);
    if (id != ClientId || secret != ClientSecret)
        return Results.Json(new { error = "invalid_client" }, statusCode: 401);
    if (!codes.TryRemove(form["code"].ToString(), out var grant) || grant.RedirectUri != form["redirect_uri"])
        return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
    if (B64(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"].ToString()))) != grant.Challenge)
        return Results.Json(new { error = "invalid_grant", error_description = "PKCE verification failed" }, statusCode: 400);

    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var idToken = Sign(new Dictionary<string, object>
    {
        ["iss"] = issuer,
        ["aud"] = ClientId,
        ["sub"] = grant.Sub,
        ["email"] = grant.Email,
        ["email_verified"] = true,
        ["preferred_username"] = grant.Email,
        ["nonce"] = grant.Nonce,
        ["iat"] = now,
        ["exp"] = now + 300,
    });
    Console.WriteLine($"Signed in {grant.Email}");
    return Results.Json(new { access_token = B64(RandomNumberGenerator.GetBytes(24)), token_type = "Bearer", expires_in = 300, id_token = idToken });
});

Console.WriteLine($"""
    OIDC stub on {issuer}

    Add it to Beacon under Settings, Users, Sign-in providers:
      Issuer URL     {issuer}
      Client ID      {ClientId}
      Client secret  {ClientSecret}

    Beacon must run with ALLOW_PRIVATE_NETWORK=true to reach this address.
    Press Ctrl+C to stop.
    """);
app.Run();

string Sign(Dictionary<string, object> claims)
{
    var header = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT", kid = "stub" }));
    var payload = B64(JsonSerializer.SerializeToUtf8Bytes(claims));
    var signature = rsa.SignData(Encoding.ASCII.GetBytes($"{header}.{payload}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    return $"{header}.{payload}.{B64(signature)}";
}

static (string? Id, string? Secret) ClientCredentials(HttpRequest request, IFormCollection form)
{
    var auth = request.Headers.Authorization.ToString();
    if (auth.StartsWith("Basic ", StringComparison.Ordinal))
    {
        var pair = Encoding.UTF8.GetString(Convert.FromBase64String(auth[6..])).Split(':', 2);
        return (Uri.UnescapeDataString(pair[0]), pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : "");
    }
    return (form["client_id"], form["client_secret"]);
}

static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
