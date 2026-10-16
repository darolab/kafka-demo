# Authentication and User-Management API Contract

This contract describes the planned HTTP surface. JSON property names are camelCase.
All user-management responses use a safe user representation and exclude passwords,
password hashes, lockout counters, bootstrap secrets, and other credential state.

## Authentication

### `POST /api/auth/register`

Public. Request:

```json
{
  "username": "casey",
  "email": "casey@example.test",
  "fullName": "Casey Example",
  "password": "<provided securely>",
  "age": 30
}
```

Creates an enabled account with only the `User` role. Success: `201 Created` with a safe
user object. Invalid fields: `400` validation problem details. Existing normalized
username/email: `409 Conflict`. Role properties are not accepted; an attempted
administrative-role assignment is rejected.

### `POST /api/auth/login`

Public. Request:

```json
{
  "username": "casey-or-casey@example.test",
  "password": "<provided securely>"
}
```

Accepts username or email in `username`. Success: `200 OK`:

```json
{
  "token": "<signed JWT>",
  "expiresAtUtc": "2030-01-01T00:00:00Z"
}
```

Unknown account, wrong password, disabled account, or active lockout returns the same
generic `401 Unauthorized` body/message. Never return password or hash. After five
consecutive failed attempts (configurable), lock the account for five minutes
(configurable); success resets the failure count.

### `POST /api/auth/bootstrap`

Available only until the first Admin account has been established. Requires the
operator-provided one-time bootstrap secret in the dedicated bootstrap header; it is
never accepted as a query parameter. The JSON body supplies the initial Admin's
username, email, full name, and password. Success: `201 Created` with a safe user
object. Invalid secret: generic `401`; already completed or concurrent replay:
`409 Conflict`. The bootstrap state and initial account/role are committed atomically.
The secret is removed/rotated in deployment configuration after use.

## JWT behavior

Protected endpoints use `Authorization: Bearer <token>`. Validate signing key,
configured issuer and audience, expiration, and signature. The issued token identifies
the user by stable database ID (`sub` and compatibility `userId` claim), includes name
and role claims, and expires according to validated configuration (default 60 minutes).
On protected HTTP requests, the current persisted account enabled state and roles are
authoritative; role/status changes apply on the next request.

Unauthenticated/missing/invalid/expired token: `401`. Authenticated caller without the
required role: `403`. Existing WebSocket access-token query support remains limited to
`/api/WebSocket/connect`. The current account status and roles are checked during the
WebSocket handshake; an already-open connection is not re-authorized or force-closed
when roles/status change and remains open until its normal disconnect.

## Admin user management

All existing user-management routes, including the compatibility route `/api/sql-users`,
require an authenticated `Admin`. Preserve the existing `/api/user` route paths where
practical; do not expose entity objects or raw `SELECT *` results.

| Operation | Contract |
|---|---|
| `GET /api/user?page=1&pageSize=50` | Paginated safe user list; page size is bounded to 100 |
| `GET /api/user/{id}` | Safe details and current role names; `404` when absent |
| `GET /{id}` | Preserve the existing absolute-route stored-procedure lookup if in use; require Admin and return only a safe projection |
| `POST /api/user` | Preserve existing Admin-only profile-create operation for a non-login legacy profile; safe response |
| `PUT /api/user/{id}` | Preserve safe profile-update route; cannot update credentials or roles through profile fields |
| `DELETE /api/user/{id}` | Preserve Admin-only delete route; reject removal if it violates last-Admin safeguards |
| `GET /api/user/search?q=...` | Preserve search route; return safe user projections |
| `POST /api/user/{id}/roles` | Body `{ "role": "Admin" }` or `{ "role": "User" }`; assign a supported role |
| `DELETE /api/user/{id}/roles/{role}` | Remove role unless that would leave an enabled user roleless or remove the last enabled Admin |
| `PATCH /api/user/{id}/status` | Body `{ "enabled": false }`; cannot disable the last enabled Admin |
| `POST /api/user/{id}/credentials` | Securely provision a legacy profile with unique username/password and enable it as `User` |
| `GET /api/sql-users` | Keep route; Admin-only and return safe projection, never database credentials/authentication fields |

Role/status operations return the updated safe user. Invalid role or state transition:
`400` or `409` as appropriate. A user without Admin role receives `403`; requests
without valid authentication receive `401`.

## Validation and error shape

Use automatic API model validation and consistent problem-details responses for
validation and resource conflicts. Never include password input, password hash,
JWT/bootstrap secret, or database credentials in errors or logs. Login errors must not
distinguish unknown, disabled, bad-password, and locked accounts.

## API request collection

The implementation will add `KafkaDemo.Auth.http`, a VS Code REST Client-compatible
collection with environment-only placeholders for API host, bootstrap secret, and
non-production credentials. It will cover registration, validation/duplicate failures,
login, token failure/expiry, role-denial/admin access, role assignment/removal, status,
bootstrap replay, and user-management authorization.
