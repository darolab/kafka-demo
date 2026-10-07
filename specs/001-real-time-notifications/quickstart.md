# Quickstart: Real-Time Notifications

## Prerequisites

- .NET 10 SDK and Docker Desktop (Kafka integration tests use Testcontainers).
- SQL Server initialized with the KafkaDemo schema.
- Kafka reachable at the configured bootstrap address.
- Authenticated users must have corresponding rows in `Users` for private-message delivery.
- Configure `ConnectionStrings:DefaultConnection` to a dedicated KafkaDemo database before applying migrations. The repository's sample connection string targets `master` and should not be used for feature migrations.
- In Development, `Jwt:SigningKey` can be stored with `dotnet user-secrets set "Jwt:SigningKey" <secret> --project KafkaDemo.csproj`; production should use a secret manager or environment variable.
- Each concurrently running API instance must have a distinct `Notification:InstanceId`; reuse that value when restarting that instance.

## Configure and start

Apply database migrations:

```powershell
dotnet ef database update
```

The full API quickstart requires a standalone Kafka broker at the configured bootstrap address. In this workspace, `localhost:9092` was unavailable; Kafka integration behavior was validated with the Testcontainers suite instead. The database migration was not applied to the sample `master` database.

Set local Kafka and notification configuration in PowerShell. For multiple API instances, keep the topic and group prefix the same but use a different stable `Notification__InstanceId` per instance.

```powershell
$env:Kafka__BootstrapServers = "localhost:9092"
$env:Kafka__NotificationTopic = "notification-events"
$env:Kafka__NotificationConsumerGroupId = "kafka-demo-notifications"
$env:Notification__InstanceId = "api-local-1"
$env:Notification__SocketSendTimeoutSeconds = "5"
$env:Jwt__SigningKey = $env:KAFKADEMO_JWT_SIGNING_KEY
dotnet run --urls http://localhost:5000
```

`KAFKADEMO_JWT_SIGNING_KEY` must come from a secret manager/environment and contain at least 32 UTF-8 bytes. Both login token signing and JWT validation use this setting; the application fails startup if it is missing or too short.

The effective consumer group ID is the configured group prefix plus the instance ID. Never run two active API instances with the same instance ID; Kafka would load-balance events instead of fanning them out to both socket registries.

## Authenticate and connect

Retrieve a JWT using the existing login endpoint:

```http
POST /api/auth/login
Content-Type: application/json

{
  "username": "user2",
  "password": "123456"
}
```

Connect with the returned token. The authenticated `userId` claim determines the socket owner; clients cannot select another account.

```text
ws://localhost:5000/api/WebSocket/connect?access_token=<jwt>
```

## Trigger a notification

Connect a second authenticated client whose JWT identity is the recipient. From the sender's WebSocket, send:

```json
{
  "recipientId": "1002",
  "message": "notification test"
}
```

The message and schema-v1 notification envelope are committed together to SQL Server. The outbox worker publishes the envelope to `notification-events`, keyed by recipient user ID. Every API instance consumes it in its own Kafka group and sends only to local sockets belonging to that user. The recipient receives an envelope containing `schemaVersion`, `notificationId`, `userId`, `eventType`, `correlationId`, `timestampUtc`, and `payload`.

## Validate routing and recovery

- Connect two users and send a message to one; only that account should receive its notification.
- Disconnect the recipient, send an event, then reconnect. V1 is live-only: the offline event is audited and is not replayed; subsequent events are delivered to the new connection.
- Close a socket during delivery. A send times out after five seconds, stale socket state is removed, and the client must reconnect.
- Run the two-instance Kafka and latency baseline tests:

```powershell
dotnet test tests/KafkaDemo.Tests/KafkaDemo.Tests.csproj --filter FullyQualifiedName~NotificationFanoutTests
```

The baseline opens 100 sockets across two API-instance registries and publishes 10 events per second total. At least 95% of sends must complete within five seconds while Kafka is healthy and has no backlog.

## Observe delivery

Scrape Prometheus metrics from `http://localhost:5000/metrics`. The `KafkaDemo.Notifications` meter includes published-event, delivery-outcome, and WebSocket connection-open/close counters. Use the existing Prometheus data source and Grafana dashboards to inspect delivery outcomes and connection trends. Structured logs and `NotificationDeliveryAudit` rows include notification ID, user ID, instance ID, correlation ID, event type, outcome, and any non-payload error detail.

## Full validation

```powershell
dotnet test tests/KafkaDemo.Tests/KafkaDemo.Tests.csproj
dotnet build KafkaDemo.csproj
```

Expected outcomes: all tests pass; existing transaction REST response shape remains unchanged; unsupported schema versions are audited as rejected; notification delivery and connection outcomes are available in logs, SQL audit rows, and Prometheus metrics.
# Quickstart: Real-Time Notifications Validation

## Prerequisites

- The KafkaDemo backend is running with .NET 10.
- A valid SQL Server instance is available and the project database is initialized.
- Kafka is available and reachable on the configured bootstrap servers.
- A user account exists and the JWT login flow is available.

## Validation steps

### 1. Start the application

From the repository root:

```bash
dotnet run
```

### 2. Authenticate a user

Use the existing login endpoint to retrieve a JWT token.

```http
POST /api/auth/login
Content-Type: application/json

{
  "username": "user2",
  "password": "123456"
}
```

### 3. Open a WebSocket connection

Use the authenticated token as the `access_token` query parameter.

```text
ws://localhost:<port>/api/WebSocket/connect?access_token=<jwt>
```

Expected outcome: the connection is accepted and the socket remains open for the authenticated user only.

### 4. Trigger a notification-producing backend event

Generate or trigger an event that targets the authenticated user’s account in the normal business flow, such as a transaction or message event.

Expected outcome: the event is published to Kafka and delivered to the connected client without affecting the existing REST endpoints.

### 5. Validate account isolation

Attempt to subscribe with a different user identity or attempt to deliver another user’s event.

Expected outcome: the wrong user does not receive the notification, and the event is either routed to the correct user or safely ignored with diagnostics.

### 6. Test disconnect and reconnect behavior

Close the WebSocket connection and reconnect with the same valid JWT.

Expected outcome: stale connection state is cleaned up, the new connection is accepted, and subsequent notifications continue to be delivered.

### 7. Check operational diagnostics

Review application logs for:

- user ID
- connection session state
- event correlation ID
- Kafka topic and partition information
- delivery result and error context

Expected outcome: the event can be traced from creation to delivery attempt and any failure can be diagnosed.

## Success signals

- Authenticated users receive account-scoped notifications in real time.
- At least 95% of notification sends complete within five seconds under the baseline of two API instances, 100 connected clients, and 10 events per second total while Kafka is healthy and has no backlog.
- Unsupported notification schema versions are rejected and diagnosed without client delivery.
- Unauthenticated or unauthorized users are rejected.
- REST API behavior remains stable while the feature is active.
- Reconnects do not require a server restart to resume delivery.
- Multi-instance deployment remains possible through Kafka-backed routing.
