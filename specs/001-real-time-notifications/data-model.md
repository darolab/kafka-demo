# Data Model: Real-Time Notifications

## Overview

The notification feature introduces a small set of durable and runtime concepts that fit within the project’s existing domain model. The primary focus is per-user routing, connection lifecycle tracking, and delivery diagnostics while preserving the current controller/service patterns.

## Entities

### User

Represents an authenticated account holder.

- `Id`: unique integer identifier
- `Email`: canonical account address
- `Name`: display name
- `CreatedAt`: account creation timestamp
- Relationships: one user may have many active connection sessions and many notification events addressed to them

### WebSocketConnection

Represents runtime-only state for a live client connection owned by one API instance. It is not persisted as a connection-history table.

- `ConnectionId`: unique runtime identifier
- `UserId`: owner of the connection
- `InstanceId`: API instance identifier
- `ConnectedAtUtc`: connect timestamp
- `LastSeenAtUtc`: last successful heartbeat or message timestamp
- `Status`: open, stale, closed, reconnecting
- Relationships: belongs to one user; may receive zero or many notifications while open

### NotificationEnvelope

Represents a versioned Kafka event intended for a specific user. It is not separately persisted; delivery outcomes are persisted in `NotificationDeliveryAudit`.

- `SchemaVersion`: required integer; `1` for the current contract
- `NotificationId`: unique event identifier
- `UserId`: target user
- `EventType`: domain event type, such as message, status update, transaction, or system notice
- `CorrelationId`: trace identifier across producer, broker, consumer, and delivery
- `Payload`: structured event payload or message body
- `CreatedAtUtc`: event creation time

### NotificationDeliveryAudit

Represents the operational evidence for a notification outcome.

- `AuditId`: unique record identifier
- `NotificationId`: associated notification
- `UserId`: target user
- `ConnectionId`: target connection if available
- `InstanceId`: API instance that attempted delivery
- `Result`: sent, not_connected, rejected, failed, stale_connection
- `DeliveredAtUtc`: timestamp of successful send
- `ErrorDetail`: optional diagnostic message

## Relationships

- A `User` can have many runtime `WebSocketConnection` instances while online; connection lifecycle history is recorded through logs and notification audit metadata, not persisted connection rows.
- A `User` can receive many `NotificationEnvelope` events.
- Each `NotificationEnvelope` can produce delivery-audit rows on each API instance that processes it.
- Each `WebSocketConnection` can be referenced by multiple `NotificationDeliveryAudit` rows over its lifecycle.

## Validation rules

- Every notification must target exactly one user or account owner.
- A connection must belong to the authenticated user who opened it.
- The client must not be able to submit another user’s identifier as the target.
- The system must reject or log invalid or stale connection states.
- Correlation IDs must be present for traceability when notifications are generated and dispatched.

## State transitions

### Connection state

- connected -> open
- open -> stale when the socket is no longer active
- stale -> closed during cleanup
- closed -> reconnecting on a new connection

### Notification state

- queued -> dispatched -> delivered
- queued -> failed when no valid target connection is available or message generation fails
- dispatched -> rejected when user authorization or routing rules fail
