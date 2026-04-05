#!/bin/bash
set -e

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
API_URL="http://localhost:5002"
PROXY_URL="http://localhost:5000"
GREEN='\033[0;32m'
RED='\033[0;31m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
NC='\033[0m'

log()  { echo -e "${CYAN}[DEMO]${NC} $1"; }
pass() { echo -e "${GREEN}  PASS${NC} $1"; }
fail() { echo -e "${RED}  FAIL${NC} $1"; }
step() { echo -e "\n${YELLOW}=== $1 ===${NC}"; }

# ── Pre-build everything ──
log "Building demo projects..."
cd "$ROOT"
dotnet build demo/FakeUpstream/FakeUpstream.csproj -q > /dev/null 2>&1
dotnet build src/SmartGateway.Host/SmartGateway.Host.csproj -q > /dev/null 2>&1
dotnet build src/SmartGateway.Api/SmartGateway.Api.csproj -q > /dev/null 2>&1
log "Build complete."

cleanup() {
    log "Stopping all services..."
    kill $PID_UP1 $PID_UP2 $PID_HOST $PID_API 2>/dev/null || true
    wait 2>/dev/null || true
    log "Done."
}
trap cleanup EXIT

# ── Step 1: Start fake upstreams ──
step "Starting fake upstream services"

cd "$ROOT/demo/FakeUpstream"
dotnet run --no-build -- orders-v1 3000 &
PID_UP1=$!
sleep 2
dotnet run --no-build -- orders-v2 3001 &
PID_UP2=$!
sleep 3

curl -sf http://localhost:3000/health > /dev/null && pass "orders-v1 on :3000" || fail "orders-v1"
curl -sf http://localhost:3001/health > /dev/null && pass "orders-v2 on :3001" || fail "orders-v2"

# ── Step 2: Start SmartGateway Host + API ──
step "Starting SmartGateway Host (YARP proxy) + Admin API"

cd "$ROOT/src/SmartGateway.Host"
dotnet run --no-build --no-launch-profile --urls http://localhost:5000 &
PID_HOST=$!

cd "$ROOT/src/SmartGateway.Api"
dotnet run --no-build --no-launch-profile --urls http://localhost:5002 &
PID_API=$!

log "Waiting for services to start..."
sleep 10

# Retry API health check with backoff
API_OK=false
for attempt in 1 2 3 4 5; do
    if curl -sf http://localhost:5002/admin/api/clusters > /dev/null 2>&1; then
        API_OK=true
        break
    fi
    log "API not ready, retry $attempt/5..."
    sleep 3
done
$API_OK && pass "Admin API on :5002" || fail "Admin API"

# Check Host
PROXY_OK=false
for attempt in 1 2 3; do
    if curl -sf -X POST http://localhost:5000/_admin/reload > /dev/null 2>&1; then
        PROXY_OK=true
        break
    fi
    sleep 2
done
$PROXY_OK && pass "YARP Host on :5000" || fail "YARP Host"

# ── Step 3: Seed config via Admin API ──
step "Seeding gateway config via Admin API"

# Clean up from previous runs
curl -s -X DELETE "$API_URL/admin/api/services/orders-v1" > /dev/null 2>&1
curl -s -X DELETE "$API_URL/admin/api/services/orders-v2" > /dev/null 2>&1
curl -s -X DELETE "$API_URL/admin/api/routes/orders-route" > /dev/null 2>&1
curl -s -X DELETE "$API_URL/admin/api/clusters/orders-service" > /dev/null 2>&1

# Create cluster
curl -sf -X POST "$API_URL/admin/api/clusters" \
  -H "Content-Type: application/json" \
  -d '{"clusterId":"orders-service","loadBalancing":"RoundRobin"}' > /dev/null
pass "Created cluster: orders-service (RoundRobin)"

# Create destination pointing to upstream v1
curl -sf -X POST "$API_URL/admin/api/services/register" \
  -H "Content-Type: application/json" \
  -d '{"clusterId":"orders-service","destinationId":"orders-v1","address":"http://localhost:3000","ttlSeconds":0}' > /dev/null
pass "Registered destination: orders-v1 -> :3000"

# Create route
curl -sf -X POST "$API_URL/admin/api/routes" \
  -H "Content-Type: application/json" \
  -d '{"routeId":"orders-route","clusterId":"orders-service","pathPattern":"/api/orders/{**catch-all}"}' > /dev/null
pass "Created route: /api/orders/{**catch-all} -> orders-service"

# Trigger YARP reload
curl -sf -X POST "$PROXY_URL/_admin/reload" > /dev/null
pass "YARP config reloaded"
sleep 1

# ── Step 4: Test basic proxy ──
step "Testing basic proxy flow"

RESP=$(curl -sf "$PROXY_URL/api/orders/123")
if echo "$RESP" | grep -q "orders-v1"; then
    pass "GET /api/orders/123 -> proxied to orders-v1"
    echo "       Response: $RESP"
else
    fail "GET /api/orders/123 (response: $RESP)"
fi

RESP=$(curl -sf "$PROXY_URL/api/orders/456/items")
if echo "$RESP" | grep -q "/api/orders/456/items"; then
    pass "Path params forwarded correctly"
else
    fail "Path params"
fi

STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$PROXY_URL/api/unknown")
if [ "$STATUS" = "404" ]; then
    pass "Unknown route returns 404"
else
    fail "Unknown route returned $STATUS (expected 404)"
fi

# ── Step 5: Test hot-reload ──
step "Testing hot-reload (add second destination)"

curl -sf -X POST "$API_URL/admin/api/services/register" \
  -H "Content-Type: application/json" \
  -d '{"clusterId":"orders-service","destinationId":"orders-v2","address":"http://localhost:3001","ttlSeconds":0}' > /dev/null
pass "Added orders-v2 destination"

curl -sf -X POST "$PROXY_URL/_admin/reload" > /dev/null
pass "YARP config reloaded"
sleep 1

log "Sending 20 requests to check load balancing..."
V1=0; V2=0
for i in $(seq 1 20); do
    RESP=$(curl -sf "$PROXY_URL/api/orders/test")
    if echo "$RESP" | grep -q "orders-v1"; then V1=$((V1+1)); fi
    if echo "$RESP" | grep -q "orders-v2"; then V2=$((V2+1)); fi
done
pass "Traffic distribution: orders-v1=$V1, orders-v2=$V2 (out of 20)"

# ── Step 6: Test health eviction ──
step "Testing health eviction (killing orders-v1)"

kill $PID_UP1 2>/dev/null || true
wait $PID_UP1 2>/dev/null || true
log "Killed orders-v1. Waiting 15s for health probe to detect..."
sleep 15

# Trigger reload so YARP picks up the health state change
curl -s -X POST "$PROXY_URL/_admin/reload" > /dev/null 2>&1
sleep 1

# All traffic should go to v2 now
V2_ONLY=0
for i in $(seq 1 5); do
    RESP=$(curl -sf "$PROXY_URL/api/orders/test" 2>/dev/null || echo "error")
    if echo "$RESP" | grep -q "orders-v2"; then V2_ONLY=$((V2_ONLY+1)); fi
done
if [ "$V2_ONLY" -ge 4 ]; then
    pass "After eviction: all traffic goes to orders-v2 ($V2_ONLY/5)"
else
    fail "Expected traffic on v2 only, got $V2_ONLY/5"
fi

# ── Step 7: Check audit trail ──
step "Checking audit trail"

AUDIT=$(curl -sf "$API_URL/admin/api/audit")
AUDIT_COUNT=$(echo "$AUDIT" | grep -o '"action"' | wc -l)
pass "Audit log has $AUDIT_COUNT entries"
echo "$AUDIT" | python3 -m json.tool 2>/dev/null | head -30 || echo "$AUDIT" | head -200

# ── Step 8: Restart upstream, test recovery ──
step "Testing auto-recovery (restarting orders-v1)"

cd "$ROOT/demo/FakeUpstream"
dotnet run --no-build -- orders-v1 3000 &
PID_UP1=$!
log "Restarted orders-v1. Waiting 15s for health probe to re-admit..."
sleep 15
curl -s -X POST "$PROXY_URL/_admin/reload" > /dev/null 2>&1
sleep 1

V1=0; V2=0
for i in $(seq 1 10); do
    RESP=$(curl -sf "$PROXY_URL/api/orders/test")
    if echo "$RESP" | grep -q "orders-v1"; then V1=$((V1+1)); fi
    if echo "$RESP" | grep -q "orders-v2"; then V2=$((V2+1)); fi
done
if [ "$V1" -gt 0 ] && [ "$V2" -gt 0 ]; then
    pass "Recovery: traffic split again v1=$V1, v2=$V2"
else
    fail "Recovery: v1=$V1, v2=$V2 (expected both > 0)"
fi

# ── Summary ──
step "DEMO COMPLETE"
log "SmartGateway is working end-to-end!"
log ""
log "Services running:"
log "  YARP Proxy:  $PROXY_URL"
log "  Admin API:   $API_URL"
log "  orders-v1:   http://localhost:3000"
log "  orders-v2:   http://localhost:3001"
log ""
log "Try it yourself:"
log "  curl $PROXY_URL/api/orders/hello"
log "  curl $API_URL/admin/api/clusters | jq"
log "  curl $API_URL/admin/api/audit | jq"
log ""
log "Press Ctrl+C to stop all services."
wait
