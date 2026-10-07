# Implementation Plan: Real-Time Notifications

**Branch**: `001-real-time-notifications` | **Date**: 2026-10-06 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-real-time-notifications/spec.md`

## Summary

Add an authenticated WebSocket notification stream to the existing KafkaDemo ASP.NET Core backend using the current JWT authentication flow and Kafka event infrastructure. The feature will emit per-user notifications from backend events, route them through a shared event bus so multiple API instances can deliver messages correctly, and preserve the existing REST surface while tracking connection and delivery diagnostics.

## Technical Context

**Language/Version**: .NET 10 ASP.NET Core

**Primary Dependencies**: ASP.NET Core Web API, JWT Bearer auth, Confluent Kafka, Entity Framework Core, SQL Server, WebSockets, OpenTelemetry Prometheus exporter, existing Grafana dashboards

**Storage**: SQL Server via `AppDbContext`; Kafka is the event transport; delivery outcomes are persisted in SQL Server, while active WebSocket connections remain instance-local runtime state

**Testing**: xUnit on .NET 10, ASP.NET Core integration testing, Testcontainers for Kafka, and SQL Server-backed tests for outbox and audit persistence

**Target Platform**: Windows/Linux server deployment running the existing ASP.NET Core API

**Project Type**: Web service

**Performance Goals**: at least 95% of sends complete within five seconds under the baseline in `spec.md` (two API instances, 100 connected clients, 10 events/second total, healthy Kafka, no backlog); a WebSocket send times out after five seconds

**Constraints**: maintain current REST contracts; no cross-user delivery; use a distinct stable Kafka consumer group per API instance and user ID as the record key; delivery is at-least-once with client deduplication by notificationId; offline events are live-only; notification envelope schema is versioned and unsupported versions fail closed

**Scale/Scope**: multi-instance API deployment with per-user WebSocket routing and Kafka-backed event fan-out

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- Secure-by-default design: PASS. Authenticated WebSocket access must use the current JWT flow and fail closed for invalid or expired sessions.
- Contract-first integration: PASS. The WebSocket channel, downstream event payloads, and Kafka topic contract require clear user and event metadata.
- Test-first delivery: PASS. Notification delivery, auth gating, and reconnect behavior must be covered with targeted regression tests.
- Observable operations: PASS. Delivery and connection diagnostics must be logged with correlation metadata and user context.
- Simplicity and maintainability: PASS. The design keeps the current controller/service architecture and introduces minimal supporting abstractions.

## Project Structure

### Documentation (this feature)

```text
specs/001-real-time-notifications/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
├── spec.md              # Feature specification
└── checklists/
    └── requirements.md
```

### Source Code (repository root)

```text
Controllers/
├── AuthController.cs
├── WebsocketController.cs
├── UserController.cs
├── MessageController.cs
├── TransactionsController.cs
├── PostsController.cs
├── ConfigController.cs
└── ...

Services/
├── KafkaProduceService.cs
├── NotificationConsumerService.cs
├── NotificationDispatcher.cs
├── NotificationMetrics.cs
├── PrivateMessageService.cs
├── VaultService.cs
├── TransactionService.cs
├── WebSocketConnectionManager.cs
├── PostApiClient.cs

Data/
├── AppDbContext.cs

Models/
├── ChatMessage.cs
├── User.cs
├── Transaction.cs
├── Post.cs
├── OutboxMessage.cs
├── NotificationDeliveryAudit.cs

DTO/
└── NotificationEnvelope.cs

tests/KafkaDemo.Tests/
├── Contracts/
├── Controllers/
├── Notifications/
└── Services/

Works/
├── OutboxWorker.cs
```

**Structure Decision**: Extend the current controller/service model already used in the repository. Each API instance consumes Kafka independently with a distinct stable group ID, then routes to its instance-local WebSocket registry. No SignalR or separate service is introduced.

## Complexity Tracking

No constitution violations require an exception for this feature. The design remains aligned with the existing architecture and security rules.
