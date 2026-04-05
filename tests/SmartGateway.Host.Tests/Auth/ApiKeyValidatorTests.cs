using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SmartGateway.Core.Data;
using SmartGateway.Core.Entities;
using SmartGateway.Host.Auth;

namespace SmartGateway.Host.Tests.Auth;

public class ApiKeyValidatorTests : IDisposable
{
    private readonly SmartGatewayDbContext _context;
    private readonly ApiKeyValidator _validator;

    public ApiKeyValidatorTests()
    {
        var options = new DbContextOptionsBuilder<SmartGatewayDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new SmartGatewayDbContext(options);

        var factory = NSubstitute.Substitute.For<IDbContextFactory<SmartGatewayDbContext>>();
        factory.CreateDbContext().Returns(_ => new SmartGatewayDbContext(options));
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(_ => new SmartGatewayDbContext(options));

        _validator = new ApiKeyValidator(factory);
    }

    [Fact]
    public async Task Validate_ShouldReturnTrue_WhenKeyExists()
    {
        var rawKey = "test-api-key-12345";
        var hash = HashKey(rawKey);

        _context.ApiKeys.Add(new GatewayApiKey { KeyHash = hash, Name = "Test Key", IsActive = true });
        await _context.SaveChangesAsync();

        var result = await _validator.ValidateAsync(rawKey);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ShouldReturnFalse_WhenKeyNotFound()
    {
        var result = await _validator.ValidateAsync("nonexistent-key");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ShouldReturnFalse_WhenKeyRevoked()
    {
        var rawKey = "revoked-key-12345";
        var hash = HashKey(rawKey);

        _context.ApiKeys.Add(new GatewayApiKey { KeyHash = hash, Name = "Revoked", IsActive = false });
        await _context.SaveChangesAsync();

        var result = await _validator.ValidateAsync(rawKey);
        result.Should().BeFalse();
    }

    [Fact]
    public void HashKey_ShouldProduceDeterministicHash()
    {
        var key = "my-secret-key";
        var hash1 = ApiKeyValidator.ComputeHash(key);
        var hash2 = ApiKeyValidator.ComputeHash(key);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void HashKey_DifferentKeys_ShouldProduceDifferentHashes()
    {
        var hash1 = ApiKeyValidator.ComputeHash("key-a");
        var hash2 = ApiKeyValidator.ComputeHash("key-b");

        hash1.Should().NotBe(hash2);
    }

    private static string HashKey(string key) => ApiKeyValidator.ComputeHash(key);

    public void Dispose() => _context.Dispose();
}
