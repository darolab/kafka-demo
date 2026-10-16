<!--
Sync Impact Report
- Version change: 1.0.0 -> 1.1.0
- Modified principles: I. Secure-by-Default Design (expanded); II. Contract-First Integration (expanded); III. Test-First Delivery (expanded); IV. Observable, Recoverable Operations (expanded); V. Simplicity and Maintainability (expanded)
- Added sections: none
- Removed sections: none
- Deferred items: TODO(RATIFICATION_DATE): confirm the original project adoption date
-->

# KafkaDemo Constitution

## Core Principles

### I. Secure-by-Default Design
Authentication MUST use JWT, and authorization MUST be enforced server-side for every
protected operation. Users MUST only access resources for which they are authorized.
APIs MUST validate input before processing it. Passwords MUST never be stored in plain
text and MUST be securely hashed. Passwords, JWT secrets, database credentials, and
other sensitive information MUST NOT be logged. Authentication and authorization MUST
fail closed when credentials, tokens, or required security configuration are missing or
invalid.

Rationale: secure defaults and explicit resource-level authorization reduce the risk of
credential exposure and unauthorized access.

### II. Contract-First Integration
The project MUST preserve existing REST API and Kafka functionality unless an intentional
change is explicitly approved and its compatibility impact is assessed. Public API,
Kafka topic, DTO, and outbound integration contracts MUST have clear interface and
request/response expectations. Contract changes MUST be tested and documented where
practical.

Rationale: stable contracts prevent accidental breakage across clients and distributed
components.

### III. Test-First Delivery
Changes MUST include automated tests that verify intended behavior and protect existing
behavior. Authentication and authorization behavior MUST have automated tests, including
checks that unauthorized users cannot access protected resources. Regression tests MUST
cover affected API, Kafka, persistence, and transaction behavior when those behaviors
change.

Rationale: automated tests make security boundaries and asynchronous integrations
repeatable to verify.

### IV. Observable, Recoverable Operations
Work that crosses process boundaries or persists data MUST use structured logging,
correlation where appropriate, and explicit failure handling. Logs MUST NOT contain
passwords, JWT secrets, database credentials, or other sensitive information.
Background workers and Kafka flows MUST surface failures rather than silently dropping
messages or requests.

Rationale: useful diagnostics must not come at the cost of exposing secrets or hiding
failures.

### V. Simplicity and Maintainability
Changes MUST follow the existing KafkaDemo architecture and coding conventions where
practical. The project MUST NOT introduce frameworks or dependencies without a
demonstrable need. Code MUST remain maintainable and testable, and changes MUST be
limited to the requested behavior rather than modifying unrelated functionality.

Rationale: consistency and focused changes reduce operational risk and long-term
maintenance cost.

## Security & Compliance
Secrets and environment-specific credentials MUST be kept out of source control.
Security-sensitive APIs MUST validate input and enforce authorization before accessing
or changing resources. Database changes MUST be designed to be safe and reversible
where practical; when reversibility is not practical, the change MUST document its
recovery or migration impact.

## Development Workflow
Changes MUST be reviewed against this Constitution before merge. Pull requests MUST
include targeted automated tests for affected behavior, verify that existing REST API
and Kafka functionality remains intact, and document intentional compatibility changes.
Changes to security boundaries, database schemas, API contracts, or Kafka contracts
MUST include an impact assessment and migration or recovery guidance where applicable.

## Governance
This Constitution governs architecture, security, testing, and delivery expectations.
Amendments MUST include a documented rationale, update the version according to
semantic versioning, and record the amendment date. A MAJOR bump is required for
backward-incompatible governance changes, a MINOR bump for new or materially expanded
principles or guidance, and a PATCH bump for clarifications and non-semantic refinements.

Every change MUST be checked for compliance with the relevant principles. Reviews that
alter authentication, authorization, sensitive-data handling, database models, API
contracts, Kafka contracts, or worker behavior MUST verify the corresponding automated
tests and assess compatibility and recovery impacts. Any intentional deviation MUST be
documented with its rationale.

**Version**: 1.1.0 | **Ratified**: TODO(RATIFICATION_DATE): confirm the original project adoption date | **Last Amended**: 2026-10-07
