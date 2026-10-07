# Research: Real-Time Notifications

## Decision: Use Kafka as the shared event backbone and per-user routing for WebSocket delivery

The design will treat Kafka as the durable event transport for backend-generated notifications, while each API instance manages only the WebSocket connections it owns. When a notification event is consumed, the instance resolves the target user and dispatches only to local sockets for that user. This prevents single-instance dependency while still preserving the current architecture and supporting account-scoped access.

Each API instance uses its own stable, unique consumer group so Kafka delivers every notification record to every running instance; records are keyed by target user ID. This deliberate fan-out is required because the socket owner is not globally known to Kafka. Delivery is at-least-once, and clients deduplicate on `notificationId`.

### Rationale

- The existing project already integrates with Kafka via `KafkaProducerService` and is already structured around event-driven processing.
- The repository currently tracks per-user WebSocket sessions in `WebSocketConnectionManager`, which is a strong fit for local delivery after the event reaches the correct node.
- The feature requirement explicitly says notifications should scale across multiple API instances and must not allow cross-user access.

### Alternatives considered

- Same-instance-only delivery: rejected because it fails the multi-instance scaling constraint and would create node affinity issues.
- Broadcast-everything-to-all-sockets: rejected because it violates the requirement that users only receive notifications for their account.
- Shared in-memory connection registry across instances: rejected because it is not a reliable multi-node pattern and contradicts the intended distributed design.

## Decision: Bind WebSocket sessions to the authenticated user identity and reject mismatched subscriptions

The existing JWT authentication already extracts a `userId` claim and sets it in `OnMessageReceived`. The notification stream will rely on that same authenticated identity and avoid accepting any user-supplied target that does not match the current principal.

### Rationale

- This preserves the current security posture and reuses existing JWT validation behavior.
- It reduces the chance of a client impersonating another account while still allowing the app to authorize per-user streams.

### Alternatives considered

- Accepting a client-provided `userId` parameter in the WebSocket request: rejected because it creates an impersonation vector.
- Using only a socket ID without identity gating: rejected because it does not enforce account isolation.

## Decision: Add delivery and connection telemetry rather than relying solely on application logs

The feature should capture event, user, connection, and delivery correlation metadata in logs and, where practical, in an audit table or message payload metadata. This provides enough diagnostic detail for delivery failures and reconnect issues without exposing unnecessary user data.

### Rationale

- The user story for troubleshooting indicates a need to determine which event and connection failed.
- The current code already logs warnings on JWT failures and connection lifecycle events.

### Alternatives considered

- Relying only on informal logs: rejected because it is too weak for distributed troubleshooting.
- Persisting full message payloads for all notifications: rejected because it increases storage and privacy risk without clear value.

## Decision: Keep REST APIs stable and add notification capability as a narrow, parallel surface

The new functionality will live next to the existing controller and service patterns without changing the current endpoints or request contracts. Notification flows will be additive and event-driven rather than altering existing API semantics.

### Rationale

- The feature must not break existing REST APIs.
- The project already organizes logic into controllers and services, so this is the least disruptive expansion path.

### Alternatives considered

- Reusing existing REST endpoints to push notifications: rejected because it mixes transport types and adds churn to the public API.
- Changing the auth or message models globally: rejected because it would be a larger and riskier shift.

## Decision: Standardize on per-user notification payloads and correlation headers

Each notification message will include a stable event identifier, target user id, type, and timestamp, plus the payload of the actual backend event. This makes dispatch and auditing consistent across nodes and easier to debug.

### Rationale

- The design requires per-user routing, observability, and reconnection resilience.
- A consistent payload lets downstream consumers and future tests assert the same contract regardless of node.

### Alternatives considered

- Free-form message bodies without metadata: rejected because it makes tracing and auditing much harder.
- Queuing only opaque Kafka values with no contract: rejected because it removes comparable message semantics and makes scaling decisions harder.

## Decision: Version the Kafka notification envelope and keep offline delivery live-only for v1

The envelope carries `schemaVersion: 1`; consumers reject unsupported versions with structured diagnostics. Events are not replayed to users who were offline: the outcome is audited, and a reconnect receives only subsequent live events. A WebSocket send is bounded to five seconds, after which the stale/slow socket is removed.

### Rationale

- An explicit schema version satisfies the contract-first governance requirement and makes incompatible changes detectable.
- Live-only delivery avoids implying durable inbox semantics that the feature does not otherwise specify.
- A bounded send prevents one slow client from indefinitely blocking the Kafka consumer or unrelated sockets.

### Alternatives considered

- Replaying missed notifications after reconnect: deferred because durable inbox and replay cursor semantics are outside v1 scope.
- Silently accepting unknown schemas: rejected because it risks misrouting or misinterpreting notification data.
