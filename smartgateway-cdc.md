# SmartGateway — Concept & Design Document

**Version:** 1.0  
**Date:** April 2026  
**Status:** Draft  
**Author:** Architecture Team  
**Backed by:** Claude Code (Anthropic)

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Problem Statement](#2-problem-statement)
3. [Vision & Goals](#3-vision--goals)
4. [Architecture Overview](#4-architecture-overview)
5. [Core Features](#5-core-features)
6. [Use Cases](#6-use-cases)
7. [Technical Stack](#7-technical-stack)
8. [Data Model](#8-data-model)
9. [API Reference](#9-api-reference)
10. [Non-Functional Requirements](#10-non-functional-requirements)
11. [Project Roadmap](#11-project-roadmap)
12. [Claude Code Integration](#12-claude-code-integration)
13. [Risks & Mitigations](#13-risks--mitigations)

---

## 1. Executive Summary

**SmartGateway** is an open-source, programmable API gateway built on top of
Microsoft YARP (Yet Another Reverse Proxy), designed for distributed .NET
systems. It replaces static JSON configuration with a **database-driven control
plane** and exposes a **Blazor-based admin GUI**, making route and cluster
management accessible to both developers and non-developer operators.

The system is built from three composable layers:

- **Data Plane** — YARP handles actual request proxying at high performance
- **Control Plane** — Dynamic `IProxyConfigProvider` backed by SQL Server
- **Management Plane** — Blazor Server admin UI + REST Admin API

The entire codebase is designed to be **scaffolded, extended, and maintained
with Claude Code**, making AI-assisted development a first-class workflow.

---

## 2. Problem Statement

Most .NET teams adopting microservices face the same gateway problems:

| Pain Point | Current Reality |
|---|---|
| Static JSON config | Requires redeployment for every route change |
| No audit trail | No visibility into who changed what and when |
| Developers-only | Non-technical operators cannot manage routes |
| No service discovery | Upstream addresses hardcoded in config files |
| Fragile resilience | No circuit breaking, retry, or health eviction |
| Invisible traffic | No built-in observability without extra tools |

Existing solutions (Kong, Traefik, Envoy) solve these problems but are
**not .NET-native**, require separate infrastructure, and introduce steep
learning curves for .NET teams.

SmartGateway is **pure .NET 9**, lives in the same solution, and integrates
naturally with ASP.NET Core middleware, EF Core, and SQL Server.

---

## 3. Vision & Goals

### Vision

> A programmable, database-driven API gateway that any .NET team can deploy,
> operate, and extend — with AI-assisted development built into the workflow.

### Primary Goals

- **Zero-downtime config changes** — route updates apply instantly via hot reload
- **Operator-friendly** — non-developers can manage routes through a GUI
- **Observable by default** — OpenTelemetry wired out of the box
- **Extensible** — clean interfaces for custom LB policies, auth, and middleware
- **AI-maintainable** — structured for Claude Code to scaffold, extend, and review

### Out of Scope (v1)

- Container orchestration (Kubernetes ingress replacement)
- GraphQL federation
- Multi-datacenter replication
- gRPC transcoding

---

## 4. Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                        Clients                              │
└────────────────────────┬────────────────────────────────────┘
                         │ HTTP/HTTPS
┌────────────────────────▼────────────────────────────────────┐
│                   SmartGateway Host                         │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              ASP.NET Core Pipeline                   │   │
│  │  RateLimit → Auth → Transforms → YARP Proxy         │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                             │
│  ┌─────────────────────┐   ┌──────────────────────────┐    │
│  │  DatabaseProxyConfig│   │   Resilience Engine      │    │
│  │  Provider           │   │   (Polly v8)             │    │
│  │  (IProxyConfig)     │   │                          │    │
│  └──────────┬──────────┘   └──────────────────────────┘    │
│             │                                               │
└─────────────┼───────────────────────────────────────────────┘
              │ EF Core
┌─────────────▼───────────────────────────────────────────────┐
│               SQL Server (Control Plane DB)                 │
│  GatewayCluster | GatewayDestination | GatewayRoute        │
│  GatewayAuditLog | GatewayHealthCheck                       │
└─────────────────────────────────────────────────────────────┘
              │
┌─────────────▼───────────────────────────────────────────────┐
│              Blazor Admin UI + REST Admin API               │
│   Routes | Clusters | Health Dashboard | Audit Log         │
└─────────────────────────────────────────────────────────────┘
              │ Upstream
┌─────────────▼───────────────────────────────────────────────┐
│           Upstream Services (any HTTP/HTTPS)                │
│   Service A ×3  |  Service B ×2  |  Service C (canary)     │
└─────────────────────────────────────────────────────────────┘
```

### Solution Structure

```
SmartGateway/
├── src/
│   ├── SmartGateway.Core/          # Domain models, interfaces, EF Core
│   ├── SmartGateway.Host/          # YARP host, pipeline, config provider
│   ├── SmartGateway.Admin/         # Blazor Server admin UI
│   ├── SmartGateway.Api/           # REST Admin API (optional)
│   └── SmartGateway.Resilience/    # Polly wrappers, health checks
├── tests/
│   ├── SmartGateway.Core.Tests/
│   ├── SmartGateway.Host.Tests/
│   └── SmartGateway.Integration.Tests/
├── docs/
│   └── smartgateway-cdc.md         # this document
└── SmartGateway.sln
```

---

## 5. Core Features

### 5.1 Dynamic Route & Cluster Management

YARP's JSON config is replaced entirely by a database-backed
`IProxyConfigProvider`. Routes and clusters are stored in SQL Server and
hot-reloaded into YARP via `IChangeToken` cancellation — zero restart required.

**Key capability:** An operator edits a route path in the Blazor admin UI,
clicks Save, and YARP begins routing to the new pattern within milliseconds.

### 5.2 Load Balancing (Extensible)

Built-in policies exposed and configurable per cluster:

| Policy | Description |
|---|---|
| `RoundRobin` | Distributes requests sequentially across destinations |
| `LeastRequests` | Routes to destination with fewest active requests |
| `PowerOfTwoChoices` | Picks best of two random destinations (low overhead) |
| `Random` | Uniform random selection |
| `Weighted` | Custom policy — weight per destination from DB |
| `LatencyAware` | Custom policy — tracks rolling p95 latency per destination |

Custom policies implement `ILoadBalancingPolicy` and register via DI.

### 5.3 Service Registry

An in-memory + SQL-backed registry allows services to self-register and
deregister. The gateway's config provider subscribes to registry changes and
triggers YARP hot reload automatically.

```
POST /admin/api/services/register   → adds destination to cluster
DELETE /admin/api/services/{id}     → removes destination, triggers reload
GET  /admin/api/services            → current registered destinations
```

Registry supports TTL-based expiry: destinations not refreshed within a
configurable window are automatically evicted.

### 5.4 Health Checks

**Active health checks** — the gateway probes each destination's health
endpoint on a configurable interval. Unhealthy destinations are flagged in
the DB and excluded from the YARP destination list on next reload.

**Passive health checks** — YARP's built-in `IPassiveHealthCheckPolicy`
detects failures from live traffic (5xx responses, connection timeouts) and
temporarily evicts destinations without requiring active probing.

**Recovery** — destinations are re-admitted after a configurable consecutive
success threshold.

### 5.5 Resilience (Polly v8)

Each cluster has an associated `ResiliencePipeline` configured in DB:

| Strategy | Configurable Parameters |
|---|---|
| Retry | `MaxAttempts`, `BackoffType` (linear/exponential), `Delay` |
| Circuit Breaker | `FailureRatio`, `SamplingDuration`, `BreakDuration` |
| Timeout | `Timeout` per request |
| Fallback | `FallbackAddress` (optional static response or alt cluster) |

Pipelines are keyed per cluster and cached. Config changes trigger pipeline
rebuild without gateway restart.

### 5.6 Rate Limiting

ASP.NET Core `RateLimiter` middleware integrated before YARP:

| Scope | Strategy | Storage |
|---|---|---|
| Global | Fixed window | In-memory |
| Per route | Sliding window | In-memory |
| Per client (IP / API key) | Token bucket | Redis (distributed) |

Rate limit config is stored per route in DB and hot-reloaded with route changes.

### 5.7 Authentication & Authorization

Pluggable auth middleware — does not enforce a single model:

- **JWT Bearer** — standard ASP.NET Core, configurable issuer/audience per route
- **API Key** — header or query param, keys stored in DB with per-key metadata
- **Passthrough** — gateway forwards auth headers upstream without validation
- **Custom** — implement `IAuthPolicy`, register in DI

Per-route auth policy is set in the admin UI (`RequiresAuth`, `PolicyName`).

### 5.8 Request & Response Transforms

YARP's `AddTransforms()` API exposed with DB-driven configuration:

**Request transforms:**
- Add / remove / override headers
- Rewrite path prefix
- Inject upstream metadata (e.g., `X-Gateway-RequestId`, `X-Forwarded-For`)
- Strip auth headers before forwarding

**Response transforms:**
- Add / remove response headers
- Inject CORS headers per route

### 5.9 Admin GUI (Blazor Server)

| Screen | Capabilities |
|---|---|
| Dashboard | Live request rate, error rate, active destinations |
| Clusters | Create, edit, delete clusters; configure LB policy and resilience |
| Routes | Create, edit routes; path/host/header match rules; auth policy |
| Destinations | Per-cluster destination list; health status; weight |
| Health | Live health status grid; force evict / re-admit |
| Audit Log | Filterable changelog: who changed what, old vs new values |
| Rate Limits | Per-route limit configuration |

All mutations trigger `SignalReload()` on `DatabaseProxyConfigProvider` —
YARP hot-reloads immediately after save.

### 5.10 Observability

OpenTelemetry wired out of the box:

- **Traces** — per-request spans including upstream latency, retries, circuit state
- **Metrics** — request count, error rate, latency (p50/p95/p99) per route and cluster
- **Logs** — structured Serilog with correlation IDs

OTLP export configured via environment variables. Compatible with Grafana,
Jaeger, Seq, Azure Monitor.

---

## 6. Use Cases

### UC-01 — Add New Upstream Service

**Actor:** Developer  
**Trigger:** New microservice deployed, needs traffic routed through gateway

**Flow:**
1. Developer opens Admin GUI → Clusters → New Cluster
2. Sets `ClusterId: orders-service`, `LoadBalancing: RoundRobin`
3. Adds destinations: `orders-v1-1: https://10.0.1.10:8080`, `orders-v1-2: https://10.0.1.11:8080`
4. Opens Routes → New Route
5. Sets `PathPattern: /api/orders/{**catch-all}`, maps to `orders-service`
6. Clicks Save → YARP hot-reloads → traffic flows immediately

**Outcome:** Zero deployment, zero restart. Route active in < 1 second.

---

### UC-02 — Canary Deployment

**Actor:** DevOps Engineer  
**Trigger:** New version of a service needs gradual traffic rollout

**Flow:**
1. Engineer adds `orders-v2` cluster with new destination addresses
2. Adds `orders-v2` destination to existing `orders-service` cluster with `Weight: 10`
3. Existing destinations retain `Weight: 100`
4. Weighted LB policy routes ~9% of traffic to v2
5. Engineer monitors error rate on dashboard; increments weight over time
6. At 100%, removes v1 destinations from cluster

**Outcome:** Canary rollout without infrastructure changes or redeployment.

---

### UC-03 — Circuit Breaker Activation

**Actor:** System (automated)  
**Trigger:** Upstream service begins returning 5xx errors

**Flow:**
1. Polly's resilience pipeline detects failure ratio > 50% over 30s window
2. Circuit opens — requests to that destination fail-fast with 503
3. Gateway emits `CircuitOpened` metric and structured log event
4. After `BreakDuration` (default 30s), circuit enters half-open
5. Single probe request sent; if successful, circuit closes
6. Dashboard shows circuit state in real time

**Outcome:** Upstream failures are contained; dependent services degrade gracefully.

---

### UC-04 — Operator Updates Rate Limit

**Actor:** Operations team member (non-developer)  
**Trigger:** A route is being abused; ops wants to add per-client throttling

**Flow:**
1. Ops opens Admin GUI → Routes → selects `/api/search`
2. Navigates to Rate Limiting tab
3. Sets `PerClientLimit: 100 req/min`, `Strategy: SlidingWindow`, `Storage: Redis`
4. Clicks Save
5. Gateway applies new rate limit policy immediately — no code change

**Outcome:** Non-developer applies infrastructure change in < 2 minutes.

---

### UC-05 — Service Self-Registration

**Actor:** Microservice instance  
**Trigger:** New pod starts up in Kubernetes or bare-metal environment

**Flow:**
1. Service calls `POST /admin/api/services/register` on startup:
   ```json
   {
     "clusterId": "inventory-service",
     "destinationId": "inventory-pod-abc123",
     "address": "https://10.0.2.55:8080",
     "ttlSeconds": 30
   }
   ```
2. Gateway adds destination to cluster, triggers YARP reload
3. Service sends `PUT /admin/api/services/inventory-pod-abc123/heartbeat` every 15s
4. On shutdown, service calls `DELETE /admin/api/services/inventory-pod-abc123`
5. Gateway removes destination, triggers reload

**Outcome:** Basic service discovery without Consul or Kubernetes dependency.

---

### UC-06 — Audit Review After Incident

**Actor:** Engineering Manager  
**Trigger:** Production incident; need to identify recent config changes

**Flow:**
1. Manager opens Admin GUI → Audit Log
2. Filters by time range (last 24h), entity type (Route)
3. Sees: `UpdatedAt 14:32 — route /api/payments — PathPattern changed — by j.smith@company.com`
4. Clicks entry to see old value vs new value diff
5. Rolls back by copying old `PathPattern` into edit form and saving

**Outcome:** Full change history with actor identity and before/after values.

---

### UC-07 — Health-Driven Destination Eviction

**Actor:** System (automated)  
**Trigger:** One destination in a cluster becomes unresponsive

**Flow:**
1. Active health check fails 3 consecutive times for `payments-instance-2`
2. Gateway marks `IsHealthy = false` in DB, excludes from YARP destinations
3. Dashboard shows destination in red with `UNHEALTHY` badge
4. Traffic redistributed to remaining healthy destinations automatically
5. Health check continues probing; after 3 consecutive successes, re-admits
6. Audit log records eviction and re-admission events

**Outcome:** Failed instance removed from rotation without manual intervention.

---

### UC-08 — Per-Tenant Header Routing

**Actor:** Developer  
**Trigger:** Multi-tenant SaaS — different tenants must reach different backend clusters

**Flow:**
1. Developer creates two clusters: `tenant-a-backend`, `tenant-b-backend`
2. Creates routes with header match rules:
   - Route A: `X-Tenant-Id: tenant-a` → `tenant-a-backend`
   - Route B: `X-Tenant-Id: tenant-b` → `tenant-b-backend`
3. Default fallback route catches unmatched tenants → shared cluster
4. Transform strips `X-Tenant-Id` before forwarding to upstream

**Outcome:** Tenant isolation at the gateway layer, no upstream code change.

---

### UC-09 — JWT Auth Enforcement Per Route

**Actor:** Developer / Security team  
**Trigger:** Certain routes must require valid JWT; others are public

**Flow:**
1. Admin opens route `/api/admin/{**catch-all}`
2. Sets `RequiresAuth: true`, `PolicyName: JwtBearer`
3. Public routes (`/api/public/**`) remain with `RequiresAuth: false`
4. Gateway validates JWT on protected routes; returns 401 if invalid
5. Valid requests forwarded upstream with auth header preserved

**Outcome:** Auth enforced centrally; upstream services trust the gateway.

---

### UC-10 — Claude Code Scaffolding New Feature

**Actor:** Developer using Claude Code  
**Trigger:** Team wants to add a custom latency-aware load balancing policy

**Flow:**
1. Developer opens Claude Code in the SmartGateway solution
2. Prompts: *"Implement a LatencyAwareLoadBalancingPolicy that tracks rolling
   p95 latency per destination using a circular buffer and selects the
   destination with the lowest p95"*
3. Claude Code reads existing `ILoadBalancingPolicy` implementations in the
   solution for context
4. Scaffolds `LatencyAwareLoadBalancingPolicy.cs` with circular buffer,
   thread-safe updates, and YARP `ILoadBalancingPolicy` interface
5. Scaffolds companion unit tests in `SmartGateway.Host.Tests`
6. Developer reviews, iterates with follow-up prompts
7. Registers policy in DI; policy appears as option in Admin GUI dropdown

**Outcome:** New LB policy implemented and tested in one session.

---

## 7. Technical Stack

| Layer | Technology | Rationale |
|---|---|---|
| Proxy engine | YARP 2.x | .NET-native, extensible, high performance |
| Runtime | .NET 9 | LTS, latest performance improvements |
| ORM | EF Core 9 | Familiar, SQL Server first-class support |
| Database | SQL Server 2019+ | Existing .NET team tooling |
| Admin UI | Blazor Server | Same stack, no API layer needed |
| Resilience | Polly v8 | Industry standard, pipeline-based |
| Observability | OpenTelemetry | Vendor-neutral, OTLP export |
| Logging | Serilog | Structured logs, sink flexibility |
| Rate limiting | ASP.NET Core RateLimiter + Redis | Native + distributed |
| Testing | xUnit + Testcontainers | Integration tests with real SQL Server |
| AI Dev | Claude Code | Scaffolding, extension, code review |

---

## 8. Data Model

### Core Tables

```sql
-- Upstream service groups
CREATE TABLE GatewayCluster (
    ClusterId       NVARCHAR(100) PRIMARY KEY,
    LoadBalancing   NVARCHAR(50)  DEFAULT 'RoundRobin',
    IsActive        BIT           DEFAULT 1,
    CreatedAt       DATETIME2     DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2     DEFAULT GETUTCDATE()
);

-- Individual upstream instances
CREATE TABLE GatewayDestination (
    Id              INT IDENTITY PRIMARY KEY,
    ClusterId       NVARCHAR(100) NOT NULL REFERENCES GatewayCluster(ClusterId),
    DestinationId   NVARCHAR(100) NOT NULL,
    Address         NVARCHAR(500) NOT NULL,
    IsHealthy       BIT           DEFAULT 1,
    Weight          INT           DEFAULT 100,
    LastHeartbeat   DATETIME2,
    TtlSeconds      INT           DEFAULT 0  -- 0 = no expiry
);

-- Route matching rules
CREATE TABLE GatewayRoute (
    RouteId         NVARCHAR(100) PRIMARY KEY,
    ClusterId       NVARCHAR(100) NOT NULL REFERENCES GatewayCluster(ClusterId),
    PathPattern     NVARCHAR(500),
    Hosts           NVARCHAR(500),  -- comma-separated
    Methods         NVARCHAR(200),  -- comma-separated
    MatchHeader     NVARCHAR(200),  -- e.g. "X-Tenant-Id"
    MatchHeaderValue NVARCHAR(200),
    Priority        INT           DEFAULT 0,
    IsActive        BIT           DEFAULT 1,
    RequiresAuth    BIT           DEFAULT 0,
    AuthPolicyName  NVARCHAR(100),
    RateLimitConfig NVARCHAR(MAX), -- JSON
    CreatedAt       DATETIME2     DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2     DEFAULT GETUTCDATE()
);

-- Resilience config per cluster
CREATE TABLE GatewayResiliencePolicy (
    ClusterId           NVARCHAR(100) PRIMARY KEY REFERENCES GatewayCluster(ClusterId),
    RetryMaxAttempts    INT           DEFAULT 3,
    RetryBackoffType    NVARCHAR(20)  DEFAULT 'Exponential',
    RetryDelayMs        INT           DEFAULT 200,
    CircuitEnabled      BIT           DEFAULT 1,
    CircuitFailureRatio FLOAT         DEFAULT 0.5,
    CircuitSamplingMs   INT           DEFAULT 30000,
    CircuitBreakMs      INT           DEFAULT 30000,
    TimeoutMs           INT           DEFAULT 10000
);

-- Full audit trail
CREATE TABLE GatewayAuditLog (
    Id          INT IDENTITY PRIMARY KEY,
    EntityType  NVARCHAR(50),
    EntityId    NVARCHAR(100),
    Action      NVARCHAR(20),   -- CREATE | UPDATE | DELETE
    ChangedBy   NVARCHAR(200),
    OldValues   NVARCHAR(MAX),  -- JSON snapshot
    NewValues   NVARCHAR(MAX),
    ChangedAt   DATETIME2 DEFAULT GETUTCDATE()
);
```

---

## 9. API Reference

### Admin REST API

All endpoints require admin API key header: `X-Admin-Key: {key}`

#### Clusters

| Method | Path | Description |
|---|---|---|
| `GET` | `/admin/api/clusters` | List all clusters |
| `POST` | `/admin/api/clusters` | Create cluster |
| `PUT` | `/admin/api/clusters/{id}` | Update cluster |
| `DELETE` | `/admin/api/clusters/{id}` | Delete cluster |

#### Routes

| Method | Path | Description |
|---|---|---|
| `GET` | `/admin/api/routes` | List all routes |
| `POST` | `/admin/api/routes` | Create route |
| `PUT` | `/admin/api/routes/{id}` | Update route |
| `DELETE` | `/admin/api/routes/{id}` | Delete route |

#### Service Registry

| Method | Path | Description |
|---|---|---|
| `POST` | `/admin/api/services/register` | Register destination |
| `PUT` | `/admin/api/services/{id}/heartbeat` | Refresh TTL |
| `DELETE` | `/admin/api/services/{id}` | Deregister destination |
| `GET` | `/admin/api/services` | List registered destinations |

#### Health

| Method | Path | Description |
|---|---|---|
| `GET` | `/admin/api/health` | Gateway health summary |
| `GET` | `/admin/api/health/destinations` | Per-destination health |
| `POST` | `/admin/api/health/evict/{destId}` | Force-evict destination |
| `POST` | `/admin/api/health/admit/{destId}` | Force re-admit destination |

#### Audit

| Method | Path | Description |
|---|---|---|
| `GET` | `/admin/api/audit` | Query audit log (filterable) |

---

## 10. Non-Functional Requirements

| Requirement | Target |
|---|---|
| Throughput | ≥ 10,000 req/s on single node (benchmarked with BenchmarkDotNet) |
| Latency overhead | < 2ms p99 added latency vs direct upstream call |
| Hot reload time | < 500ms from DB save to YARP config active |
| Availability | Gateway continues serving if Admin UI / DB is temporarily unavailable (last good config cached in memory) |
| Config durability | All config changes persisted before YARP reload triggered |
| Security | Admin API requires key auth; Blazor UI requires role-based auth |
| Observability | 100% of requests traced; metrics exported every 15s |
| Testability | All core interfaces mockable; integration tests run against Testcontainers SQL Server |

---

## 11. Project Roadmap

### Phase 1 — Real Gateway (Weeks 1–4)

- [ ] YARP host project with `DatabaseProxyConfigProvider`
- [ ] EF Core models + SQL Server migration
- [ ] `SignalReload()` hot-reload mechanism
- [ ] Round-robin and weighted LB policies
- [ ] Polly v8 circuit breaker per cluster
- [ ] Basic Blazor admin: clusters, routes, destinations
- [ ] OpenTelemetry traces and metrics wired

### Phase 2 — Production Hardening (Weeks 5–10)

- [ ] Active + passive health checks with auto-eviction
- [ ] Service self-registration API with TTL heartbeat
- [ ] Redis-backed distributed rate limiting
- [ ] JWT and API key auth middleware
- [ ] Audit log (full change history with actor)
- [ ] Admin UI: health dashboard, audit log viewer
- [ ] Integration test suite with Testcontainers

### Phase 3 — Advanced Capabilities (Weeks 11–16)

- [ ] Canary / traffic-split routing (weighted destinations)
- [ ] Header-based and tenant-aware routing
- [ ] Request/response transform UI
- [ ] Latency-aware custom LB policy
- [ ] In-memory config cache (gateway survives DB outage)
- [ ] Multi-environment support (dev/staging/prod filter in DB)
- [ ] Plugin loading via `ILoadBalancingPolicy` / `IAuthPolicy` DI registration

---

## 12. Claude Code Integration

SmartGateway is designed as a **Claude Code-native** project. The codebase
structure, interface design, and documentation are optimized for AI-assisted
development workflows.

### How Claude Code Is Used

| Task | Claude Code Role |
|---|---|
| Scaffold new LB policy | Read existing policy, generate new implementation + tests |
| Add new DB column | Update EF model, generate migration, update admin UI binding |
| Review resilience config | Analyze Polly pipeline for correctness and edge cases |
| Generate integration tests | Scaffold Testcontainers-based tests for new endpoints |
| Audit trail queries | Write complex EF Core LINQ for filtered audit log queries |
| Performance review | Identify N+1 queries, allocation hotspots in config provider |

### Recommended CLAUDE.md Instructions

The project root includes a `CLAUDE.md` file with:

```markdown
# SmartGateway — Claude Code Instructions

## Architecture
- Data plane: SmartGateway.Host (YARP)
- Control plane: SmartGateway.Core (EF Core, interfaces)
- Admin: SmartGateway.Admin (Blazor Server)

## Key Interfaces
- IProxyConfigProvider → DatabaseProxyConfigProvider
- ILoadBalancingPolicy → custom policies in Host/LoadBalancing/
- IAuthPolicy → custom policies in Host/Auth/

## Conventions
- All DB mutations must call _configProvider.SignalReload() after SaveChanges()
- All admin mutations must log to GatewayAuditLog via IAuditService
- EF queries must use .AsNoTracking() for read-only operations
- New LB policies must be registered in Program.cs DI and appear in ClusterLbPolicy enum

## Testing
- Integration tests use Testcontainers (SmartGateway.Integration.Tests)
- Unit tests mock IProxyConfigProvider and IServiceRegistry
- Always test SignalReload() is called after mutations
```

### Example Claude Code Prompts

```
"Add a new GatewayApiKey table, update the EF model, create a migration,
 and add an IAuthPolicy implementation that validates API keys from this table.
 Register it in DI and add the Blazor admin screen to manage keys."

"The DatabaseProxyConfigProvider.LoadFromDatabase() is doing N+1 queries.
 Identify the issue and rewrite using Include() and projection."

"Write a Testcontainers integration test that: starts SQL Server, applies
 migrations, creates a cluster with two destinations, calls SignalReload(),
 and asserts YARP returns the correct IProxyConfig."
```

---

## 13. Risks & Mitigations

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| DB outage takes down gateway | Medium | Critical | Cache last-good config in memory; serve stale config during outage |
| Hot reload race condition | Low | High | Thread-safe `CancellationTokenSource` swap with lock; YARP handles atomicity |
| SignalReload() not called after mutation | Medium | Medium | Encapsulate in `IGatewayConfigService` — all mutations go through it |
| Redis unavailable for rate limiting | Low | Medium | Fallback to in-memory limiter with log warning |
| Admin UI auth bypass | Low | Critical | Separate auth policy for admin routes; network-level restriction |
| YARP version breaking changes | Low | Medium | Pin YARP version; abstract behind `IProxyConfigProvider` |
| Performance regression from DB load on reload | Medium | Medium | Config loaded once on reload, cached in memory — DB not hit per request |

---

*SmartGateway — Architecture Team — April 2026*  
*This document is a living specification. All changes are tracked in the project repository.*
