using Microsoft.EntityFrameworkCore;
using SmartGateway.Core.Entities;

namespace SmartGateway.Core.Data;

public class SmartGatewayDbContext : DbContext
{
    public SmartGatewayDbContext(DbContextOptions<SmartGatewayDbContext> options)
        : base(options) { }

    public DbSet<GatewayCluster> Clusters => Set<GatewayCluster>();
    public DbSet<GatewayDestination> Destinations => Set<GatewayDestination>();
    public DbSet<GatewayRoute> Routes => Set<GatewayRoute>();
    public DbSet<GatewayResiliencePolicy> ResiliencePolicies => Set<GatewayResiliencePolicy>();
    public DbSet<GatewayAuditLog> AuditLogs => Set<GatewayAuditLog>();
    public DbSet<GatewayApiKey> ApiKeys => Set<GatewayApiKey>();
    public DbSet<GatewayTransform> Transforms => Set<GatewayTransform>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GatewayCluster>(entity =>
        {
            entity.HasKey(e => e.ClusterId);
            entity.Property(e => e.ClusterId).HasMaxLength(100);
            entity.Property(e => e.LoadBalancing).HasMaxLength(50).HasDefaultValue("RoundRobin");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<GatewayDestination>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DestinationId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Address).HasMaxLength(500).IsRequired();
            entity.Property(e => e.ClusterId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.IsHealthy).HasDefaultValue(true);
            entity.Property(e => e.Weight).HasDefaultValue(100);

            entity.HasOne(e => e.Cluster)
                .WithMany(c => c.Destinations)
                .HasForeignKey(e => e.ClusterId);
        });

        modelBuilder.Entity<GatewayRoute>(entity =>
        {
            entity.HasKey(e => e.RouteId);
            entity.Property(e => e.RouteId).HasMaxLength(100);
            entity.Property(e => e.ClusterId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.PathPattern).HasMaxLength(500);

            entity.HasOne(e => e.Cluster)
                .WithMany(c => c.Routes)
                .HasForeignKey(e => e.ClusterId);
        });

        modelBuilder.Entity<GatewayResiliencePolicy>(entity =>
        {
            entity.HasKey(e => e.ClusterId);
            entity.Property(e => e.ClusterId).HasMaxLength(100);
            entity.Property(e => e.RetryOnStatusCodes).HasMaxLength(200).HasDefaultValue("502,503,504");

            entity.HasOne(e => e.Cluster)
                .WithOne(c => c.ResiliencePolicy)
                .HasForeignKey<GatewayResiliencePolicy>(e => e.ClusterId);
        });

        modelBuilder.Entity<GatewayAuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EntityType).HasMaxLength(50);
            entity.Property(e => e.EntityId).HasMaxLength(100);
            entity.Property(e => e.Action).HasMaxLength(20);
            entity.Property(e => e.ChangedBy).HasMaxLength(200);
        });

        modelBuilder.Entity<GatewayTransform>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RouteId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Type).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Key).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Value).HasMaxLength(500);
            entity.Property(e => e.Action).HasMaxLength(20).HasDefaultValue("Set");

            entity.HasOne(e => e.Route)
                .WithMany()
                .HasForeignKey(e => e.RouteId);
        });

        modelBuilder.Entity<GatewayApiKey>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.KeyHash).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Scopes).HasMaxLength(500);
            entity.Property(e => e.Role).HasMaxLength(50).HasDefaultValue("admin");
        });
    }
}
