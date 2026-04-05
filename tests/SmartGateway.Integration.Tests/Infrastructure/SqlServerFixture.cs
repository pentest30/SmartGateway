using Microsoft.EntityFrameworkCore;
using SmartGateway.Core.Data;
using Testcontainers.MsSql;

namespace SmartGateway.Integration.Tests.Infrastructure;

public class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync().AsTask();
    }

    public SmartGatewayDbContext CreateDbContext(string? dbName = null)
    {
        var connStr = ConnectionString;
        if (!string.IsNullOrEmpty(dbName))
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connStr)
            {
                InitialCatalog = dbName
            };
            connStr = builder.ConnectionString;
        }

        var options = new DbContextOptionsBuilder<SmartGatewayDbContext>()
            .UseSqlServer(connStr)
            .Options;

        return new SmartGatewayDbContext(options);
    }

    public async Task<SmartGatewayDbContext> CreateAndMigrateAsync(string? dbName = null)
    {
        var context = CreateDbContext(dbName);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    public string GetConnectionString(string dbName)
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(ConnectionString)
        {
            InitialCatalog = dbName
        };
        return builder.ConnectionString;
    }
}
