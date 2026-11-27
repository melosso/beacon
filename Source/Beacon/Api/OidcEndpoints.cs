using System.Text.RegularExpressions;
using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Security;
using Beacon.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Beacon.Api;

public static partial class OidcEndpoints
{
    private const string FlowBase = "/api/admin/auth/oidc";
    private const string AdminBase = "/api/admin/oidc/providers";
    private static readonly string[] Roles = ["admin", "user"];

    public static void MapOidcEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/admin/auth/providers", ListSignInProviders).ExcludeFromDescription();
        routes.MapGet($"{FlowBase}/{{slug}}/start", StartSignIn).ExcludeFromDescription();
        routes.MapGet($"{FlowBase}/{{slug}}/callback", CompleteSignIn).ExcludeFromDescription();

        routes.MapGet(AdminBase, ListProviders).RequireAuthorization("Admin").ExcludeFromDescription();
        routes.MapPost(AdminBase, CreateProvider).RequireAuthorization("Admin").ExcludeFromDescription();
        routes.MapPut($"{AdminBase}/{{id:int}}", UpdateProvider).RequireAuthorization("Admin").ExcludeFromDescription();
        routes.MapDelete($"{AdminBase}/{{id:int}}", DeleteProvider).RequireAuthorization("Admin").ExcludeFromDescription();
    }

    internal static bool Allowed(IOptionsMonitor<ApiKeyAuthOptions> options) =>
        options.Get(ApiKeyAuthExtensions.SchemeName).UserAuthentication is "user" or "both";

    private static async Task<IResult> ListSignInProviders([FromServices] BeaconDbContext db, [FromServices] IOptionsMonitor<ApiKeyAuthOptions> options)
    {
        if (!Allowed(options)) return Results.Ok(Array.Empty<object>());
        var providers = await db.OidcProviders.AsNoTracking()
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.Id)
            .Select(p => new { slug = p.Slug, name = p.Name })
            .ToListAsync();
        return Results.Ok(providers);
    }

    private static async Task<IResult> StartSignIn(string slug, HttpContext context, [FromServices] BeaconDbContext db, [FromServices] IOptionsMonitor<ApiKeyAuthOptions> options, [FromServices] ILogger<Program> logger)
    {
        var provider = await UsableAsync(db, options, slug);
        if (provider is null) return Results.NotFound();

        try
        {
            var start = await OidcFlow.BeginAsync(provider, RedirectUri(context, provider.Slug), context.RequestAborted);
            return Results.Redirect(start.AuthorizeUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
        {
            logger.LogError(ex, "Could not read the discovery document for {Provider}", provider.Slug);
            return Results.Redirect(Back(OidcFlow.Failed));
        }
    }

    private static async Task<IResult> CompleteSignIn(
        string slug,
        string? code,
        string? state,
        string? error,
        HttpContext context,
        [FromServices] BeaconDbContext db,
        [FromServices] Encryptor encryptor,
        [FromServices] IHttpClientFactory clients,
        [FromServices] IWebHostEnvironment env,
        [FromServices] IOptionsMonitor<ApiKeyAuthOptions> options,
        [FromServices] IOptionsMonitor<JwtAuthOptions> jwtOptions,
        [FromServices] ILogger<Program> logger)
    {
        var flow = OidcFlow.Claim(state);
        if (flow is null) return Results.Redirect(Back(OidcFlow.Failed));
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code)) return Results.Redirect(Back(OidcFlow.Denied));

        var provider = await UsableAsync(db, options, slug);
        if (provider is null || provider.Id != flow.ProviderId) return Results.Redirect(Back(OidcFlow.Failed));

        OidcIdentity? identity;
        try
        {
            var secret = provider.EncryptedClientSecret.Length > 0 ? encryptor.Decrypt(provider.EncryptedClientSecret) : "";
            identity = await OidcFlow.CompleteAsync(provider, secret, flow, code, clients.CreateClient(), context.RequestAborted);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
        {
            logger.LogError(ex, "Could not complete the sign-in with {Provider}", provider.Slug);
            identity = null;
        }
        if (identity is null) return Results.Redirect(Back(OidcFlow.Failed));

        var (account, problem) = await ResolveAccountAsync(db, provider, identity);
        if (account is null)
        {
            logger.LogWarning("Refused sign-in through {Provider} ({Problem})", provider.Slug, problem);
            return Results.Redirect(Back(problem));
        }

        account.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        logger.LogInformation("Sign-in as {Username} through {Provider}", account.Username, provider.Slug);

        var now = DateTimeOffset.UtcNow;
        AuthEndpoints.AppendSessionCookie(context, env, jwtOptions.Get(JwtAuthExtensions.SchemeName).SigningKey,
            account.Username, now.AddHours(1), account.Role, authTime: now);
        return Results.Redirect("/admin");
    }

    internal static async Task<(User? Account, string Problem)> ResolveAccountAsync(BeaconDbContext db, OidcProvider provider, OidcIdentity identity)
    {
        var account = await db.Users.FirstOrDefaultAsync(u => u.Provider == provider.Slug && u.ExternalId == identity.Subject);

        if (account is null && identity.EmailVerified && identity.Email.Length > 0)
        {
            var email = identity.Email.ToLowerInvariant();
            account = await db.Users.FirstOrDefaultAsync(u =>
                (u.Provider == null || u.Provider == provider.Slug)
                && ((u.Email != null && u.Email.ToLower() == email) || u.Username.ToLower() == email));
            if (account is not null)
            {
                account.Provider = provider.Slug;
                account.ExternalId = identity.Subject;
            }
        }

        if (account is null)
        {
            if (!provider.CreateAccounts) return (null, OidcFlow.NoAccount);
            account = await ProvisionAsync(db, provider, identity);
        }

        return account.IsEnabled ? (account, "") : (null, OidcFlow.Inactive);
    }

    private static async Task<User> ProvisionAsync(BeaconDbContext db, OidcProvider provider, OidcIdentity identity)
    {
        var baseName = UserEndpoints.ValidateUsername(identity.Username, out _) ? identity.Username
            : UserEndpoints.ValidateUsername(identity.Email, out _) ? identity.Email
            : "sso-" + UnsafeHandle().Replace(identity.Subject, "-").Trim('-');
        if (baseName.Length > 90) baseName = baseName[..90];

        var username = baseName;
        for (var n = 2; await db.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower()); n++)
            username = $"{baseName}-{n}";

        var (hash, salt) = PasswordHasher.HashPassword(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        var (_, apiKeyHash) = ApiKeyGenerator.Generate();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = hash,
            Salt = salt,
            Role = provider.CreatedRole,
            ApiKeyHash = apiKeyHash,
            CreatedAt = DateTime.UtcNow,
            Provider = provider.Slug,
            ExternalId = identity.Subject,
            Email = identity.EmailVerified && identity.Email.Length > 0 ? identity.Email : null
        };
        db.Users.Add(user);
        return user;
    }

    internal sealed record ProviderRequest(
        string? Slug, string? Name, string? Authority, string? ClientId, string? ClientSecret, string? Scopes,
        string? UsernameClaim, string? EmailClaim, bool? IsEnabled, bool? CreateAccounts, string? CreatedRole);

    private static async Task<IResult> ListProviders(HttpContext context, [FromServices] BeaconDbContext db)
    {
        var providers = await db.OidcProviders.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
        return Results.Ok(providers.Select(p => new
        {
            id = p.Id,
            slug = p.Slug,
            name = p.Name,
            authority = p.Authority,
            clientId = p.ClientId,
            hasClientSecret = p.EncryptedClientSecret.Length > 0,
            scopes = p.Scopes,
            usernameClaim = p.UsernameClaim,
            emailClaim = p.EmailClaim,
            isEnabled = p.IsEnabled,
            createAccounts = p.CreateAccounts,
            createdRole = p.CreatedRole,
            redirectUri = RedirectUri(context, p.Slug)
        }));
    }

    private static async Task<IResult> CreateProvider([FromBody] ProviderRequest request, [FromServices] BeaconDbContext db, [FromServices] Encryptor encryptor)
    {
        var provider = new OidcProvider();
        if (Apply(request, provider, encryptor, isNew: true) is { } problem) return Results.BadRequest(new { error = problem });
        if (await db.OidcProviders.AnyAsync(p => p.Slug == provider.Slug))
            return Results.Conflict(new { error = "That key is already in use." });

        db.OidcProviders.Add(provider);
        await db.SaveChangesAsync();
        return Results.Ok(new { id = provider.Id });
    }

    private static async Task<IResult> UpdateProvider(int id, [FromBody] ProviderRequest request, [FromServices] BeaconDbContext db, [FromServices] Encryptor encryptor)
    {
        var provider = await db.OidcProviders.FirstOrDefaultAsync(p => p.Id == id);
        if (provider is null) return Results.NotFound();
        var oldSlug = provider.Slug;
        if (Apply(request, provider, encryptor, isNew: false) is { } problem) return Results.BadRequest(new { error = problem });
        if (await db.OidcProviders.AnyAsync(p => p.Slug == provider.Slug && p.Id != id))
            return Results.Conflict(new { error = "That key is already in use." });

        if (oldSlug != provider.Slug)
            await db.Users.Where(u => u.Provider == oldSlug).ExecuteUpdateAsync(s => s.SetProperty(u => u.Provider, provider.Slug));
        await db.SaveChangesAsync();
        OidcFlow.Forget(provider.Id);
        return Results.Ok(new { id });
    }

    private static async Task<IResult> DeleteProvider(int id, [FromServices] BeaconDbContext db)
    {
        var provider = await db.OidcProviders.FirstOrDefaultAsync(p => p.Id == id);
        if (provider is null) return Results.NotFound();

        var unbound = await db.Users.Where(u => u.Provider == provider.Slug)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Provider, (string?)null).SetProperty(u => u.ExternalId, (string?)null));
        db.OidcProviders.Remove(provider);
        await db.SaveChangesAsync();
        OidcFlow.Forget(provider.Id);
        return Results.Ok(new { unboundAccounts = unbound });
    }

    internal static string? Apply(ProviderRequest request, OidcProvider provider, Encryptor encryptor, bool isNew)
    {
        if (request.Slug is { } slug)
        {
            if (!SlugPattern().IsMatch(slug)) return "A key may use lowercase letters, numbers and hyphens, up to 32 characters.";
            provider.Slug = slug;
        }
        else if (isNew) return "A key is required.";

        if (request.Name is { } name) provider.Name = name.Trim();
        if (string.IsNullOrWhiteSpace(provider.Name) || provider.Name.Length > 100) return "A name of up to 100 characters is required.";

        if (request.Authority is { } authority)
        {
            if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || authority.Length > 500)
                return "The issuer must be an absolute https URL.";
            if (uri.Scheme == "http" && !OidcFlow.AllowsPlainHttp(authority))
                return "Only a provider on loopback may use plain http.";
            provider.Authority = authority.TrimEnd('/');
        }
        else if (isNew) return "An issuer URL is required.";

        if (request.ClientId is { } clientId) provider.ClientId = clientId.Trim();
        if (string.IsNullOrWhiteSpace(provider.ClientId) || provider.ClientId.Length > 255) return "A client ID is required.";

        if (request.ClientSecret is { } secret) provider.EncryptedClientSecret = secret.Length == 0 ? "" : encryptor.Encrypt(secret);

        if (request.Scopes is { } scopes && scopes.Trim().Length > 0)
        {
            if (!scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("openid")) return "Scopes must include openid.";
            provider.Scopes = scopes.Trim();
        }
        if (request.UsernameClaim is { } usernameClaim && usernameClaim.Trim().Length > 0) provider.UsernameClaim = usernameClaim.Trim();
        if (request.EmailClaim is { } emailClaim && emailClaim.Trim().Length > 0) provider.EmailClaim = emailClaim.Trim();

        if (request.CreatedRole is { } role)
        {
            if (!Roles.Contains(role)) return "Role must be 'admin' or 'user'.";
            provider.CreatedRole = role;
        }

        if (request.IsEnabled is { } enabled) provider.IsEnabled = enabled;
        if (request.CreateAccounts is { } create) provider.CreateAccounts = create;
        return null;
    }

    private static Task<OidcProvider?> UsableAsync(BeaconDbContext db, IOptionsMonitor<ApiKeyAuthOptions> options, string slug) =>
        Allowed(options)
            ? db.OidcProviders.FirstOrDefaultAsync(p => p.Slug == slug && p.IsEnabled)
            : Task.FromResult<OidcProvider?>(null);

    private static string Back(string reason) => $"/admin/login?sso={reason}";

    internal static string RedirectUri(HttpContext context, string slug) =>
        $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}{FlowBase}/{slug}/callback";

    [GeneratedRegex("^[a-z0-9-]{1,32}$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("[^a-zA-Z0-9._-]")]
    private static partial Regex UnsafeHandle();
}
