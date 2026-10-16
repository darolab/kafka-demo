# Research: Authentication and Role Management

## Decisions

### Password hashing without the Identity account-management system

- **Decision**: Register and use ASP.NET Core's built-in `IPasswordHasher<User>` directly
  for password hashing and verification. Do not add `AddIdentity`, `AddIdentityCore`,
  `UserManager`, or an Identity schema.
- **Rationale**: The .NET 10 Web SDK already references the ASP.NET Core shared
  framework, so this provides the framework's password-hash implementation without a
  new production dependency or replacing the project's existing EF/controller/service
  architecture. Upgrade a hash after successful verification when the result is
  `SuccessRehashNeeded`.
- **Alternatives considered**: Implement custom password cryptography (rejected due to
  avoidable cryptographic risk); install the full Identity system (rejected because the
  feature needs only password hashing and custom user/role administration is already
  requested).
- **Reference**: [PasswordHasher<TUser> API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.passwordhasher-1?view=aspnetcore-10.0),
  [ASP.NET Core shared framework](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/target-aspnetcore?view=aspnetcore-10.0).

### JWT issuance, validation, and current authorization data

- **Decision**: Use the existing JWT bearer handler and HMAC signing model. Configure
  issuer, audience, signing key, and token lifetime; validate signature, issuer,
  audience, and lifetime explicitly. Keep the existing `userId` claim consumed by the
  WebSocket controller, plus subject/name and role claims. Configure claim mapping
  consistently with the emitted role/name claim types.
- **Rationale**: The app already issues and accepts locally signed JWTs. Explicit issuer
  and audience validation adds validation boundaries without replacing JWT bearer
  authentication. For protected HTTP requests, resolve the persisted user and current
  roles during token validation, rejecting disabled accounts and applying role changes
  on the next request as required by the spec.
- **Alternatives considered**: Continue with signature/lifetime only (rejected because
  issuer and audience are not currently validated); adopt an external token issuer
  (out of scope for the existing self-issued JWT design).
- **Compatibility**: Newly issued tokens contain issuer/audience claims. Existing demo
  tokens without them will not validate after strict validation is enabled; they are
  short-lived demo credentials rather than registered account sessions. Retain query
  token support only on the existing WebSocket connect path.
- **Long-lived WebSocket boundary**: Current authorization state is refreshed when the
  WebSocket handshake authenticates. An already-established socket is not a new HTTP
  authorization request; role/status changes apply on its next connection, not
  asynchronously during an open socket. Do not add multi-instance socket revocation
  infrastructure without a separate requirement.
- **Reference**: [Configure JWT bearer authentication in ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0).

### SQL Server identity, uniqueness, and legacy-data rollout

- **Decision**: Extend the existing `Users` table with nullable username and password
  hash for legacy compatibility; retain existing profile columns and rows. Add bounded
  normalized username/email values for indexed case-insensitive uniqueness, an enabled
  flag, failed-login count, and lockout expiry. Add `Roles`, `UserRoles`, and a singleton
  `AuthenticationBootstrapState` table. Seed only `Admin` and `User`; existing users
  receive no role, no credentials, and disabled status. New registration is enabled and
  assigned `User`.
- **Rationale**: This maps authentication to existing user identities and supports the
  clarified non-login legacy state, while unique database indexes enforce identity
  uniqueness under concurrent registrations.
- **Migration safety**: Before adding unique normalized-email constraints, preflight
  existing emails for duplicates after trim/case normalization and values outside the
  chosen supported email length. Stop the migration and require reviewed remediation
  rather than deleting or merging user data. Preserve original email strings; store a
  normalized comparison value. Add a filtered unique index for nullable normalized
  usernames. Keep migration `Down` limited to new schema objects and newly added
  columns.
- **Concurrency**: Perform registration and bootstrap in a database transaction.
  Bootstrap state must be checked and claimed atomically so concurrent calls establish
  at most one initial Admin. Bootstrap state is a seeded singleton row. Atomically
  claim its uncompleted marker with a conditional database update; create the initial
  account and role in the same transaction and require exactly one row to be claimed.
  A conflict returns a controlled `409`; a failed account insert rolls back the marker
  for safe retry. This provides one-winner behavior without SQL Server-specific stored
  procedures and is testable using the existing SQLite provider. Persisted login-failure
  counters must use atomic updates or concurrency control so simultaneous attempts
  cannot lose increments or bypass the threshold. Protect last-enabled-Admin role/status
  changes in a serialized transaction; handle expected unique-index/concurrency
  conflicts as controlled client errors.
- **Alternative considered**: SQL Server `sp_getapplock` for bootstrap serialization.
  Rejected for the initial design because the singleton compare-and-set transaction
  already provides a durable atomic claim and avoids provider-specific locking logic.
- **References**: [EF Core indexes and uniqueness](https://learn.microsoft.com/en-us/ef/core/modeling/indexes),
  [EF Core concurrency handling](https://learn.microsoft.com/en-us/ef/core/saving/concurrency),
  [EF Core migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing).

### Login lockout

- **Decision**: Keep consecutive failure count and lockout-until timestamp with the
  account's authentication state. Default to five failed attempts and five minutes,
  configurable through validated `Authentication` options. Reset the count on success;
  return the same generic login status and message for unknown, invalid, disabled, and
  locked credentials.
- **Rationale**: Persisted state works across app restarts and multiple backend
  instances. A dummy password-hash verification for missing accounts reduces an
  account-enumeration timing difference. Database uniqueness and concurrency handling
  must prevent concurrent failures from losing increments or bypassing the threshold.
- **Tradeoff**: Account-based lockout allows targeted denial of service. The threshold
  and duration are configurable; broader IP/network rate limiting is not added without
  an established project requirement.

### API shape, errors, and compatibility

- **Decision**: Keep `/api/auth/login` and its existing `{ "token": ... }` response field;
  add `/api/auth/register` and operator-secret-protected `/api/auth/bootstrap`. Keep
  existing `/api/user` paths but require Admin authorization for user-management
  operations, return safe DTOs, and add role, enabled-status, and legacy-credential
  provisioning operations. Keep `/api/sql-users` for route compatibility but protect it
  as Admin-only and stop returning raw database rows. Leave other route policies and
  Kafka behavior unchanged.
- **Rationale**: This explicitly applies the clarification about route access and avoids
  exposing newly stored hashes through existing entity serialization or `SELECT *`.
- **Errors**: Use standard validation problem details for invalid request values, 409
  for duplicate/conflicting state, 401 for generic login/token authentication failure,
  and 403 for authenticated callers without role permission. Never echo credential
  input or log secret-bearing exception/context values. The existing JWT bearer event
  handlers log exceptions and error descriptions; change these to log only a safe
  failure category and request path, never token contents, raw exception text, or
  sensitive claim values.
- **Data access**: Add a focused EF-backed authentication repository using the existing
  `AppDbContext`; retain the current stored-procedure behavior in `UserRepository` for
  its existing compatibility route rather than silently replacing that path.
- **Pagination**: Admin user listing uses bounded page/size parameters (default 50,
  maximum 100) to avoid unbounded user table responses.
- **Alternative considered**: Replace all existing user routes with a new
  `/api/admin/users` hierarchy (rejected to preserve route shapes where practical).

### Testing and API request collection

- **Decision**: Add service-level xUnit tests and HTTP integration tests using the
  existing `WebApplicationFactory<Program>` and SQLite provider. Disable Kafka hosted
  services in these test hosts as current tests do. Add `KafkaDemo.Auth.http` using
  environment variables for secrets and a non-production SQL database.
- **Rationale**: This reuses current test dependencies and test-host conventions. Tests
  cover database uniqueness, registration/login, all JWT negative cases, five-attempt
  lockout/expiry, role checks and refresh, bootstrap concurrency/replay, safe response
  projections, and the unmodified non-user routes/Kafka regression suite.
- **Limitation**: SQLite integration checks do not certify SQL Server-specific locking,
  collation, or migration execution. The bootstrap compare-and-set and counter updates
  must be provider-translated by both supported providers and covered under concurrent
  tests. Validate the generated migration/script against a SQL Server test database
  before deployment; do not apply it automatically on startup.
