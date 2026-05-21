using System.Text;
using System.Text.Json;
using Beacon.Core.Security;
using Beacon.Security;
using Beacon.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Beacon.Tests;

// ── PasswordHasher ────────────────────────────────────────────────────────────

public class PasswordHasherTests
{
    [Fact]
    public void HashPassword_ReturnsNonEmptyHashAndSalt()
    {
        var (hash, salt) = PasswordHasher.HashPassword("correct-horse-battery-staple");

        Assert.NotEmpty(hash);
        Assert.NotEmpty(salt);
    }

    [Fact]
    public void HashPassword_ProducesDifferentSaltEachTime()
    {
        var (_, salt1) = PasswordHasher.HashPassword("same-password-12345");
        var (_, salt2) = PasswordHasher.HashPassword("same-password-12345");

        Assert.NotEqual(salt1, salt2);
    }

    [Fact]
    public void HashPassword_ProducesDifferentHashForSamePassword_DueToDifferentSalt()
    {
        var (hash1, _) = PasswordHasher.HashPassword("same-password-12345");
        var (hash2, _) = PasswordHasher.HashPassword("same-password-12345");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void HashPassword_OutputIsValidBase64()
    {
        var (hash, salt) = PasswordHasher.HashPassword("test-password-value");

        var hashBytes = Convert.FromBase64String(hash);
        var saltBytes = Convert.FromBase64String(salt);

        Assert.Equal(64, hashBytes.Length); // 64-byte output
        Assert.Equal(32, saltBytes.Length); // 32-byte salt
    }

    [Fact]
    public void VerifyPassword_ReturnsTrueForCorrectPassword()
    {
        const string password = "correct-horse-battery-staple";
        var (hash, salt) = PasswordHasher.HashPassword(password);

        Assert.True(PasswordHasher.VerifyPassword(password, hash, salt));
    }

    [Fact]
    public void VerifyPassword_ReturnsFalseForWrongPassword()
    {
        var (hash, salt) = PasswordHasher.HashPassword("correct-password-12345");

        Assert.False(PasswordHasher.VerifyPassword("wrong-password-12345", hash, salt));
    }

    [Fact]
    public void VerifyPassword_ReturnsFalseForEmptyPassword()
    {
        var (hash, salt) = PasswordHasher.HashPassword("actual-password-here!");

        Assert.False(PasswordHasher.VerifyPassword("", hash, salt));
    }

    [Fact]
    public void VerifyPassword_ReturnsFalseForWrongSalt()
    {
        const string password = "same-password-12345";
        var (hash, _) = PasswordHasher.HashPassword(password);
        var (_, differentSalt) = PasswordHasher.HashPassword("other-password-12345");

        Assert.False(PasswordHasher.VerifyPassword(password, hash, differentSalt));
    }

    [Fact]
    public void VerifyPassword_IsSymmetric_RoundTrip()
    {
        const string password = "round-trip-test-pw!";
        var (hash, salt) = PasswordHasher.HashPassword(password);

        Assert.True(PasswordHasher.VerifyPassword(password, hash, salt));
        Assert.False(PasswordHasher.VerifyPassword(password + "x", hash, salt));
    }
}

// ── ApiKeyGenerator ───────────────────────────────────────────────────────────

public class ApiKeyGeneratorTests
{
    [Fact]
    public void Generate_ReturnsNonEmptyKeyAndHash()
    {
        var (apiKey, apiKeyHash) = ApiKeyGenerator.Generate();

        Assert.NotEmpty(apiKey);
        Assert.NotEmpty(apiKeyHash);
    }

    [Fact]
    public void Generate_ProducesDifferentKeyEachTime()
    {
        var (key1, _) = ApiKeyGenerator.Generate();
        var (key2, _) = ApiKeyGenerator.Generate();

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void Generate_HashMatchesKey()
    {
        var (apiKey, apiKeyHash) = ApiKeyGenerator.Generate();

        Assert.Equal(apiKeyHash, ApiKeyGenerator.ComputeHash(apiKey));
    }

    [Fact]
    public void ComputeHash_IsDeterministic()
    {
        const string apiKey = "test-api-key-value";

        var hash1 = ApiKeyGenerator.ComputeHash(apiKey);
        var hash2 = ApiKeyGenerator.ComputeHash(apiKey);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_IsLowerHexString()
    {
        var (_, apiKeyHash) = ApiKeyGenerator.Generate();

        Assert.Equal(64, apiKeyHash.Length);
        Assert.True(apiKeyHash.All(c => char.IsAsciiHexDigitLower(c)));
    }

    [Fact]
    public void ComputeHash_DifferentKeysProduceDifferentHashes()
    {
        var hash1 = ApiKeyGenerator.ComputeHash("key-alpha-value-here");
        var hash2 = ApiKeyGenerator.ComputeHash("key-beta-value-here!");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Generate_KeyIsBase64UrlSafe()
    {
        // Repeat to reduce probability of missing edge cases
        for (int i = 0; i < 10; i++)
        {
            var (apiKey, _) = ApiKeyGenerator.Generate();
            Assert.DoesNotContain("+", apiKey);
            Assert.DoesNotContain("/", apiKey);
            Assert.DoesNotContain("=", apiKey);
        }
    }

    [Fact]
    public void Generate_KeyLengthIsConsistent()
    {
        var (key1, _) = ApiKeyGenerator.Generate();
        var (key2, _) = ApiKeyGenerator.Generate();

        // 32 bytes base64url-encoded = 43 chars
        Assert.Equal(key1.Length, key2.Length);
    }
}

// JwtAuthHandler 
public class JwtRoleTests
{
    // 32-byte signing key (base64-encoded for NormalizeKey compatibility)
    private static readonly byte[] TestSigningKey = Convert.FromBase64String(
        "dGVzdC1zaWduaW5nLWtleS1mb3ItdGVzdGluZy1vbmx5LWtleQ==");

    private static JsonElement DecodeJwtPayload(string jwt)
    {
        var parts = jwt.Split('.');
        var padded = parts[1].Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded
        };
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    [Fact]
    public void CreateToken_HasThreeParts()
    {
        var token = JwtAuthHandler.CreateToken(TestSigningKey, "user", DateTimeOffset.UtcNow.AddHours(1));

        Assert.Equal(3, token.Split('.').Length);
    }

    [Fact]
    public void CreateToken_DefaultsToAdminRole()
    {
        var token = JwtAuthHandler.CreateToken(TestSigningKey, "alice", DateTimeOffset.UtcNow.AddHours(1));

        var payload = DecodeJwtPayload(token);
        Assert.Equal("admin", payload.GetProperty("role").GetString());
    }

    [Fact]
    public void CreateToken_IncludesAdminRoleInPayload()
    {
        var token = JwtAuthHandler.CreateToken(TestSigningKey, "alice", DateTimeOffset.UtcNow.AddHours(1), "admin");

        var payload = DecodeJwtPayload(token);
        Assert.Equal("admin", payload.GetProperty("role").GetString());
    }

    [Fact]
    public void CreateToken_IncludesUserRoleInPayload()
    {
        var token = JwtAuthHandler.CreateToken(TestSigningKey, "bob", DateTimeOffset.UtcNow.AddHours(1), "user");

        var payload = DecodeJwtPayload(token);
        Assert.Equal("user", payload.GetProperty("role").GetString());
    }

    [Fact]
    public void CreateToken_IncludesSubjectInPayload()
    {
        var token = JwtAuthHandler.CreateToken(TestSigningKey, "john", DateTimeOffset.UtcNow.AddHours(1), "admin");

        var payload = DecodeJwtPayload(token);
        Assert.Equal("john", payload.GetProperty("sub").GetString());
    }

    [Fact]
    public void CreateToken_IncludesIatAndExp()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var token = JwtAuthHandler.CreateToken(TestSigningKey, "alice", expiresAt, "admin");

        var payload = DecodeJwtPayload(token);
        var exp = payload.GetProperty("exp").GetInt64();
        var iat = payload.GetProperty("iat").GetInt64();

        Assert.True(exp > iat);
        Assert.Equal(expiresAt.ToUnixTimeSeconds(), exp);
    }

    [Fact]
    public void CreateToken_SubjectAndRoleAreBothPresent()
    {
        var token = JwtAuthHandler.CreateToken(TestSigningKey, "charlie", DateTimeOffset.UtcNow.AddHours(1), "user");

        var payload = DecodeJwtPayload(token);
        Assert.Equal("charlie", payload.GetProperty("sub").GetString());
        Assert.Equal("user", payload.GetProperty("role").GetString());
    }
}

// ── UserRepository ────────────────────────────────────────────────────────────

public class UserRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BeaconDbContext _db;
    private readonly UserRepository _repository;

    public UserRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BeaconDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new BeaconDbContext(options);
        _db.Database.EnsureCreated();
        _repository = new UserRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<Beacon.Core.Models.User> SeedUserAsync(string username = "testuser", string role = "admin")
    {
        var (hash, salt) = PasswordHasher.HashPassword("seeded-password-12!");
        var (_, keyHash) = ApiKeyGenerator.Generate();
        return await _repository.CreateAsync(username, hash, salt, role, keyHash);
    }

    // ── CreateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_StoresUser_InDatabase()
    {
        await SeedUserAsync();

        Assert.Equal(1, await _db.Users.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_SetsIsEnabledTrue()
    {
        var user = await SeedUserAsync();

        Assert.True(user.IsEnabled);
    }

    [Fact]
    public async Task CreateAsync_SetsCreatedAt()
    {
        var before = DateTime.UtcNow;
        var user = await SeedUserAsync();
        var after = DateTime.UtcNow;

        Assert.InRange(user.CreatedAt, before, after);
    }

    [Fact]
    public async Task CreateAsync_AssignsNewGuid()
    {
        var user = await SeedUserAsync();

        Assert.NotEqual(Guid.Empty, user.Id);
    }

    [Fact]
    public async Task CreateAsync_StoresUsername_AndRole()
    {
        var user = await SeedUserAsync("alice", "user");

        Assert.Equal("alice", user.Username);
        Assert.Equal("user", user.Role);
    }

    [Fact]
    public async Task CreateAsync_TwoUsers_HaveDifferentIds()
    {
        var u1 = await SeedUserAsync("alpha");
        var u2 = await SeedUserAsync("beta");

        Assert.NotEqual(u1.Id, u2.Id);
    }

    // ── FindByUsernameAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task FindByUsernameAsync_ReturnsUser_WhenExists()
    {
        await SeedUserAsync("alice");

        var result = await _repository.FindByUsernameAsync("alice");

        Assert.NotNull(result);
        Assert.Equal("alice", result.Username);
    }

    [Fact]
    public async Task FindByUsernameAsync_IsCaseInsensitive()
    {
        await SeedUserAsync("Alice");

        Assert.NotNull(await _repository.FindByUsernameAsync("alice"));
        Assert.NotNull(await _repository.FindByUsernameAsync("ALICE"));
        Assert.NotNull(await _repository.FindByUsernameAsync("AlIcE"));
    }

    [Fact]
    public async Task FindByUsernameAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _repository.FindByUsernameAsync("nobody");

        Assert.Null(result);
    }

    [Fact]
    public async Task FindByUsernameAsync_DoesNotReturnOtherUsers()
    {
        await SeedUserAsync("alice");
        await SeedUserAsync("bob");

        var result = await _repository.FindByUsernameAsync("alice");

        Assert.Equal("alice", result!.Username);
    }

    // ── FindByApiKeyHashAsync ────────────────────────────────────────────────

    [Fact]
    public async Task FindByApiKeyHashAsync_ReturnsUser_WhenHashMatches()
    {
        var (hash, salt) = PasswordHasher.HashPassword("test-pwd-12345!");
        var (_, keyHash) = ApiKeyGenerator.Generate();
        await _repository.CreateAsync("apiuser", hash, salt, "admin", keyHash);

        var result = await _repository.FindByApiKeyHashAsync(keyHash);

        Assert.NotNull(result);
        Assert.Equal("apiuser", result.Username);
    }

    [Fact]
    public async Task FindByApiKeyHashAsync_ReturnsNull_WhenNoMatch()
    {
        await SeedUserAsync();

        var result = await _repository.FindByApiKeyHashAsync("0000000000000000000000000000000000000000000000000000000000000000");

        Assert.Null(result);
    }

    // ── CountAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CountAsync_ReturnsZero_WhenEmpty()
    {
        Assert.Equal(0, await _repository.CountAsync());
    }

    [Fact]
    public async Task CountAsync_ReturnsCorrectCount()
    {
        await SeedUserAsync("u1");
        await SeedUserAsync("u2");
        await SeedUserAsync("u3");

        Assert.Equal(3, await _repository.CountAsync());
    }

    // ── GetAllAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsEmpty_WhenNoUsers()
    {
        var result = await _repository.GetAllAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllUsers()
    {
        await SeedUserAsync("u1");
        await SeedUserAsync("u2");

        var result = await _repository.GetAllAsync();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetAllAsync_OrdersByCreatedAtDescending()
    {
        var u1 = await SeedUserAsync("first");
        await Task.Delay(10); // ensure distinct CreatedAt
        var u2 = await SeedUserAsync("second");

        var result = await _repository.GetAllAsync();

        Assert.Equal("second", result[0].Username);
        Assert.Equal("first", result[1].Username);
    }

    // ── UpdatePasswordAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdatePasswordAsync_UpdatesHashAndSalt()
    {
        var user = await SeedUserAsync();
        var originalHash = user.PasswordHash;
        var (newHash, newSalt) = PasswordHasher.HashPassword("new-password-12345!");

        await _repository.UpdatePasswordAsync(user.Id, newHash, newSalt);

        var updated = await _db.Users.FindAsync(user.Id);
        Assert.Equal(newHash, updated!.PasswordHash);
        Assert.Equal(newSalt, updated.Salt);
        Assert.NotEqual(originalHash, updated.PasswordHash);
    }

    [Fact]
    public async Task UpdatePasswordAsync_SetsUpdatedAt()
    {
        var user = await SeedUserAsync();
        var (newHash, newSalt) = PasswordHasher.HashPassword("new-password-12345!");
        var before = DateTime.UtcNow;

        await _repository.UpdatePasswordAsync(user.Id, newHash, newSalt);
        var after = DateTime.UtcNow;

        var updated = await _db.Users.FindAsync(user.Id);
        Assert.NotNull(updated!.UpdatedAt);
        Assert.InRange(updated.UpdatedAt!.Value, before, after);
    }

    [Fact]
    public async Task UpdatePasswordAsync_DoesNothing_WhenUserNotFound()
    {
        await _repository.UpdatePasswordAsync(Guid.NewGuid(), "hash", "salt"); // must not throw
    }

    // ── UpdateApiKeyAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateApiKeyAsync_UpdatesHash()
    {
        var user = await SeedUserAsync();
        var (_, newKeyHash) = ApiKeyGenerator.Generate();

        await _repository.UpdateApiKeyAsync(user.Id, newKeyHash);

        var updated = await _db.Users.FindAsync(user.Id);
        Assert.Equal(newKeyHash, updated!.ApiKeyHash);
    }

    [Fact]
    public async Task UpdateApiKeyAsync_SetsUpdatedAt()
    {
        var user = await SeedUserAsync();
        var (_, newKeyHash) = ApiKeyGenerator.Generate();
        var before = DateTime.UtcNow;

        await _repository.UpdateApiKeyAsync(user.Id, newKeyHash);
        var after = DateTime.UtcNow;

        var updated = await _db.Users.FindAsync(user.Id);
        Assert.NotNull(updated!.UpdatedAt);
        Assert.InRange(updated.UpdatedAt!.Value, before, after);
    }

    // ── UpdateRoleAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRoleAsync_ChangesRole_AdminToUser()
    {
        var user = await SeedUserAsync(role: "admin");

        await _repository.UpdateRoleAsync(user.Id, "user");

        var updated = await _db.Users.FindAsync(user.Id);
        Assert.Equal("user", updated!.Role);
    }

    [Fact]
    public async Task UpdateRoleAsync_ChangesRole_UserToAdmin()
    {
        var user = await SeedUserAsync(role: "user");

        await _repository.UpdateRoleAsync(user.Id, "admin");

        var updated = await _db.Users.FindAsync(user.Id);
        Assert.Equal("admin", updated!.Role);
    }

    // ── SetLastLoginAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task SetLastLoginAsync_SetsLastLoginAt()
    {
        var user = await SeedUserAsync();
        Assert.Null(user.LastLoginAt);

        var before = DateTime.UtcNow;
        await _repository.SetLastLoginAsync(user.Id);
        var after = DateTime.UtcNow;

        var updated = await _db.Users.FindAsync(user.Id);
        Assert.NotNull(updated!.LastLoginAt);
        Assert.InRange(updated.LastLoginAt!.Value, before, after);
    }

    [Fact]
    public async Task SetLastLoginAsync_UpdatesLastLoginAt_OnSubsequentCalls()
    {
        var user = await SeedUserAsync();
        await _repository.SetLastLoginAsync(user.Id);
        var firstLogin = (await _db.Users.FindAsync(user.Id))!.LastLoginAt;

        await Task.Delay(10);
        await _repository.SetLastLoginAsync(user.Id);
        var secondLogin = (await _db.Users.FindAsync(user.Id))!.LastLoginAt;

        Assert.True(secondLogin >= firstLogin);
    }

    // ── DeleteAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesUser()
    {
        var user = await SeedUserAsync();

        await _repository.DeleteAsync(user.Id);

        Assert.Equal(0, await _db.Users.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_OnlyRemovesTargetUser()
    {
        var u1 = await SeedUserAsync("keep");
        var u2 = await SeedUserAsync("remove");

        await _repository.DeleteAsync(u2.Id);

        Assert.Equal(1, await _db.Users.CountAsync());
        Assert.NotNull(await _db.Users.FindAsync(u1.Id));
    }

    [Fact]
    public async Task DeleteAsync_IsIdempotent_WhenUserNotFound()
    {
        await _repository.DeleteAsync(Guid.NewGuid()); // must not throw
    }
}
