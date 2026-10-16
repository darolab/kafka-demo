# Implementation Plan: Authentication and Role Management

**Branch**: `002-authentication-role-management` | **Date**: 2026-10-07 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-authentication-role-management/spec.md`

## Summary

Replace demo-account login with database-backed registration and authentication;
add role-protected administration, safe legacy-account provisioning, and one-time
first-Admin bootstrap. Extend the existing `Users` data model with nullable
authentication fields so legacy rows remain intact and non-login by default. Use
EF Core/SQL Server already in the project, the existing JWT bearer middleware, and the
ASP.NET Core password hasher directly without enabling the Identity account-management
system. Keep current non-user REST authentication policies and Kafka services unchanged.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: ASP.NET Core Web API, EF Core 10 SQL Server, existing
`Microsoft.AspNetCore.Authentication.JwtBearer`; ASP.NET Core's
`IPasswordHasher<User>` registered directly. No new production dependency is planned.

**Storage**: Existing SQL Server `AppDbContext` and `Users` table; additive EF Core
migration with data preflight for normalized email uniqueness and retained legacy rows.

**Testing**: Existing xUnit project, `WebApplicationFactory<Program>`, and EF Core SQLite
test provider; targeted service tests plus HTTP integration tests and the API request
collection.

**Target Platform**: Existing ASP.NET Core backend deployment on .NET 10 with SQL Server.

**Project Type**: Single-project ASP.NET Core Web API with an existing test project.

**Performance Goals**: No feature-specific latency/throughput SLO is defined. Avoid
unnecessary database round trips; validate current account enabled status and roles
against the database for each authenticated HTTP request so role/status changes take
effect on the next request.

**Constraints**: Preserve existing user profile rows, route shapes where practical,
current non-user endpoint authentication policies, WebSocket JWT support, and all Kafka
behavior. Admin authentication and authorization apply to every user-management surface.
Never log credential material. The login lockout defaults to five consecutive failures
and five minutes, both configurable. JWT signing key, issuer, audience, and bootstrap
secret are deployment configuration, not committed secrets. Move database credentials
out of tracked configuration as well; the current appsettings file contains connection
configuration and must not be used to commit real credentials.

**Scale/Scope**: Current application and its existing SQL-backed user data; no user
volume or availability target is specified. Bootstrap and last-Admin protections must
remain correct under concurrent requests.

## Constitution Check

| Principle / Gate | Plan assessment |
|---|---|
| Security-first, hashed credentials, JWT, server-side authorization | Pass: use a standard password hasher, validate tokens, and put all user-management operations behind Admin authorization. |
| Resource authorization and input validation | Pass: admin routes operate on validated IDs, supported roles, and safe request DTOs; public registration cannot submit roles. |
| Sensitive information excluded from logs and responses | Pass: return safe DTOs, use generic login failures, and exclude hashes, passwords, signing keys, and bootstrap secret from logs. |
| Existing architecture, dependencies, REST, and Kafka | Pass: reuse the current Web API, EF Core context, JWT configuration, DI and xUnit test project; add no Identity system or production package. Preserve unrelated route policies and Kafka services. |
| Maintainable/testable behavior | Pass: separate authentication/account operations behind a scoped service and repository, with focused unit and HTTP integration tests. |
| Safe/reversible database changes | Pass with rollout gate: additive migration, legacy rows disabled for login, deterministic backfill, duplicate-data preflight, and reversible schema operations. Do not auto-delete or merge legacy user data. |
| Authentication and authorization automated tests | Pass: test success and negative cases, role changes, lockout, bootstrap races, and existing WebSocket token compatibility. |

**Gate result**: Pass. No constitutional violation or dependency/framework exception is
required. The deliberate user-management authentication change is specified; other
existing REST access policies remain unchanged.

## Project Structure

### Documentation (this feature)

```text
specs/002-authentication-role-management/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
└── contracts/
    └── authentication-api.md
```

### Source Code (repository root)

```text
Controllers/
├── AuthController.cs                 # register, login, one-time bootstrap
├── UserController.cs                 # existing user routes, Admin-protected; add role/status/provision actions
└── SqlUsersController.cs             # preserve route but require Admin and return safe user data
Data/
└── AppDbContext.cs                   # role/user-role/bootstrap mapping and normalized unique indexes
DTO/
├── AuthenticationRequestDTOs.cs      # registration, login, bootstrap, role/status, legacy provisioning
└── UserResponseDTO.cs                # safe response projection
Migrations/
└── EF-generated authentication migration and model snapshot
Models/
├── User.cs                           # extend current profile with auth state
├── Role.cs
├── UserRole.cs
└── AuthenticationBootstrapState.cs
Repository/
├── UserRepository.cs                 # retain the current stored-procedure compatibility path
└── AuthenticationRepository.cs       # EF-backed auth, role and transactional account operations
Services/
├── AuthenticationService.cs          # password verification, lockout, registration, token issuance, bootstrap
├── JwtOptions.cs                      # signing/issuer/audience/token lifetime settings
└── JwtBearerOptionsPostConfigure.cs   # signing key and validation settings
Program.cs                             # options validation, scoped DI and authorization setup
tests/KafkaDemo.Tests/
├── Controllers/AuthenticationApiTests.cs
├── Controllers/UserManagementApiTests.cs
└── Services/AuthenticationServiceTests.cs
KafkaDemo.Auth.http                    # runnable frontend-free REST test collection
```

**Structure Decision**: Extend the existing single web project and test project in place.
Keep controller routing and the existing repository/service patterns. Add an
`AuthenticationRepository` over the already-registered `AppDbContext` for relational
account/role operations, leaving the existing stored-procedure `UserRepository` path
intact. Register the password hasher, authentication service/repository, and validated
authentication/JWT options through existing dependency injection; add Admin role
authorization to user-management controllers/actions. Do not modify notification,
Kafka, transaction, or unrelated controllers.

## Complexity Tracking

No constitution violations. No additional application project, Identity framework, or
production dependency is proposed.
