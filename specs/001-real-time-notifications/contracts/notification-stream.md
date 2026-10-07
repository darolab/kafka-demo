# Notification Stream Contract

## WebSocket endpoint

**Method**: `GET`
**Path**: `/api/WebSocket/connect`
**Authentication**: JWT bearer token required
**Transport**: WebSocket

### Request notes

- The token must be present and valid for the current user.
- The connection is bound to the authenticated user identity and cannot be used to subscribe to another account.
- The server tracks the connection lifecycle and cleans up stale sessions.

## Outbound notification payload

```json
{
  "schemaVersion": 1,
  "notificationId": "uuid",
  "userId": 1002,
  "eventType": "transaction.created",
  "correlationId": "trace-or-event-id",
  "timestampUtc": "2026-10-06T12:34:56.000Z",
  "payload": {
    "message": "Example notification body",
    "source": "backend-event"
  }
}
```

## Kafka event contract

The backend event used for notification fan-out should include the target user and trace metadata so that each API instance can route messages to the correct connection set.

```json
{
  "schemaVersion": 1,
  "notificationId": "uuid",
  "userId": 1002,
  "eventType": "transaction.created",
  "correlationId": "trace-or-event-id",
  "timestampUtc": "2026-10-06T12:34:56.000Z",
  "payload": {
    "message": "Example notification body"
  }
}
```

## Delivery guarantees

- `schemaVersion` is required and is `1` for this contract version. Consumers reject unsupported versions with a diagnostic and do not deliver them.
- Each API instance uses a distinct, stable Kafka consumer group; Kafka records are keyed by `userId`, and each instance dispatches only to its local connections.
- Delivery is at-least-once. Clients use `notificationId` to deduplicate events.
- Delivery is live-only in v1. If no instance has an active socket for the target user, the event is audited and is not replayed after reconnect.
- Messages are delivered only to the intended user.
- The connection model must not assume a single process owns every socket.
- Delivery attempts must be logged with enough metadata to debug missed or failed sends.
- A WebSocket send is limited to five seconds; a timed-out connection is removed and must reconnect.
- If a user is offline, the event is safely audited without causing a crash or API regression.
