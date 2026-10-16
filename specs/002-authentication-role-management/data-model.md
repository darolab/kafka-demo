# Data Model: Authentication and Role Management

## Existing user profile

Extend the existing `KafkaDemo.Models.User` row rather than creating a parallel user
identity. Retain `Id`, `Name`, `Email`, `Age`, and `CreatedAt`. Keep existing profile
data intact through the migration.

| Field | Type / nullability | Rules |
|---|---|---|
| `Id` | Existing integer primary key | Stable identity used by existing APIs and WebSocket `userId` claim |
| `Name` | Existing string, required | Full name; registration requires a non-empty value |
| `Email` | Existing string, required | Display/original value retained; compared using a trimmed, case-normalized value |
| `NormalizedEmail` | String, required, bounded to 320 | Deterministic trim + invariant uppercase; unique database index |
| `Username` | String, nullable for legacy rows, max 64 | New/provisioned account login name |
| `NormalizedUsername` | String, nullable, max 64 | Trim + invariant uppercase; filtered unique index, allowing legacy nulls |
| `PasswordHash` | String, nullable for legacy rows | Generated and verified only with the framework password hasher; never returned/logged |
| `IsEnabled` | Boolean, required | Existing rows migrate disabled; new registrations are enabled |
| `FailedLoginCount` | Integer, required, default 0 | Consecutive failed attempts; reset after successful login or expired lockout per policy |
| `LockoutUntilUtc` | UTC timestamp, nullable | Login is rejected while current time precedes this value |
| `Age` | Existing nullable integer | Preserved optional profile field |
| `CreatedAt` | Existing UTC timestamp | Preserved |

Legacy records have null username/password hash, no role links, and `IsEnabled = false`;
they cannot authenticate. Admin provisioning fills normalized login identifiers and
password hash, assigns at least `User`, and enables the account within one transaction.

## Role

| Field | Type | Rules |
|---|---|---|
| `Id` | Integer primary key | Internal role key |
| `Name` | String, unique, max 32 | Only `Admin` and `User` are seeded/supported |

No public request may create roles or grant `Admin`.

## UserRole

| Field | Type | Rules |
|---|---|---|
| `UserId` | Integer foreign key to `Users.Id` | Part of composite primary key |
| `RoleId` | Integer foreign key to `Roles.Id` | Part of composite primary key |

An account may have multiple supported roles. Every enabled account must have at least
one role. Restrict deletion of role/user references as needed to preserve relationship
integrity; deleting a legacy profile remains an Admin-only existing operation.

## AuthenticationBootstrapState

| Field | Type | Rules |
|---|---|---|
| `Id` | Integer primary key | Singleton row (fixed key) |
| `CompletedAtUtc` | UTC timestamp, nullable | Set atomically when the first Admin is created; never cleared by disable/delete |

The persistent completion marker prevents bootstrap from reopening if the initial
administrator is later disabled or removed. Concurrent bootstrap requests use a
conditional database update from incomplete to completed and require exactly one
affected row before creating the account and role in the same transaction. A losing
request receives a conflict; failure to create the account rolls back the marker and
allows a safe retry.

## Important indexes and constraints

- Unique index on `NormalizedEmail`; preflight and resolve legacy duplicates before
  migration. Never auto-delete/merge user rows to make the index succeed.
- Filtered unique index on non-null `NormalizedUsername`, allowing legacy records to
  remain without login names.
- Unique `Role.Name`; composite primary key `(UserId, RoleId)` prevents duplicate role
  assignments.
- Foreign keys from `UserRoles` to `Users` and `Roles`.
- Enforce allowed roles and at-least-one-role / last-Admin invariants in service
  transactions, with database constraints/indexes where suitable.

## Migration and state transitions

1. Preflight email normalization for duplicates/overlength values; stop for operator
   remediation if conflicts exist.
2. Add authentication columns. Existing rows receive disabled status, zero failures,
   normalized email, and no credentials/roles.
3. Create role, user-role, and bootstrap-state tables; seed `Admin`, `User`, and the
   singleton uncompleted bootstrap-state row.
4. Registration: absent → enabled `User` account with normalized identifiers, hash,
   zero failed attempts.
5. Failed login: increment consecutive failures; at configured threshold set
   `LockoutUntilUtc`. After expiry, clear lockout/failure state as part of the next
   login attempt.
6. Successful login: clear lockout/failure state; rehash when the hasher requires it;
   issue a short-lived token with user ID and current roles.
7. Legacy provisioning: disabled legacy row → enabled account with credentials and
   `User` role, transactionally.
8. Admin role/status change: apply only if the result preserves at least one enabled
   Admin and leaves any enabled account with at least one role.
9. Bootstrap: uncompleted → completed, atomically creating exactly one enabled Admin.

Migration `Down` removes the newly introduced tables/indexes/columns only. Deployment
must back up data and review the generated SQL migration before production rollout.
The existing WebSocket endpoint authorizes against current account state at connection
handshake; a role/status update does not forcibly terminate an already-open socket.
