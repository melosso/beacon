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

        routes.MapGet("/api/admin/oidc/link", GetLink).RequireAuthorization().ExcludeFromDescription();
        routes.MapPost("/api/admin/oidc/link/{slug}", StartLink).RequireAuthorization().ExcludeFromDescription();
        routes.MapDelete("/api/admin/oidc/link", Unlink).RequireAuthorization().ExcludeFromDescription();

        routes.MapGet(AdminBase, ListProviders).RequireAuthorization("Admin").ExcludeFromDescription();
        routes.MapPost(AdminBase, CreateProvider).RequireAuthorization("Admin").ExcludeFromDescription();
        routes.MapPut($"{AdminBase}/{{id:int}}", UpdateProvider).RequireAuthorization("Admin").ExcludeFromDescription();
        routes.MapDelete($"{AdminBase}/{{id:int}}", DeleteProvider).RequireAuthorization("Admin").ExcludeFromDescription();
    }

    internal static bool Allowed(IOptionsMonitor<ApiKeyAuthOptions> options) =>
        options.Get(ApiKeyAuthExtensions.SchemeName).UserAuthentication is "user" or "both";

    private static async Task<IResult> ListSignInProviders([FromServices] BeaconDbContext db, [FromServices] IOptionsMonitor<ApiKeyAuthOptions> options, CancellationToken ct)
    {
        if (!Allowed(options)) return Results.Ok(Array.Empty<object>());
        var providers = await db.OidcProviders.AsNoTracking()
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.Id)
            .Select(p => new { slug = p.Slug, name = p.Name })
            .ToListAsync(ct);
        return Results.Ok(providers);
    }

    private static async Task<IResult> StartSignIn(string slug, HttpContext context, [FromServices] BeaconDbContext db, [FromServices] OidcClient oidc, [FromServices] IOptionsMonitor<ApiKeyAuthOptions> options, [FromServices] ILogger<Program> logger, CancellationToken ct)
    {
        var provider = await UsableAsync(db, options, slug, ct);
        if (provider is null) return Results.NotFound();

        try
        {
            var start = await oidc.BeginAsync(db, provider, RedirectUri(context, provider.Slug), null, ct);
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
        [FromServices] OidcClient oidc,
        [FromServices] Encryptor encryptor,
        [FromServices] IWebHostEnvironment env,
        [FromServices] IOptionsMonitor<ApiKeyAuthOptions> options,
        [FromServices] IOptionsMonitor<JwtAuthOptions> jwtOptions,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        var flow = await OidcFlow.ClaimAsync(db, state, ct);
        if (flow is null) return Results.Redirect(Back(OidcFlow.Failed));
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code)) return Results.Redirect(Back(OidcFlow.Denied));

        var provider = await UsableAsync(db, options, slug, ct);
        if (provider is null || provider.Id != flow.ProviderId) return Results.Redirect(Back(OidcFlow.Failed));

        OidcIdentity? identity;
        try
        {
            var secret = provider.EncryptedClientSecret.Length > 0 ? encryptor.Decrypt(provider.EncryptedClientSecret) : "";
            identity = await oidc.CompleteAsync(provider, secret, flow, code, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
        {
            logger.LogError(ex, "Could not complete the sign-in with {Provider}", provider.Slug);
            identity = null;
        }
        if (identity is null) return Results.Redirect(Back(OidcFlow.Failed));

        if (flow.LinkUserId is { } linkUserId)
        {
            var linked = await LinkAsync(db, provider, identity, linkUserId, ct);
            logger.LogInformation("Link through {Provider}: {Result}", provider.Slug, linked);
            return Results.Redirect($"/admin?view=settings&section=account&sso={linked}");
        }

        var (account, problem) = await ResolveAccountAsync(db, provider, identity, ct);
        if (account is null)
        {
            logger.LogWarning("Refused sign-in through {Provider} ({Problem})", provider.Slug, problem);
            return Results.Redirect(Back(problem));
        }

        account.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Sign-in as {Username} through {Provider}", account.Username, provider.Slug);

        var now = DateTimeOffset.UtcNow;
        AuthEndpoints.AppendSessionCookie(context, env, jwtOptions.Get(JwtAuthExtensions.SchemeName).SigningKey,
            account.Username, now.AddHours(1), account.Role, authTime: now);
        return Results.Redirect("/admin");
    }

    internal static async Task<string> LinkAsync(BeaconDbContext db, OidcProvider provider, OidcIdentity identity, Guid userId, CancellationToken ct = default)
    {
        var account = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (account is null || !account.IsEnabled) return OidcFlow.NotLinked;
        if (await db.Users.AnyAsync(u => u.Id != userId && u.Provider == provider.Slug && u.ExternalId == identity.Subject, ct))
            return OidcFlow.NotLinked;

        account.Provider = provider.Slug;
        account.ExternalId = identity.Subject;
        if (account.Email is null && identity.EmailVerified && identity.Email.Length > 0) account.Email = identity.Email;
        await db.SaveChangesAsync(ct);
        return OidcFlow.Linked;
    }

    internal sealed record PasswordRequest(string? CurrentPassword);

    private static async Task<User?> SignedInUserAsync(HttpContext context, BeaconDbContext db, CancellationToken ct)
    {
        var username = context.User.Identity?.Name;
        if (string.IsNullOrEmpty(username) || context.User.HasClaim(JwtAuthHandler.AdminKeyClaim, "true")) return null;
        var lowered = username.ToLower();
        return await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == lowered, ct);
    }

    private static async Task<IResult> GetLink(HttpContext context, [FromServices] BeaconDbContext db, [FromServices] IOptionsMonitor<ApiKeyAuthOptions> options, CancellationToken ct)
    {
        var user = await SignedInUserAsync(context, db, ct);
        if (user is null || !Allowed(options)) return Results.Ok(new { available = false, linked = (string?)null, providers = Array.Empty<object>() });
        var providers = await db.OidcProviders.AsNoTracking().Where(p => p.IsEnabled).OrderBy(p => p.Id)
            .Select(p => new { slug = p.Slug, name = p.Name }).ToListAsync(ct);
        var linked = user.Provider is null ? null : await db.OidcProviders.AsNoTracking()
            .Where(p => p.Slug == user.Provider).Select(p => p.Name).FirstOrDefaultAsync(ct) ?? user.Provider;
        return Results.Ok(new { available = true, linked, providers });
    }

    private static async Task<IResult> StartLink(
        string slug,
        [FromBody] PasswordRequest request,
        HttpContext context,
        [FromServices] BeaconDbContext db,
        [FromServices] OidcClient oidc,
        [FromServices] LoginLockout lockout,
        [FromServices] IOptionsMonitor<ApiKeyAuthOptions> options,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        var user = await SignedInUserAsync(context, db, ct);
        if (user is null) return Results.BadRequest(new { error = "Sign in with a user account to link a provider." });
        if (ConfirmPassword(user, request.CurrentPassword, lockout) is { } denied) return denied;

        var provider = await UsableAsync(db, options, slug, ct);
        if (provider is null) return Results.NotFound(new { error = "Provider not found." });

        try
        {
            var start = await oidc.BeginAsync(db, provider, RedirectUri(context, provider.Slug), user.Id, ct);
            return Results.Ok(new { authorizeUrl = start.AuthorizeUrl });
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
        {
            logger.LogError(ex, "Could not read the discovery document for {Provider}", provider.Slug);
            return Results.BadRequest(new { error = "The provider could not be reached. Check its issuer URL." });
        }
    }

    private static async Task<IResult> Unlink(
        [FromBody] PasswordRequest request,
        HttpContext context,
        [FromServices] BeaconDbContext db,
        [FromServices] LoginLockout lockout,
        CancellationToken ct)
    {
        var user = await SignedInUserAsync(context, db, ct);
        if (user is null) return Results.BadRequest(new { error = "Sign in with a user account to unlink a provider." });
        if (ConfirmPassword(user, request.CurrentPassword, lockout) is { } denied) return denied;

        user.Provider = null;
        user.ExternalId = null;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { linked = (string?)null });
    }

    private static IResult? ConfirmPassword(User user, string? password, LoginLockout lockout)
    {
        var key = $"link:{user.Id}";
        if (lockout.IsLocked(key, out _))
            return Results.Json(new { error = "Too many attempts. Wait a minute and retry." }, statusCode: 429);
        if (string.IsNullOrEmpty(password) || !PasswordHasher.VerifyPassword(password, user.PasswordHash, user.Salt))
        {
            lockout.RecordFailure(key);
            return Results.Json(new { error = "That password is incorrect." }, statusCode: 403);
        }
        lockout.Reset(key);
        return null;
    }

    internal static async Task<(User? Account, string Problem)> ResolveAccountAsync(BeaconDbContext db, OidcProvider provider, OidcIdentity identity, CancellationToken ct = default)
    {
        var account = await db.Users.FirstOrDefaultAsync(u => u.Provider == provider.Slug && u.ExternalId == identity.Subject, ct);

        if (account is null && identity.EmailVerified && identity.Email.Length > 0)
        {
            var email = identity.Email.ToLowerInvariant();
            account = await db.Users.FirstOrDefaultAsync(u =>
                (u.Provider == null || u.Provider == provider.Slug)
                && ((u.Email != null && u.Email.ToLower() == email) || u.Username.ToLower() == email), ct);
            if (account is not null)
            {
                account.Provider = provider.Slug;
                account.ExternalId = identity.Subject;
            }
        }

        if (account is null)
        {
            if (!provider.CreateAccounts) return (null, OidcFlow.NoAccount);
            account = await ProvisionAsync(db, provider, identity, ct);
        }

        return account.IsEnabled ? (account, "") : (null, OidcFlow.Inactive);
    }

    private static async Task<User> ProvisionAsync(BeaconDbContext db, OidcProvider provider, OidcIdentity identity, CancellationToken ct)
    {
        var baseName = UserEndpoints.ValidateUsername(identity.Username, out _) ? identity.Username
            : UserEndpoints.ValidateUsername(identity.Email, out _) ? identity.Email
            : "sso-" + UnsafeHandle().Replace(identity.Subject, "-").Trim('-');
        if (baseName.Length > 90) baseName = baseName[..90];

        var username = baseName;
        for (var n = 2; await db.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower(), ct); n++)
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

    private static async Task<IResult> ListProviders(HttpContext context, [FromServices] BeaconDbContext db, CancellationToken ct)
    {
        var providers = await db.OidcProviders.AsNoTracking().OrderBy(p => p.Id).ToListAsync(ct);
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

    private static async Task<IResult> CreateProvider([FromBody] ProviderRequest request, [FromServices] BeaconDbContext db, [FromServices] OidcClient oidc, [FromServices] Encryptor encryptor, CancellationToken ct)
    {
        var provider = new OidcProvider();
        if ((Apply(request, provider, encryptor, isNew: true) ?? await IssuerProblemAsync(oidc, provider.Authority, ct)) is { } problem)
            return Results.BadRequest(new { error = problem });
        if (await db.OidcProviders.AnyAsync(p => p.Slug == provider.Slug, ct))
            return Results.Conflict(new { error = "That key is already in use." });

        db.OidcProviders.Add(provider);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { id = provider.Id });
    }

    private static async Task<IResult> UpdateProvider(int id, [FromBody] ProviderRequest request, [FromServices] BeaconDbContext db, [FromServices] OidcClient oidc, [FromServices] Encryptor encryptor, CancellationToken ct)
    {
        var provider = await db.OidcProviders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (provider is null) return Results.NotFound();
        var oldSlug = provider.Slug;
        if ((Apply(request, provider, encryptor, isNew: false) ?? await IssuerProblemAsync(oidc, provider.Authority, ct)) is { } problem)
            return Results.BadRequest(new { error = problem });
        if (await db.OidcProviders.AnyAsync(p => p.Slug == provider.Slug && p.Id != id, ct))
            return Results.Conflict(new { error = "That key is already in use." });

        if (oldSlug != provider.Slug)
            await db.Users.Where(u => u.Provider == oldSlug).ExecuteUpdateAsync(s => s.SetProperty(u => u.Provider, provider.Slug), ct);
        await db.SaveChangesAsync(ct);
        oidc.Forget(provider.Id);
        return Results.Ok(new { id });
    }

    private static async Task<IResult> DeleteProvider(int id, [FromServices] BeaconDbContext db, [FromServices] OidcClient oidc, CancellationToken ct)
    {
        var provider = await db.OidcProviders.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (provider is null) return Results.NotFound();

        var unbound = await db.Users.Where(u => u.Provider == provider.Slug)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Provider, (string?)null).SetProperty(u => u.ExternalId, (string?)null), ct);
        db.OidcProviders.Remove(provider);
        await db.SaveChangesAsync(ct);
        oidc.Forget(provider.Id);
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

    internal static async Task<string?> IssuerProblemAsync(OidcClient oidc, string authority, CancellationToken ct = default) =>
        oidc.AllowPrivateNetwork || await SsrfGuard.ResolveAndValidateAsync(authority, ct) is not null
            ? null
            : "The issuer resolves to a private or reserved address. Set OIDC_ALLOW_PRIVATE_NETWORK=true to allow providers on your own network.";

    private static Task<OidcProvider?> UsableAsync(BeaconDbContext db, IOptionsMonitor<ApiKeyAuthOptions> options, string slug, CancellationToken ct) =>
        Allowed(options)
            ? db.OidcProviders.FirstOrDefaultAsync(p => p.Slug == slug && p.IsEnabled, ct)
            : Task.FromResult<OidcProvider?>(null);

    private static string Back(string reason) => $"/admin/login?sso={reason}";

    internal static string RedirectUri(HttpContext context, string slug) =>
        $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}{FlowBase}/{slug}/callback";

    [GeneratedRegex("^[a-z0-9-]{1,32}$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("[^a-zA-Z0-9._-]")]
    private static partial Regex UnsafeHandle();
}
