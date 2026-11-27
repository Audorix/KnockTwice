# Phase 52: Multi-node SignalR delivery

## Scope added to Release 1

Knock Twice has an optional Redis SignalR backplane under `ScaleOut:Redis`, a Redis key/channel
prefix under `ScaleOut:Prefix`, shared presence and rate counters, Redis readiness, and client-side
conversation rejoin after SignalR reconnect. The external acceptance harness in
`ScaleOutPublicEndpointTests` exercises those existing paths against two separately hosted API
containers instead of the in-memory presence stand-in used by `ScaleOutTests`.

This phase adds a disposable staging stack with PostgreSQL, Redis, two API nodes, the React web
container, and HAProxy cookie affinity. HAProxy routes `/api`, `/hubs`, and health requests to the
API pool, permits WebSocket upgrades, gives clients a `CHATX_NODE` affinity cookie, and returns
`X-ChatX-Node` so the test can confirm negotiation reached the assigned node. Negotiation remains
enabled, matching the browser client's existing SignalR transport behavior.

## Start the staging environment

```powershell
Copy-Item docker/scaleout.env.example docker/.env.scaleout
# Edit docker/.env.scaleout: set a strong PostgreSQL password and a unique environment prefix.
docker compose -f docker/compose.scaleout.yaml --env-file docker/.env.scaleout up --build -d
```

Open `http://localhost:8080`, register two disposable test users, make them contacts, create an
organization and a channel, and make both users authorized channel members. Set
`CHATX_SCALEOUT_CHANNEL_ID` to the channel's conversation ID. The harness creates their personal
direct conversation itself. Its sessions must be assigned to distinct `api-a` and `api-b` nodes;
the test fails with the assignments if round-robin allocation sends both to one node.

## Run the real-client acceptance harness

Set the following environment variables in the same PowerShell window:

```powershell
$env:CHATX_SCALEOUT_BASE_URL = 'http://localhost:8080'
$env:CHATX_SCALEOUT_CHANNEL_ID = '<channel conversation GUID>'
$env:CHATX_SCALEOUT_ANA_EMAIL = '<first user email>'
$env:CHATX_SCALEOUT_ANA_PASSWORD = '<first user password>'
$env:CHATX_SCALEOUT_BEN_EMAIL = '<second user email>'
$env:CHATX_SCALEOUT_BEN_PASSWORD = '<second user password>'
$env:CHATX_SCALEOUT_EXPECT_BACKPLANE = 'true'
$env:CHATX_SCALEOUT_COMPOSE_FILE = (Resolve-Path docker/compose.scaleout.yaml).Path
$env:CHATX_SCALEOUT_ENV_FILE = (Resolve-Path docker/.env.scaleout).Path
dotnet test tests/Audorix.KnockTwice.IntegrationTests/Audorix.KnockTwice.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~ScaleOutPublicEndpointTests
```

The message test signs in through the public endpoint, records the two `CHATX_NODE` cookie values,
asserts that hub negotiation reaches each assigned node, joins the authorized channel and a new
personal direct conversation, and posts messages through the normal HTTP message endpoint. It
asserts the other node receives the matching persisted message IDs in both directions for both
conversation types. Set `CHATX_SCALEOUT_EXPECT_BACKPLANE=false` for the negative-control run; the
same messages must persist while cross-node live events do not arrive.

Run the negative control with both API nodes recreated without `ScaleOut:Redis`, leaving HAProxy,
its affinity cookie, PostgreSQL and Redis running. Then re-enable the same Redis connection and
repeat the positive run:

```powershell
$env:CHATX_SCALEOUT_REDIS = ''
docker compose -f docker/compose.scaleout.yaml --env-file docker/.env.scaleout up -d --force-recreate api-a api-b
$env:CHATX_SCALEOUT_EXPECT_BACKPLANE = 'false'
dotnet test tests/Audorix.KnockTwice.IntegrationTests/Audorix.KnockTwice.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~Affinity_keeps_each_client
$env:CHATX_SCALEOUT_REDIS = 'redis:6379,abortConnect=false'
docker compose -f docker/compose.scaleout.yaml --env-file docker/.env.scaleout up -d --force-recreate api-a api-b
$env:CHATX_SCALEOUT_EXPECT_BACKPLANE = 'true'
dotnet test tests/Audorix.KnockTwice.IntegrationTests/Audorix.KnockTwice.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~Affinity_keeps_each_client
```

The failover test is destructive only to one of this disposable Compose stack's API services and
is opt-in. Run it with `CHATX_SCALEOUT_TEST_FAILOVER=true`. It stops the node that owns the second
client, sends a durable message while that client reconnects, verifies the client lands on the
surviving node and can read the missed message from history, then verifies a subsequent live event.
The stopped service is restarted in a `finally` block.

```powershell
$env:CHATX_SCALEOUT_TEST_FAILOVER = 'true'
dotnet test tests/Audorix.KnockTwice.IntegrationTests/Audorix.KnockTwice.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~Client_reconnects_rejoins_and_catches_up
```

## Production load-balancer settings

The production load balancer is operated outside this repository. Apply the equivalent of
`docker/haproxy/scaleout.cfg`: route `/hubs/chat` negotiation and all subsequent hub requests to the
same healthy API node using a cookie affinity key; permit HTTP/1.1 WebSocket upgrades and set a
long-lived tunnel/read timeout; preserve `Host`, `X-Forwarded-For`, and the external HTTPS scheme.
Apply affinity to all hub requests, including Long Polling fallback, not just the WebSocket
upgrade. Set the affinity cookie `Secure` when TLS terminates at the load balancer. Route ordinary
`/api/v1` requests normally; the chat client's authenticated API requests may be served by any
node because durable state is shared in PostgreSQL.

All API nodes in one environment must use the same `ScaleOut:Redis` connection and the same
`ScaleOut:Prefix`. Give each environment a different prefix (for example `chatx-staging` and
`chatx-production`). Supply Redis credentials through the secret environment or deployment secret
store; never commit them. The Redis backplane is separately enabled from `Search`, `Storage`, and
`SupportQueue` provider selection. When `ScaleOut:Redis` is configured, Redis is required for
readiness; the service does not fall back to local-only SignalR delivery.

## Other multi-node limits

- The SignalR connection registry and `RealtimeState` are local to each API process. Redis presence
  records which nodes have a user online and `IHubContext`/groups carry events cross-node. Immediate
  socket aborts and other connection-ID operations only see connections owned by the current node;
  session checks on the socket still run at hub invocation and on the periodic sweep.
- The separate ephemeral `PrivateChats` feature stores chat sessions and messages in process
  memory. It must remain disabled or all participants must be kept on one node; it is not made
  multi-node by the SignalR backplane. Durable personal direct conversations are supported.
- Durable messages and support-routing work use shared PostgreSQL/outbox or configured queue
  coordination. Do not add a second uncoordinated worker that owns process-local copies of durable
  work.
- WebSocket clients do not require affinity when the Redis backplane is healthy, but this release
  configures affinity for negotiation, reconnect, and fallback transports as requested. It also
  keeps future Long Polling clients pinned to one node for the connection lifetime.

## Current verification status

The API already contains the Redis backplane, Redis readiness check, shared presence/rate limits,
and React reconnect-time group rejoin. `docker/compose.scaleout.yaml`, `docker/haproxy/scaleout.cfg`,
and `ScaleOutPublicEndpointTests` now provide a reproducible real-dependency test path. The current
environment has Docker Compose but no reachable Docker daemon, so the two-node positive test,
negative control, and node-stop/reconnect test have not run here. The previous Phase 21 scale-out
tests use an in-memory stand-in and do not count as the requested cross-node delivery proof.
