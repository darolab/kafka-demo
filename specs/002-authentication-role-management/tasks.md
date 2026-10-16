# Tasks: Authentication and Role Management

**Input**: Design documents from `/specs/002-authentication-role-management/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/authentication-api.md, quickstart.md

**Tests**: Automated tests are required by the feature specification. Write the story tests first and confirm they fail before implementing each story.

**Organization**: Tasks are grouped by the four prioritized user stories. Authentication, role, migration, and API work is split into independently reviewable tasks with explicit dependencies.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel after its stated prerequisites, in a different file from other parallel tasks
- **[Story]**: User story label for story-phase tasks only
- Every task names its target file or files

## Phase 1: Setup

**Purpose**: Prepare a safe, repeatable foundation for the authentication schema rollout.

- [X] T001 Create `Migrations/AuthenticationDataPreflight.sql` to report duplicate trimmed/case-normalized legacy emails and emails exceeding 320 characters without changing or deleting any rows.
- [X] T002 Remove real environment-specific database credentials from `appsettings.json` and document required connection-string environment configuration in `specs/002-authentication-role-management/quickstart.md`.

---

## Phase 2: Foundational

**Purpose**: Shared account/role schema and validated options required by all user stories.

**⚠️ CRITICAL**: Complete this phase before implementing any user story.

- [X] T003 [P] Create `Models/Role.cs` and `Models/UserRole.cs`; constrain role names to supported `Admin` and `User` values (maximum 32 characters), and use `(UserId, RoleId)` as the composite key.
- [X] T004 [P] Create `Models/AuthenticationBootstrapState.cs` with a singleton fixed key and nullable `CompletedAtUtc`, which is never cleared after bootstrap completes.
- [X] T005 Extend `Models/User.cs` with nullable legacy-compatible `Username`, `NormalizedUsername`, and `PasswordHash`; add required `NormalizedEmail` (maximum 320), `IsEnabled` (existing users migrate disabled), `FailedLoginCount` (default 0), and nullable UTC `LockoutUntilUtc`, preserving `Id`, `Name`, `Email`, `Age`, and `CreatedAt`.
- [X] T006 Configure `Data/AppDbContext.cs` for `Roles`, `UserRoles`, and bootstrap state; enforce unique role names, composite user-role keys, filtered unique non-null normalized usernames, and unique normalized emails, and seed `Admin`, `User`, and the incomplete singleton bootstrap marker (depends on T003–T005).
- [X] T007 Add `Services/AuthenticationOptions.cs` with validated defaults of five consecutive failures and a five-minute lockout, and extend `Services/JwtOptions.cs` with required signing key, issuer, audience, and token lifetime settings (default 60 minutes).
- [X] T008 Generate the additive EF migration and model snapshot under `Migrations/`, preserving all legacy profiles as disabled users without credentials or role links; include bounded normalized-email backfill and safe rollback of only added schema (depends on T001, T006).
- [X] T009 Create `tests/KafkaDemo.Tests/AuthenticationTestFactory.cs` to use `WebApplicationFactory<Program>` with isolated SQLite storage and Kafka hosted services disabled, following the existing controller-test host pattern.

**Checkpoint**: The schema, validated settings, migration preflight, and shared test host are ready for story implementation.

---

## Phase 3: User Story 1 - Register and sign in securely (Priority: P1)

**Goal**: Register a validated account with a securely hashed password and default `User` role; authenticate accounts with generic failure responses and configured lockout.

**Independent Test**: Registration, duplicate and validation failures, successful/failed login, safe responses, five-attempt lockout, expiry, and reset are covered without requiring an Admin management API.

### Tests for User Story 1

- [X] T010 [P] [US1] Add `tests/KafkaDemo.Tests/Services/AuthenticationServiceTests.cs` cases for password hashing/verification, email and username normalization, generic invalid-login outcomes, consecutive failure counting, five-minute unlock, success reset, and `SuccessRehashNeeded`.
- [X] T011 [P] [US1] Add `tests/KafkaDemo.Tests/Controllers/AuthenticationApiTests.cs` integration cases for registration success, invalid registration, case-insensitive duplicate username/email, login success/failure, generic failure responses, and absence of password/hash in all responses.

### Implementation for User Story 1

- [X] T012 [P] [US1] Create `DTO/AuthenticationRequestDTOs.cs` request types for registration and login; require username (maximum 64), email (maximum 320), full name, and password (minimum 12 characters), and do not accept roles on public registration.
- [X] T013 [P] [US1] Create `DTO/UserResponseDTO.cs` with only safe profile fields and role names; exclude `PasswordHash`, password, lockout counters, and internal authentication state.
- [X] T014 [US1] Create `Repository/AuthenticationRepository.cs` account lookups and registration persistence using `Data/AppDbContext.cs`; handle case-normalized uniqueness with database constraints and translate expected uniqueness conflicts to domain errors (depends on T006).
- [X] T015 [US1] Implement registration, login, and persistent lockout in `Services/AuthenticationService.cs` using `IPasswordHasher<User>`; verify unknown users with a dummy hash, increment failures atomically, return indistinguishable failures, reset on success, and rehash when required (depends on T007, T014).
- [X] T016 [US1] Replace demo-account logic in `Controllers/AuthController.cs` with `POST /api/auth/register` and database-backed `POST /api/auth/login`; preserve the login response `token` field and return only `DTO/UserResponseDTO.cs` for registration (depends on T012, T013, T015).
- [X] T017 [US1] Register the password hasher, authentication service/repository, and validated authentication options in `Program.cs`; validate required settings at startup without logging secret values (depends on T007, T014, T015).
- [X] T018 [US1] Run and refine the US1 tests in `tests/KafkaDemo.Tests/Services/AuthenticationServiceTests.cs` and `tests/KafkaDemo.Tests/Controllers/AuthenticationApiTests.cs`; ensure all registration/login/lockout scenarios pass and no credential data is exposed.

**Checkpoint**: Users can safely register and sign in; invalid credentials remain generic and lockout behavior is durable.

---

## Phase 4: User Story 2 - Access protected services according to identity and role (Priority: P1)

**Goal**: Issue and validate JWTs, reject unauthenticated requests, distinguish forbidden requests, and enforce current account status/roles on protected HTTP requests.

**Independent Test**: Missing, malformed, invalid, and expired tokens receive 401; valid Users receive 403 on Admin-only routes while Admins are authorized; current role/status changes apply on the next HTTP request.

### Tests for User Story 2

- [X] T019 [P] [US2] Extend `tests/KafkaDemo.Tests/Controllers/AuthenticationApiTests.cs` with missing, malformed, invalid-signature, wrong-issuer, wrong-audience, and expired-token cases plus 401-versus-403 assertions.
- [X] T020 [P] [US2] Add `tests/KafkaDemo.Tests/Services/CurrentUserAuthorizationTests.cs` coverage for disabled-account rejection and role/status refresh on subsequent requests using the persisted account state.

### Implementation for User Story 2

- [X] T021 [US2] Update `Services/JwtOptions.cs` and `Services/JwtBearerOptionsPostConfigure.cs` to require and validate signing key, issuer, audience, lifetime, signature, and consistent role/name claim types while retaining the existing `userId` claim used by `Controllers/WebsocketController.cs`.
- [X] T022 [US2] Extend `Services/AuthenticationService.cs` to issue signed JWTs with `sub`, compatibility `userId`, name, role claims, configured issuer/audience, and configured expiration; return the `expiresAtUtc` contract value.
- [X] T023 [US2] Add current-account JWT validation in `Program.cs` using a scoped account/role lookup during bearer validation; reject disabled/missing accounts and replace token role claims with persisted current roles for the request (depends on T014, T021).
- [X] T024 [US2] Replace sensitive JWT diagnostics in `Program.cs` with safe failure categories and request paths only; do not log exception text, token/error descriptions, credential values, or sensitive claims.
- [X] T025 [US2] Configure the Admin authorization policy and preserve query-string JWT handling only for `/api/WebSocket/connect` in `Program.cs`; retain existing non-user route authentication policies.
- [X] T026 [US2] Run and refine `tests/KafkaDemo.Tests/Controllers/AuthenticationApiTests.cs` and `tests/KafkaDemo.Tests/Services/CurrentUserAuthorizationTests.cs`; verify 401/403 distinction, current role/status enforcement, and existing WebSocket JWT handshake compatibility.

**Checkpoint**: JWT authentication and authorization distinguish identity failure from permission failure and use current account state.

---

## Phase 5: User Story 3 - Administrators manage accounts and roles (Priority: P2)

**Goal**: Admins can safely view/provision users, assign/remove roles, and enable/disable accounts; ordinary users cannot administer accounts or grant themselves privileges.

**Independent Test**: Admin-only listing, details, provisioning, role and status operations work; User and anonymous callers are denied; unsupported states and last-Admin removal are rejected.

### Tests for User Story 3

- [X] T027 [P] [US3] Add `tests/KafkaDemo.Tests/Controllers/UserManagementApiTests.cs` tests for Admin list/details/search, safe DTOs, role assignment/removal, status changes, legacy provisioning, and 401/403 access denial.
- [X] T028 [P] [US3] Add `tests/KafkaDemo.Tests/Controllers/AuthenticationBootstrapApiTests.cs` with bootstrap invalid-secret, completed/replay, concurrent first-admin, atomic rollback/retry, and exactly-one-admin cases.

### Implementation for User Story 3

- [X] T029 [P] [US3] Extend `DTO/AuthenticationRequestDTOs.cs` with bootstrap, role-assignment, account-status, profile-update, and legacy-provisioning request DTOs; validate supported role names and prevent client-controlled privilege fields.
- [X] T030 [US3] Extend `Repository/AuthenticationRepository.cs` with paginated safe user projections (default page size 50, maximum 100), role/status changes, legacy provisioning, and last-enabled-Admin/last-role checks in transactional persistence.
- [X] T031 [US3] Implement atomic one-time bootstrap in `Repository/AuthenticationRepository.cs` and `Services/AuthenticationService.cs` using a conditional update of the singleton completion marker and account/role creation in the same transaction; return conflict unless exactly one request claims the marker.
- [X] T032 [US3] Add `POST /api/auth/bootstrap` to `Controllers/AuthController.cs`; require the deployment-provided secret in a dedicated header, never log it, and return generic invalid-secret and completed-bootstrap responses (depends on T029, T031).
- [X] T033 [US3] Update `Controllers/UserController.cs` to require Admin authorization for every existing route, return safe DTOs instead of `User` entities, preserve existing route shapes, and expose role/status/legacy-provisioning endpoints (depends on T023, T029, T030).
- [X] T034 [US3] Protect `Controllers/SqlUsersController.cs` with Admin authorization, replace `SELECT *` and raw dictionaries with safe user projections, and preserve `/api/sql-users` route compatibility (depends on T030).
- [X] T035 [US3] Register bootstrap, user-management repository/service dependencies and Admin policies in `Program.cs`; validate deployment secret configuration and ensure disabled/roleless accounts cannot be enabled (depends on T031–T034).
- [X] T036 [US3] Run and refine `tests/KafkaDemo.Tests/Controllers/UserManagementApiTests.cs` and `tests/KafkaDemo.Tests/Controllers/AuthenticationBootstrapApiTests.cs`; verify bootstrap race/replay protection, legacy provisioning, role invariants, and no sensitive response fields.

**Checkpoint**: Admin operations are protected, safe, and preserve minimum-administrator and account-role invariants.

---

## Phase 6: User Story 4 - Verify the feature without a frontend (Priority: P2)

**Goal**: Provide a runnable API request collection and repeatable acceptance coverage without a frontend, while guarding existing REST and Kafka behavior.

**Independent Test**: A reviewer can configure test-only variables, exercise the documented HTTP flows, run the full automated suite, and confirm existing Kafka/non-user API regressions remain clear.

### Tests and API collection for User Story 4

- [ ] T037 [P] [US4] Create `KafkaDemo.Auth.http` with REST Client environment placeholders for API host, bootstrap secret, Admin/User credentials, and tokens; include register success/validation/duplicates, login success/failure, missing/invalid/expired JWT, role denial/Admin access, role changes, user-management authorization, bootstrap replay, and lockout scenarios.
- [ ] T038 [P] [US4] Add contract assertions to `tests/KafkaDemo.Tests/Controllers/UserManagementApiTests.cs` for preserved non-user REST access behavior and safe response shapes across every user-management route, including `/api/sql-users` and the existing stored-procedure route.
- [ ] T039 [US4] Update `specs/002-authentication-role-management/quickstart.md` to match the final route, environment-variable, expected-status, test-command, and migration rollout details after implementation.
- [ ] T040 [US4] Run the complete `tests/KafkaDemo.Tests/KafkaDemo.Tests.csproj` suite and resolve authentication regressions without changing unrelated REST or Kafka behavior.

**Checkpoint**: Automated coverage and an independently runnable API collection are available; existing supported workflows retain their behavior.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final safety, migration, and documentation review across user stories.

- [ ] T041 [P] Review generated SQL Server migration SQL and `Migrations/AuthenticationDataPreflight.sql` against a populated disposable database; confirm no user rows are removed/merged and rollback removes only feature-added schema.
- [ ] T042 [P] Review `Program.cs`, `Services/AuthenticationService.cs`, `Controllers/AuthController.cs`, and both user-management controllers for credential/token/secret leakage in logs, exceptions, and serialized responses.
- [ ] T043 Update `specs/002-authentication-role-management/quickstart.md` with observed SQL Server migration validation and API-collection execution results; document any environment-specific prerequisites without including secrets.
- [ ] T044 Run all acceptance steps in `specs/002-authentication-role-management/quickstart.md` and ensure they pass against a non-production SQL Server database.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No code dependencies; preflight script and secret-configuration cleanup may proceed independently.
- **Foundational (Phase 2)**: Depends on setup. Model, options, and test-host tasks form prerequisites for all user stories; migration depends on the finalized model.
- **User Story 1 (Phase 3)**: Depends on the schema/options/test-host foundation. Delivers registration and login MVP.
- **User Story 2 (Phase 4)**: Depends on User Story 1 token issuance and account repository. Adds strict validation and current authorization state.
- **User Story 3 (Phase 5)**: Depends on User Story 2 authorization and User Story 1 account services.
- **User Story 4 (Phase 6)**: Depends on the completed authentication and administration contracts from User Stories 1–3.
- **Polish (Phase 7)**: Depends on all desired user stories and the completed migration.

### User Story Dependencies

- **US1 (P1)**: Starts after Phase 2; no dependency on other stories.
- **US2 (P1)**: Depends on US1 token issuance and authentication repository.
- **US3 (P2)**: Depends on US1 account service and US2 Admin policy/current-account validation.
- **US4 (P2)**: Depends on US1–US3 contracts and routes.

### Parallel Opportunities

- T003 and T004 can be developed in parallel; T005 is a separate model file. T006 waits for T003–T005.
- T007 and T009 can proceed in parallel with the entity-model tasks. T008 waits on the migration preflight and model.
- US1 request/response DTO tasks T012 and T013 can proceed alongside the US1 tests; repository, service, and controller tasks remain dependency-ordered.
- US2 JWT matrix test T019 and current-account service test T020 can be authored in parallel.
- US3 admin API tests T027 and bootstrap tests T028 can be authored in parallel; bootstrap DTO task T029 is independent of both test files.
- US4 request collection T037 and response-contract tests T038 can proceed in parallel after the preceding stories.
- Polish reviews T041 and T042 can proceed in parallel after implementation; quickstart result updates and final validation follow.

## Parallel Example: User Story 1

```text
After Phase 2:
Task T010: AuthenticationService unit tests in tests/KafkaDemo.Tests/Services/AuthenticationServiceTests.cs
Task T011: Authentication API tests in tests/KafkaDemo.Tests/Controllers/AuthenticationApiTests.cs
Task T012: Authentication request DTOs in DTO/AuthenticationRequestDTOs.cs
Task T013: Safe response DTO in DTO/UserResponseDTO.cs
```

## Implementation Strategy

### MVP First (User Story 1)

1. Complete Setup and Foundational phases, including legacy data preflight and migration review.
2. Complete US1 registration, hashed password storage, database login, and lockout.
3. **Stop and validate** registration/login against its independent tests before proceeding.
4. Add US2 JWT validation/current authorization, then US3 administration and bootstrap.
5. Deliver US4's API collection and run migration and full regression checks.

### Incremental Delivery

1. Setup + Foundation → safe schema and validated configuration.
2. US1 → independently testable secure registration/login MVP.
3. US2 → validated JWT and role-aware current account identity.
4. US3 → Admin account/role/status management and bootstrap.
5. US4 → no-frontend acceptance collection and cross-system regression validation.

## Notes

- Every task remains unchecked; task completion is recorded by the implementation workflow, not by this planning command.
- `[P]` marks only independent work in separate files after prerequisites are satisfied.
- All test tasks are included because the specification requires automated authentication/authorization tests and API acceptance coverage.
- Use the user-story independent test criteria above as checkpoints; do not implement later stories by weakening earlier security requirements.
