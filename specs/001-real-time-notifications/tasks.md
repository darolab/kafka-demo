# Tasks: Real-Time Notifications

**Input**: Design documents from `/specs/001-real-time-notifications/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/notification-stream.md

**Tests**: Included to satisfy the project constitution's test-first requirement. The repository has no existing test project; the setup task adds one.

**Organization**: Tasks are grouped by the four user stories in spec.md and ordered by priority.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because the task edits separate files and has no dependency on incomplete tasks.
- **[Story]**: Maps a task to a user story in spec.md.
- Every task names its exact source, test, or documentation path.

## Phase 1: Setup

**Purpose**: Establish the test harness needed for test-first implementation.

- [X] T001 Create `tests/KafkaDemo.Tests/KafkaDemo.Tests.csproj` targeting `net10.0`, referencing `KafkaDemo.csproj`, and including xUnit, `Microsoft.AspNetCore.Mvc.Testing`, and Testcontainers Kafka dependencies; exclude `tests/**/*.cs` from the root application compile items in `KafkaDemo.csproj`.

## Phase 2: Foundational

**Purpose**: Resolve cross-instance delivery semantics and create shared contracts/configuration before story implementation.

- [X] T002 Update `specs/001-real-time-notifications/research.md`, `specs/001-real-time-notifications/spec.md`, `specs/001-real-time-notifications/plan.md`, `specs/001-real-time-notifications/contracts/notification-stream.md`, and `specs/001-real-time-notifications/data-model.md` to define a versioned schema (`schemaVersion: 1`), one unique stable Kafka consumer group per API instance, userId record keys, at-least-once delivery with client deduplication by notificationId, live-only handling of offline users, a five-second send timeout, and the measured baseline in spec.md.
- [X] T003 [P] Add the v1 serialization contract test in `tests/KafkaDemo.Tests/Contracts/NotificationEnvelopeContractTests.cs` before defining the `NotificationEnvelope` record in `DTO/NotificationEnvelope.cs`; require schemaVersion, notificationId, userId, eventType, correlationId, timestampUtc, and payload to use the documented JSON names.
- [X] T004 [P] Add validated notification configuration in `Services/NotificationOptions.cs` and document `Kafka:NotificationTopic`, the per-instance `Kafka:NotificationConsumerGroupId`, `Notification:InstanceId`, and a five-second socket send timeout in `appsettings.json` and `specs/001-real-time-notifications/quickstart.md`.

**Checkpoint**: Shared event payload and multi-instance routing semantics are fixed; user-story work can begin.

## Phase 3: User Story 1 - Receive account-scoped notifications over WebSocket (Priority: P1)

**Goal**: Authenticated users receive Kafka-backed events through their local WebSocket connections, without cross-user delivery, regardless of which API instance consumes the event.

**Independent Test**: With two API-instance consumer groups and active local sockets, publish one event for user A; user A's socket receives it on the owning instance, user B's socket does not, and an unauthenticated WebSocket connection is rejected.

### Tests for User Story 1

- [X] T005 [P] [US1] Add behavioral tests for per-user routing, multiple sockets, send serialization, and disconnected sockets in `tests/KafkaDemo.Tests/Services/WebSocketConnectionManagerTests.cs` using a recording WebSocket implementation.
- [X] T006 [P] [US1] Add Kafka Testcontainers integration coverage proving two distinct per-instance consumer groups each receive a published notification and dispatch only to matching local user sockets in `tests/KafkaDemo.Tests/Notifications/NotificationFanoutTests.cs`.
- [X] T007 [P] [US1] Add WebSocket contract tests for valid JWT acceptance, missing/invalid identity rejection, and account binding in `tests/KafkaDemo.Tests/Controllers/WebSocketNotificationTests.cs`, plus unsupported-version and offline-outcome tests for `Services/NotificationDispatcher.cs` in `tests/KafkaDemo.Tests/Services/NotificationDispatcherTests.cs`.

### Implementation for User Story 1

- [X] T008 Implement `NotificationDispatcher` in `Services/NotificationDispatcher.cs` to validate envelope identity/metadata, serialize the contract, route through `IWebSocketConnectionManager`, and return a delivery outcome for connected and offline users.
- [X] T009 Implement `NotificationConsumerService` in `Services/NotificationConsumerService.cs` as a hosted Kafka consumer using the configured stable per-instance group ID; consume all events, dispatch locally by userId, and commit offsets only after dispatch outcome handling completes.
- [X] T010 Extend `Services/WebSocketConnectionManager.cs` and `Services/IWebSocketConnectionManager.cs` to track per-connection user, instance, and lifecycle timestamps, safely remove stale/disconnected sockets, and retain serialized sends.
- [X] T011 Register notification options, dispatcher, Kafka consumer hosted service, and WebSocket delivery dependencies in `Program.cs`; load JWT signing from external configuration and fail closed for a missing or shorter-than-32-byte key without changing the existing route or account-binding behavior.

**Checkpoint**: User Story 1 works independently when a notification envelope is published to Kafka.

## Phase 4: User Story 2 - Backend events generate notifications without disrupting existing APIs (Priority: P1)

**Goal**: Generate account-scoped notification events from an existing backend flow using SQL Server outbox persistence and the existing Kafka producer, while keeping public REST and WebSocket request contracts stable.

**Independent Test**: Sending a private message persists both the message and its notification outbox record atomically; the outbox worker publishes the record keyed by recipient user ID; representative existing REST requests retain their established response contracts.

### Tests for User Story 2

- [X] T012 [P] [US2] Add tests proving message creation and notification outbox insertion are atomic and the Kafka key is the recipient user ID in `tests/KafkaDemo.Tests/Services/PrivateMessageNotificationTests.cs`.
- [X] T013 [P] [US2] Add regression tests for existing transaction REST status and response shape in `tests/KafkaDemo.Tests/Controllers/TransactionsControllerTests.cs` while notification services are registered.

### Implementation for User Story 2

- [X] T014 Add a Kafka message key and atomic claim/lease fields to `Models/OutboxMessage.cs`, configure constraints in `Data/AppDbContext.cs`, and generate the corresponding EF Core migration under `Migrations/`.
- [X] T015 Update `Works/OutboxWorker.cs` to atomically claim unpublished rows with a SQL Server lease so concurrent API instances cannot publish the same row simultaneously; publish with the outbox message key and preserve retry diagnostics.
- [X] T016 Update `Services/PrivateMessageService.cs` to persist a `NotificationEnvelope` outbox row in the same database save/transaction as the chat message, keyed by recipient userId, and avoid a second local-only socket send for that event.
- [X] T017 Register and enable `OutboxWorker` in `Program.cs`, preserving existing REST endpoints and ensuring cancellation and worker failures do not fail unrelated HTTP requests.

**Checkpoint**: Existing private-message backend events flow through the shared Kafka notification path; existing REST contracts remain unchanged.

## Phase 5: User Story 3 - Reconnect and recover after disconnects (Priority: P2)

**Goal**: Clean up dropped socket state and allow an authenticated client to reconnect and receive subsequent live notifications.

**Independent Test**: A socket that closes unexpectedly is removed from its owning instance; reconnecting with a valid JWT creates a fresh session that receives subsequent notifications, while an invalid/expired token is rejected.

### Tests for User Story 3

- [X] T018 [P] [US3] Add tests for unexpected disconnect cleanup, repeated reconnects, and subsequent notification delivery in `tests/KafkaDemo.Tests/Controllers/WebSocketReconnectTests.cs`.

### Implementation for User Story 3

- [X] T019 Harden connection registration/removal and close/cancellation handling in `Controllers/WebsocketController.cs` and `Services/WebSocketConnectionManager.cs`; ensure stale sessions cannot remain routable and reconnect does not require process restart.

**Checkpoint**: User Story 3 recovers future live delivery after reconnect without changing authentication requirements.

## Phase 6: User Story 4 - Operators can diagnose missing notification delivery (Priority: P2)

**Goal**: Record event, target-user, connection, instance, correlation, and outcome information sufficient to investigate delivered, offline, rejected, and failed notifications.

**Independent Test**: For delivered and offline events, persisted audit records and metrics/logs identify the notification and outcome without storing full sensitive payloads.

### Tests for User Story 4

- [X] T020 [P] [US4] Add persistence tests for delivered, offline, rejected, and failed notification audit outcomes in `tests/KafkaDemo.Tests/Notifications/NotificationDeliveryAuditTests.cs`.

### Implementation for User Story 4

- [X] T021 Add `Models/NotificationDeliveryAudit.cs`, its `DbSet` and SQL Server mapping in `Data/AppDbContext.cs`, and an EF Core migration under `Migrations/` for notification delivery audit records.
- [X] T022 Update `Services/NotificationDispatcher.cs` to persist one outcome record per delivery attempt and emit structured logs containing notificationId, userId, connectionId when present, instanceId, correlationId, eventType, and outcome without logging the full payload.
- [X] T023 Add a `MeterListener` test in `tests/KafkaDemo.Tests/Services/NotificationMetricsTests.cs`, then add notification and connection counters using `System.Diagnostics.Metrics` in `Services/NotificationMetrics.cs` and register the meter with the existing Prometheus/OpenTelemetry pipeline in `Program.cs`.

**Checkpoint**: Operators can correlate notification events and delivery outcomes using audit records, structured logs, and Prometheus metrics.

## Phase 7: Polish and Cross-Cutting Concerns

**Purpose**: Document deployment configuration and prove the feature's end-to-end path.

- [X] T024 Update `specs/001-real-time-notifications/quickstart.md` with the Kafka topic, distinct stable consumer group ID per API instance, SQL migration, authenticated WebSocket, live-only offline, reconnect, and Prometheus validation steps.
- [X] T025 Run `dotnet test tests/KafkaDemo.Tests/KafkaDemo.Tests.csproj`, `dotnet build KafkaDemo.csproj`, and the available quickstart checks; record unavailable external services and the required dedicated database configuration in `specs/001-real-time-notifications/quickstart.md`.

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001 creates the test project; no other task can run before it.
- **Foundational (Phase 2)**: T002 fixes the distributed-delivery contract; T003's contract test precedes DTO implementation, while T004 can run in parallel. All user stories depend on this phase.
- **User Story 1 (Phase 3)**: Depends on the foundational phase and establishes the shared notification consumer/dispatcher.
- **User Story 2 (Phase 4)**: Depends on User Story 1 because it produces envelopes for the established consumer. T012 and T013 can run in parallel; the implementation tasks are sequential because they modify the outbox flow and `Program.cs`.
- **User Story 3 (Phase 5)**: Depends on User Story 1's connection manager and consumer. Its test can be prepared in parallel with later story tests; implementation follows T018.
- **User Story 4 (Phase 6)**: Depends on User Story 1's dispatcher. T020 precedes audit persistence; audit persistence precedes dispatcher changes. Metrics registration must be integrated after the other `Program.cs` changes.
- **Polish (Phase 7)**: Depends on all selected user stories.

### User Story Dependencies

- **US1 (P1)**: Starts after Setup and Foundational; no dependency on another user story.
- **US2 (P1)**: Depends on US1's event envelope and Kafka consumer.
- **US3 (P2)**: Depends on US1's WebSocket connection manager.
- **US4 (P2)**: Depends on US1's dispatcher.

### Parallel Opportunities

- After T001 and T002, T003 and T004 can run in parallel.
- Within US1, T005, T006, and T007 are separate test files and can run in parallel; implementation follows those tests.
- Within US2, T012 and T013 are separate test files and can run in parallel.
- US3 reconnect tests and US4 audit tests can be authored in parallel after their shared US1 contracts exist. Implementation tasks touching `Services/NotificationDispatcher.cs`, `Services/WebSocketConnectionManager.cs`, or `Program.cs` should be integrated sequentially to avoid conflicts.

## Parallel Example: User Story 1

```text
Task: T005 WebSocketConnectionManager behavior tests in tests/KafkaDemo.Tests/Services/WebSocketConnectionManagerTests.cs
Task: T006 Kafka fan-out integration tests in tests/KafkaDemo.Tests/Notifications/NotificationFanoutTests.cs
Task: T007 WebSocket authentication contract tests in tests/KafkaDemo.Tests/Controllers/WebSocketNotificationTests.cs
```

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational phases.
2. Complete US1 to prove authenticated, account-scoped, multi-instance WebSocket delivery from a Kafka event.
3. Complete US2 to connect a real backend event source through the existing outbox and Kafka producer; US1 plus US2 is the first end-to-end customer-facing MVP.
4. Validate each story independently before proceeding to reconnect and operator diagnostics.

### Incremental Delivery

- Deliver US1 as the transport/routing increment.
- Add US2 as the event-source increment without altering REST contracts.
- Add US3 reconnect recovery.
- Add US4 persisted diagnostics and metrics.
- Run the quickstart against the configured Kafka and SQL Server instances before release.

### Parallel Team Strategy

- Complete Setup and Foundational decisions together.
- Parallelize the independent US1 test files, then implement US1 integration.
- After US1 is stable, parallelize US2 regression tests, US3 reconnect tests, and US4 audit tests; integrate shared source-file edits sequentially.

## Notes

- `[P]` tasks only edit separate files and have no dependency on incomplete tasks.
- `[US1]` through `[US4]` map to the user stories in `specs/001-real-time-notifications/spec.md`.
- Tests are included because `.specify/memory/constitution.md` requires test-first delivery.
- Kafka delivery is at-least-once; `notificationId` is the client-visible deduplication key.
- The per-instance consumer group identifier must be distinct between simultaneously running API instances and stable across a given instance's restart.
- No extension hooks are configured in `.specify/extensions.yml`.

## Phase 8: Convergence

**Purpose**: Close verified gaps found by comparing the implementation with the specification and plan.

- [ ] T026 Add a test asserting that each WebSocket delivery audit identifies the exact socket connection that received or rejected the send in `tests/KafkaDemo.Tests/Notifications/NotificationDeliveryAuditTests.cs` per FR-011 and US4/AC1 (partial).
- [ ] T027 Return per-connection delivery outcomes including `ConnectionId` and `InstanceId` from `Services/IWebSocketConnectionManager.cs` and `Services/WebSocketConnectionManager.cs`; persist each result in `Models/NotificationDeliveryAudit.cs` through `Services/NotificationDispatcher.cs` and log connection IDs on connect/disconnect in `Controllers/WebsocketController.cs` per FR-011 and US4/AC1 (partial).
- [ ] T028 Add a Kafka topic provisioning and verification step for configured `Kafka:NotificationTopic` to `specs/001-real-time-notifications/quickstart.md`, including development and deployment partition/replication assumptions, per FR-004 and plan: Kafka topic contract (missing).
