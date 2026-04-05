# SmartGateway — Claude Code Instructions

## Architecture
- Data plane: SmartGateway.Host (YARP reverse proxy)
- Control plane: SmartGateway.Core (EF Core entities, DbContext, IAuditService)
- Admin API: SmartGateway.Api (REST controllers)
- Admin UI: SmartGateway.Admin (Blazor Server, dark theme)
- Resilience: SmartGateway.Resilience (Polly v8 pipeline factory)

## Key Interfaces
- IProxyConfigProvider → DatabaseProxyConfigProvider (loads from SQL Server, hot-reloads via SignalReload)
- ILoadBalancingPolicy → WeightedLoadBalancingPolicy, LatencyAwareLoadBalancingPolicy
- IDestinationWeightProvider → DatabaseDestinationWeightProvider
- IAuditService → AuditService (tracks all mutations with actor + old/new JSON)

## Conventions
- All DB mutations via Admin API must call IAuditService.LogAsync()
- All config changes must call configProvider.SignalReload() after SaveChanges()
- EF queries must use .AsNoTracking() for read-only operations
- New LB policies implement ILoadBalancingPolicy, register in Host/Program.cs
- Navigation properties on entities are nullable (for API model binding)

## Database
- Connection: Server=127.0.0.1,1433;Database=SmartGateway;User Id=sa;Password=YourStrong@Passw0rd
- Schema auto-created via db.Database.EnsureCreated() on startup
- Tables: GatewayCluster, GatewayDestination, GatewayRoute, GatewayResiliencePolicy, GatewayAuditLog, GatewayApiKey, GatewayTransform

## Testing
- Unit tests: SmartGateway.Core.Tests, SmartGateway.Host.Tests (InMemory DB, NSubstitute)
- Integration tests: SmartGateway.Integration.Tests (Testcontainers SQL Server, WebApplicationFactory, TestUpstreamServer)
- Always test SignalReload() is called after mutations
- E2E proxy tests verify requests flow through YARP to real upstream servers

## Running
```bash
dotnet test SmartGateway.sln           # Run all 137 tests
docker compose up                      # SQL Server + Host + Admin + API
```

## Ports
- Host (YARP proxy): 5000
- Admin UI: 5001
- Admin API: 5002
- SQL Server: 1433
