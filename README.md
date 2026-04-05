# SmartGateway

A programmable, database-driven API gateway built on [YARP](https://microsoft.github.io/reverse-proxy/) (Yet Another Reverse Proxy) for .NET distributed systems.

SmartGateway replaces static JSON configuration with a **SQL Server control plane** and exposes a **Blazor admin UI**, making route and cluster management accessible to both developers and operators — with zero-downtime config changes.

![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)
![YARP](https://img.shields.io/badge/YARP-2.3-blue)
![Tests](https://img.shields.io/badge/tests-141%20passing-green)
![License](https://img.shields.io/badge/license-MIT-gray)

---

## Architecture

```
Clients ──► SmartGateway Host (YARP) ──► Upstream Services
                 │
                 ├── DatabaseProxyConfigProvider (SQL Server)
                 ├── WeightedLoadBalancingPolicy
                 ├── LatencyAwareLoadBalancingPolicy
                 ├── HealthProbeService (background)
                 ├── TtlExpiryService (background)
                 ├── API Key Auth Middleware
                 └── Rate Limiter
                 
Admin API ──► SQL Server ◄── Blazor Admin UI
```

| Layer | Project | Purpose |
|---|---|---|
| Data Plane | `SmartGateway.Host` | YARP reverse proxy, LB policies, health checks |
| Control Plane | `SmartGateway.Core` | EF Core entities, DbContext, audit service |
| Admin API | `SmartGateway.Api` | REST endpoints for CRUD + service registry |
| Admin UI | `SmartGateway.Admin` | Blazor Server dashboard (dark theme) |
| Resilience | `SmartGateway.Resilience` | Polly v8 pipeline factory |

## Features

- **Zero-downtime config** — routes and clusters stored in SQL Server, hot-reloaded into YARP via `SignalReload()`
- **3 load balancing policies** — RoundRobin (YARP built-in), Weighted, LatencyAware (custom p95 tracking)
- **Active health checks** — background service probes `/health` on each destination, auto-evicts unhealthy nodes
- **Service self-registration** — `POST /admin/api/services/register` with TTL-based heartbeat and auto-expiry
- **Polly v8 resilience** — per-cluster retry, circuit breaker, and timeout pipelines
- **Rate limiting** — ASP.NET Core RateLimiter with FixedWindow, SlidingWindow, TokenBucket per route
- **API Key authentication** — SHA256-hashed keys validated per route via `X-Api-Key` header
- **Request/response transforms** — DB-driven header injection, removal, path prefix stripping
- **Header-based tenant routing** — route by `X-Tenant-Id` header to different backend clusters
- **WebSocket proxying** — YARP handles WebSocket upgrade natively
- **Full audit trail** — every mutation logged with actor, timestamp, old/new JSON values
- **Canary deployments** — visual weight sliders for gradual traffic rollout
- **Plugin system** — load custom `ILoadBalancingPolicy` from external DLLs
- **Admin API security** — `X-Admin-Key` header authentication
- **Blazor Admin UI** — 11 pages, dark professional theme, modal-based CRUD
- **141 tests** — unit + integration + E2E with Testcontainers SQL Server

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) (for SQL Server)

### 1. Start SQL Server

```bash
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YourStrong@Passw0rd" \
  -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
```

### 2. Run the Gateway

```bash
# Terminal 1 — YARP Proxy (port 5000)
cd src/SmartGateway.Host
dotnet run --urls http://localhost:5000

# Terminal 2 — Admin API (port 5002)
cd src/SmartGateway.Api
dotnet run --no-launch-profile --urls http://localhost:5002

# Terminal 3 — Admin UI (port 5001)
cd src/SmartGateway.Admin
dotnet run
```

### 3. Open the Admin UI

Navigate to **http://localhost:5001** — you'll see the dark-themed dashboard.

### 4. Configure via API

```bash
# Create a cluster
curl -X POST http://localhost:5002/admin/api/clusters \
  -H "Content-Type: application/json" \
  -d '{"clusterId":"my-service","loadBalancing":"RoundRobin"}'

# Register a destination
curl -X POST http://localhost:5002/admin/api/services/register \
  -H "Content-Type: application/json" \
  -d '{"clusterId":"my-service","destinationId":"instance-1","address":"http://localhost:3000","ttlSeconds":0}'

# Create a route
curl -X POST http://localhost:5002/admin/api/routes \
  -H "Content-Type: application/json" \
  -d '{"routeId":"my-route","clusterId":"my-service","pathPattern":"/api/{**catch-all}"}'

# Trigger YARP to reload config
curl -X POST http://localhost:5000/_admin/reload

# Test the proxy
curl http://localhost:5000/api/hello
```

### 5. Run the Demo

```bash
bash demo/demo.sh
```

This starts fake upstreams, seeds config, and runs through proxy, load balancing, health eviction, and recovery scenarios.

## Docker Compose

```bash
docker compose up
```

| Service | Port |
|---|---|
| YARP Proxy | 5000 |
| Admin UI | 5001 |
| Admin API | 5002 |
| SQL Server | 1433 |

## API Reference

### Clusters

| Method | Path | Description |
|---|---|---|
| `GET` | `/admin/api/clusters` | List all clusters |
| `POST` | `/admin/api/clusters` | Create cluster |
| `PUT` | `/admin/api/clusters/{id}` | Update cluster |
| `DELETE` | `/admin/api/clusters/{id}` | Delete cluster |

### Routes

| Method | Path | Description |
|---|---|---|
| `GET` | `/admin/api/routes` | List all routes |
| `POST` | `/admin/api/routes` | Create route |
| `PUT` | `/admin/api/routes/{id}` | Update route |
| `DELETE` | `/admin/api/routes/{id}` | Delete route |

### Service Registry

| Method | Path | Description |
|---|---|---|
| `GET` | `/admin/api/services` | List destinations |
| `POST` | `/admin/api/services/register` | Register destination |
| `PUT` | `/admin/api/services/{id}/heartbeat` | Refresh TTL |
| `DELETE` | `/admin/api/services/{id}` | Deregister |

### Audit

| Method | Path | Description |
|---|---|---|
| `GET` | `/admin/api/audit` | Query audit log |

### Gateway Control

| Method | Path | Description |
|---|---|---|
| `POST` | `/_admin/reload` | Trigger YARP config reload |

## Testing

```bash
# Run all 141 tests
dotnet test SmartGateway.sln

# Unit tests only (fast, no Docker)
dotnet test tests/SmartGateway.Core.Tests
dotnet test tests/SmartGateway.Host.Tests

# Integration tests (requires Docker for Testcontainers)
dotnet test tests/SmartGateway.Integration.Tests
```

### Test Coverage

| Category | Tests | What's tested |
|---|---|---|
| Core models & services | 23 | Entities, DbContext, AuditService |
| Host policies & providers | 52 | ConfigProvider, LB policies, health checks, rate limiting, auth |
| E2E proxy | 11 | Requests through YARP to real upstreams, hot-reload |
| SQL Server schema | 8 | FK enforcement, defaults, concurrent writes |
| Health probes | 5 | Probe, evict, recover, unreachable |
| Service registry | 5 | Register, heartbeat, TTL, deregister |
| Weighted LB | 2 | Statistical distribution verification |
| Tenant routing | 4 | Header-based routing to different clusters |
| Transforms | 3 | Request/response header injection |
| Audit trail | 7 | CRUD audit entries, filters, ordering |
| Admin API auth | 4 | X-Admin-Key validation |
| Latency-aware LB | 10 | Circular buffer, p95 tracking, thread safety |
| Config cache | 3 | Survive DB outage with last-good config |

## Documentation

See the [`docs/`](docs/) folder for detailed guides:

- [Getting Started](docs/01-getting-started.html) — Installation and first route
- [Routing & Clusters](docs/02-routing-clusters.html) — Route matching, cluster configuration
- [Load Balancing](docs/03-load-balancing.html) — RoundRobin, Weighted, LatencyAware policies
- [Health Checks](docs/04-health-checks.html) — Active probing, eviction, recovery
- [Service Registry](docs/05-service-registry.html) — Self-registration with TTL
- [Resilience](docs/06-resilience.html) — Polly circuit breakers, retries, timeouts
- [Authentication](docs/07-authentication.html) — API Key and JWT auth per route
- [Rate Limiting](docs/08-rate-limiting.html) — Per-route throttling
- [Canary Deployments](docs/09-canary-deployments.html) — Weighted traffic splitting
- [Transforms](docs/10-transforms.html) — Request/response header manipulation
- [Admin UI Guide](docs/11-admin-ui.html) — Blazor dashboard walkthrough

## Tech Stack

| Component | Technology |
|---|---|
| Proxy Engine | YARP 2.3 |
| Runtime | .NET 10 |
| Database | SQL Server + EF Core 10 |
| Admin UI | Blazor Server |
| Resilience | Polly v8 |
| Observability | OpenTelemetry + Serilog |
| Testing | xUnit + Testcontainers + FluentAssertions |

## License

MIT
