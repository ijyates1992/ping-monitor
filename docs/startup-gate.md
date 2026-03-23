# Startup gate

## Overview

The startup gate is a controlled initialization and safety mechanism that prevents the application from running in a partially configured or inconsistent state.

It ensures that all critical prerequisites are satisfied before the main application becomes available.

The startup gate is responsible for:

- database configuration
- database connectivity validation
- schema validation and upgrades
- initial identity bootstrap
- diagnostics and safe recovery

---

## Design principles

- The application must never run in a degraded or partially configured state  
- All critical failures must be surfaced explicitly at startup  
- Configuration and schema changes must be explicit, not automatic  
- Sensitive operations must only be available from local access  
- Remote access must be read-only in gate mode  
- The gate must be deterministic and auditable  

---

## Startup modes

The application runs in one of two modes:

### 1. Normal mode

The application runs normally when all startup checks pass:

- database configuration exists  
- database connection is successful  
- schema is valid and up to date  
- at least one admin user exists  

All application features are available.

---

### 2. Startup gate mode

The application enters startup gate mode when any required condition fails.

In this mode:

- only the startup gate UI/endpoints are available  
- all normal application endpoints are disabled  
- the system provides diagnostics and guided setup  

---

## Startup checks

The startup gate evaluates the following checks in order:

### 1. Configuration check

- database connection settings must exist  
- required fields:
  - host
  - port (default 3306)
  - database name
  - username
  - password  

If missing, the gate enters configuration mode.

---

### 2. Database connectivity check

- the application must be able to connect to MySQL using provided credentials  

Failure results in:

- connection error shown  
- no further checks performed  

---

### 3. Schema check

The application verifies that the database schema matches the required structure.

Checks include:

- required tables exist  
- required columns exist  
- schema version is compatible  

If any mismatch is detected:

- the gate enters schema mode  
- required changes are displayed  

---

### 4. Identity bootstrap check

The application verifies that at least one admin user exists.

If no admin exists:

- the gate enters identity bootstrap mode  
- admin creation is required before proceeding  

---

## Local vs remote access rules

### Local access (trusted)

Requests originating from the local machine (loopback) are considered trusted.

Examples:

- http://localhost  
- http://127.0.0.1  
- http://[::1]  

Local access is allowed to:

- configure database connection  
- apply schema changes  
- create initial admin user  

---

### Remote access (untrusted)

All non-local requests are considered remote.

Remote access is restricted to:

- viewing diagnostics only  
- no ability to modify configuration  
- no ability to apply schema changes  
- no ability to create users  

The UI must clearly indicate that write operations require local access.

---

## Database configuration

### Input fields

The startup gate collects:

- host  
- port  
- database name  
- username  
- password  

The application constructs the connection string internally.

### Storage

- configuration is stored locally in application configuration  
- credentials must not be exposed after saving  
- password should be protected using secure storage mechanisms where possible  

---

## Schema management

### Schema validation

The application must:

- detect missing tables  
- detect missing columns  
- detect incompatible schema versions  

### Schema versioning

A schema tracking table must exist:

Example:

- `AppSchemaInfo`
  - `CurrentSchemaVersion`
  - `UpdatedAtUtc`

### Schema actions

Local users may:

- create initial schema  
- apply required schema updates  

### Important rules

- schema changes must not be applied automatically in production  
- all changes must be explicit and user-triggered  
- schema operations must be logged  

---

## Identity bootstrap

### Condition

Triggered when:

- no users exist  
  OR  
- no admin user exists  

### Local-only action

Local users may:

- create the first admin user  

Fields:

- username  
- email  
- password  
- confirm password  

### Security rules

- this action must never be available remotely  
- the first user must be assigned admin privileges  
- password must meet minimum security requirements  

---

## Diagnostics

The startup gate must provide clear diagnostics:

### Status overview

- application version  
- environment  
- startup mode (normal / gate)  

### Database status

- configuration present  
- connection success/failure  
- error details if failed  

### Schema status

- schema version  
- missing components  
- upgrade required  

### Identity status

- admin present / missing  

### Access mode

- local vs remote detection  
- whether write operations are enabled  

---

## Behaviour rules

- the main application must not start until all checks pass  
- partial startup is not allowed  
- missing configuration must block application startup  
- schema mismatch must block application startup  
- missing admin must block application startup  

---

## Security rules

- write operations must be restricted to local access  
- credentials must not be exposed in UI after submission  
- database passwords must not be logged  
- admin creation must be local-only  
- no bypass of startup gate is allowed  

---

## Logging and audit

The following actions must be logged:

- database configuration changes  
- schema creation or updates  
- admin user creation  
- startup failures  

Logs must include:

- timestamp  
- action type  
- success/failure  
- relevant context  

---

## Non-goals (v1)

- remote setup workflows  
- automatic schema migration on startup  
- multi-database support  
- distributed setup coordination  
- advanced RBAC during bootstrap  

---

## Future extensions

Possible future improvements:

- encrypted credential storage  
- multi-environment configuration profiles  
- rollback support for schema changes  
- maintenance mode integration  
- automated backup before schema upgrade  

---

## Summary

The startup gate enforces a strict rule:

**The system must be fully configured, connected, and valid before it runs.**

- configuration must exist  
- database must be reachable  
- schema must be correct  
- admin must exist  

Until then, the application remains in a controlled, secure startup mode with local-only write access.
