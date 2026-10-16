# Quickstart: Authentication and Role Management

## Prerequisites

- .NET 10 SDK.
- A non-production SQL Server database with a backup and the existing KafkaDemo schema.
- A deployment-provided JWT signing key, issuer, audience, and token lifetime settings.
- A deployment-provided one-time bootstrap secret and initial Admin credentials.
- VS Code REST Client (or another HTTP client) for API smoke tests.

Never put real JWT signing keys, bootstrap secrets, passwords, or database credentials
in tracked files. Use environment variables, development user secrets, or the
deployment's secret manager.

Configure the database connection using `ConnectionStrings__DefaultConnection`
environment configuration (or .NET user secrets for local development). The checked-in
`appsettings.json` is not a place for deployment-specific database credentials. Run
`Migrations/AuthenticationDataPreflight.sql` against the target SQL Server before
applying the authentication migration; resolve every reported row without deleting or
merging user records.

Configure JWT settings through deployment secrets or environment configuration:
`Jwt__SigningKey` (at least 32 characters), `Jwt__Issuer`, and `Jwt__Audience`.
`Jwt__TokenLifetimeMinutes` defaults to 60. Authentication lockout settings are
`Authentication__MaxFailedLoginAttempts` (default 5) and
`Authentication__LockoutMinutes` (default 5).

For local development, configure required JWT values in .NET user-secrets instead of
adding them to `appsettings.json`:

```powershell
dotnet user-secrets set "Jwt:SigningKey" "<random secret of at least 32 characters>" --project .\KafkaDemo.csproj
dotnet user-secrets set "Jwt:Issuer" "KafkaDemo.Local" --project .\KafkaDemo.csproj
dotnet user-secrets set "Jwt:Audience" "KafkaDemo.Local.Client" --project .\KafkaDemo.csproj
```

The application validates these values at startup and will stop if the signing key,
issuer, or audience is missing or invalid.

Set the operator-controlled, one-time bootstrap secret outside tracked files before
creating the first Admin:

```powershell
dotnet user-secrets set "Authentication:BootstrapSecret" "<random secret of at least 32 characters>" --project .\KafkaDemo.csproj
```

## Registration and login smoke test

The initial authentication increment exposes:

- `POST /api/auth/register` with `username`, `email`, `fullName`, and a password of at
  least 12 characters. Optional `age` must be between 18 and 100. New accounts receive
  only the `User` role.
- `POST /api/auth/login` with `username` (either username or email) and `password`.
  Success returns a signed JWT in `token` plus `expiresAtUtc`; invalid credentials
  return the same generic `401` response.

Import `KafkaDemo.postman_collection.json` into Postman, set `baseUrl` if the API is not
running at `http://localhost:5151`, then run the collection in order. It generates a
unique test account and exercises registration, validation, duplicate detection,
username/email login, indistinguishable login failures, and the existing WebSocket JWT
authentication path. The valid-token WebSocket request is intentionally a normal HTTP
request and expects `400` when authentication succeeds but no WebSocket upgrade is
requested.

Never save real passwords or JWT configuration in a committed request collection.
The initial Admin is created once through `POST /api/auth/bootstrap`, with the
configured secret in `X-Authentication-Bootstrap`. Replays return `409`; missing
bootstrap configuration returns `503`, and an incorrect secret returns `401`.
Afterward, log in as that Admin and use the bearer token for the Admin-only
`/api/user` operations: list (`page`, `pageSize` up to 100), detail, search, legacy
profile creation, profile update/delete, role assignment/removal, status changes, and
legacy credential provisioning. The API rejects changes that would leave an enabled
user roleless or remove/disable the last enabled Admin.

## Safe database rollout

1. Back up the target database.
2. Check existing user email addresses for duplicates after trim/case normalization and
   for values outside the supported length. The migration must stop rather than delete
   or merge conflicting legacy users.
3. Review the generated EF Core SQL migration. Verify it retains all existing user
   profile rows and leaves them disabled/non-login with no assigned roles.
4. Confirm `ConnectionStrings__DefaultConnection` (or its user-secret equivalent)
   targets the intended application database, not SQL Server's `master` database.
5. Apply pending migrations to a disposable SQL Server database first:

   ```powershell
   dotnet ef database update --context AppDbContext
   ```

   Verify the migration and rollback path before using a production database. This
   command applies every pending migration, including the authentication schema and
   the additive `Age`/`CreatedAt` user-profile fields.
6. Configure the validated JWT and authentication options through deployment secrets.
   Do not log those values.

The application must not automatically apply this security/data migration at startup.

## Build and automated tests

From the repository root:

```powershell
dotnet test .\tests\KafkaDemo.Tests\KafkaDemo.Tests.csproj
```

The focused authentication test classes should cover registration, normalized
uniqueness, password-hash verification, login response indistinguishability,
five-attempt/five-minute lockout, JWT validation, role refresh, Admin operations,
legacy provisioning, and bootstrap race/replay. Integration test hosts should use the
existing `WebApplicationFactory<Program>` pattern and SQLite provider, while disabling
Kafka hosted services as current controller tests do.

SQLite tests do not replace applying the migration and checking filtered indexes,
locking, and collation behavior against SQL Server.

## API smoke-test sequence

1. Start the API with a dedicated non-production database and test-only secret
   configuration.
2. Import and run `KafkaDemo.postman_collection.json` against the API using test-only
   credentials.
3. Register a regular account; repeat registration with case-changed username and email
   to verify normalized uniqueness.
4. Log in successfully and use its token on an Admin-only user-management operation;
   confirm `403`. Try missing, malformed, invalid, and expired tokens; confirm `401`.
5. Authenticate as Admin, list/read users, assign/remove roles, toggle enabled status,
   and provision a legacy profile. Verify every result excludes hashes and lockout
   state.
6. Send five failed logins and verify additional attempts fail generically until five
   minutes elapse; verify successful login resets the failure counter.
7. Run the existing non-auth API and Kafka tests to confirm their behavior is unchanged.

Expected status distinctions: unauthenticated `401`, authenticated but unauthorized
`403`, invalid input `400`, duplicate/conflicting state `409`, successful creation
`201`, and successful reads/updates `200`/`204` as specified in the API contract.
