using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SmartGateway.Core.Data;

namespace SmartGateway.Host.Auth;

public class ApiKeyValidator
{
    private readonly IDbContextFactory<SmartGatewayDbContext> _contextFactory;

    public ApiKeyValidator(IDbContextFactory<SmartGatewayDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<bool> ValidateAsync(string rawKey)
    {
        var hash = ComputeHash(rawKey);

        await using var context = await _contextFactory.CreateDbContextAsync();
        var key = await context.ApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyHash == hash && k.IsActive);

        if (key == null)
            return false;

        // Update last used (fire-and-forget on a new context)
        await using var updateCtx = await _contextFactory.CreateDbContextAsync();
        var toUpdate = await updateCtx.ApiKeys.FindAsync(key.Id);
        if (toUpdate != null)
        {
            toUpdate.LastUsedAt = DateTime.UtcNow;
            await updateCtx.SaveChangesAsync();
        }

        return true;
    }

    public static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}
