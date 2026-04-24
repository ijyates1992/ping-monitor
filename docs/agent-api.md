# Agent API Contract (v1, enterprise-grade)

## Overview

This document defines the server API used by monitoring agents.

The agent is an outbound worker. It does not expose any public API and does not require inbound connectivity. All communication is initiated by the agent over HTTPS to the web application.

The agent is responsible for:

- authenticating to the server
- downloading assigned monitoring configuration
- executing checks locally
- posting raw results
- sending periodic heartbeat messages
- buffering unsent results temporarily if the server is unavailable

The server is responsible for:

- authenticating agent requests
- assigning endpoints to agents
- providing timing and check settings
- storing raw results
- calculating endpoint state
- applying dependency suppression
- generating alerts
- tracking uptime, incidents, and audit events

## Design principles

- HTTPS only
- agent-initiated communication only
- no trust based on source IP
- no alerting logic in the agent
- no dependency suppression logic in the agent
- server returns configuration, agent executes it
- agent may continue briefly using cached configuration if the server is unavailable
- the agent owns execution timing
- the server owns monitoring truth

---

## Authentication

Every agent request must include:

- `X-Instance-Id`
- `Authorization: Bearer <API_KEY>`

### Required headers

```http
X-Instance-Id: branch-office-01
Authorization: Bearer <api_key>
Content-Type: application/json
Accept: application/json
User-Agent: ping-agent/0.1.1
```

### Authentication rules

The server must:

- look up the configured agent record using `X-Instance-Id`
- validate the bearer token against the stored API key hash
- reject disabled or revoked agents
- reject unknown instance IDs
- reject invalid credentials with `401 Unauthorized`
- never trust source IP address as identity

### Security rules

- API keys must be generated server-side
- API keys must be stored hashed, not plaintext
- instance IDs must be unique
- TLS is mandatory in production
- plaintext HTTP must not be supported in production
- API keys must be rotatable
- revoked API keys must immediately stop working

---

## Versioning

All endpoints are versioned under `/api/v1/agent`.

Base path:

```text
/api/v1/agent
```

Breaking changes require `/api/v2`.

Non-breaking changes may include:

- new optional response fields
- new optional request fields
- new status codes where appropriate
- new endpoints under `/api/v1/agent`

Agents must ignore unknown response fields unless explicitly documented otherwise.

---

## Common rules

### Time format

All timestamps must be UTC ISO-8601 strings with `Z` suffix.

Example:

```json
"2026-03-21T21:31:10Z"
```

### ID format

All IDs are opaque strings to the agent. The agent must not infer meaning from them.

Examples:

- UUIDs
- ULIDs
- server-generated opaque identifiers

### JSON conventions

- request and response bodies are JSON
- property names use camelCase
- unknown response fields must be ignored by the agent
- unknown request fields may be rejected by the server with `400 Bad Request`
- null must be used only where explicitly allowed

### Character encoding

All JSON payloads must use UTF-8.

### Numeric conventions

- intervals are integer seconds
- timeouts are integer milliseconds
- durations and counts must be non-negative
- round-trip times must be integer milliseconds when present

---

## Agent configuration inputs

The agent is configured locally using environment variables.

### Required variables

```env
SERVER_URL=https://monitor.example.com
INSTANCE_ID=branch-office-01
API_KEY=replace-with-generated-secret
```

### Optional variables

```env
CONFIG_REFRESH_SECONDS=300
HEARTBEAT_INTERVAL_SECONDS=60
RESULT_BATCH_INTERVAL_SECONDS=10
VERIFY_TLS=true
LOG_LEVEL=INFO
RESULT_QUEUE_PATH=./data/result-queue.jsonl
```

These optional values may be overridden by server responses where documented.

---

## Endpoint summary

The v1 API consists of four endpoints:

- `POST /api/v1/agent/hello`
- `GET /api/v1/agent/config`
- `POST /api/v1/agent/heartbeat`
- `POST /api/v1/agent/results`

---

# 1. Hello

Used by the agent at startup to validate credentials, announce its presence, and receive server timing guidance.

## Request

`POST /api/v1/agent/hello`

### Request body

```json
{
  "agentVersion": "0.1.1",
  "machineName": "BRANCH-PC-01",
  "platform": "windows",
  "capabilities": [
    "icmp"
  ],
  "startedAtUtc": "2026-03-21T21:30:00Z"
}
```

### Request schema

| Field | Type | Required | Rules |
|---|---|---:|---|
| `agentVersion` | string | yes | 1-50 chars |
| `machineName` | string | yes | 1-255 chars |
| `platform` | string | yes | 1-50 chars |
| `capabilities` | array of string | yes | at least 1 item |
| `startedAtUtc` | string | yes | UTC ISO-8601 |

### Validation rules

- `capabilities` must contain only supported capability strings
- duplicate capability values should be ignored or normalised by the server
- `agentVersion`, `machineName`, and `platform` must not be blank
- `startedAtUtc` must be a valid UTC timestamp

## Response

### Success response

`200 OK`

```json
{
  "agentId": "a7a4b4c2-2a77-4d9f-b6c1-11b6c1b38c51",
  "serverTimeUtc": "2026-03-21T21:30:00Z",
  "configRefreshSeconds": 300,
  "heartbeatIntervalSeconds": 60,
  "resultBatchIntervalSeconds": 10,
  "maxResultBatchSize": 500,
  "configVersion": "cfg_20260321_213000_01"
}
```

### Response schema

| Field | Type | Required | Notes |
|---|---|---:|---|
| `agentId` | string | yes | internal immutable agent ID |
| `serverTimeUtc` | string | yes | current UTC server time |
| `configRefreshSeconds` | integer | yes | recommended config refresh interval |
| `heartbeatIntervalSeconds` | integer | yes | recommended heartbeat interval |
| `resultBatchIntervalSeconds` | integer | yes | recommended result upload interval |
| `maxResultBatchSize` | integer | yes | maximum results per batch |
| `configVersion` | string | yes | opaque config version string |

## Behaviour

- the agent should call this once on startup
- a successful hello does not replace a config fetch
- the agent should fetch config immediately after hello
- the server may update last-seen metadata on successful hello

---

# 2. Config

Returns the current list of monitor assignments and execution settings for the authenticated agent.

## Request

`GET /api/v1/agent/config`

No request body.

## Response

### Success response

`200 OK`

```json
{
  "configVersion": "cfg_20260321_213000_01",
  "generatedAtUtc": "2026-03-21T21:30:00Z",
  "assignments": [
    {
      "assignmentId": "e601f66e-8c39-4d91-a73e-2fef70a8c001",
      "endpointId": "f5b72b2d-3d4d-4736-91dd-530b4a88c501",
      "name": "Core Switch",
      "target": "192.168.1.1",
      "checkType": "icmp",
      "enabled": true,
      "pingIntervalSeconds": 30,
      "retryIntervalSeconds": 5,
      "timeoutMs": 1000,
      "failureThreshold": 3,
      "recoveryThreshold": 2,
      "dependsOnEndpointIds": [],
      "tags": [
        "core",
        "switch"
      ]
    },
    {
      "assignmentId": "167e77fe-14be-4702-982d-3bc2f184df12",
      "endpointId": "23f79f65-0cd1-4eeb-8fd8-20a0d0a18e90",
      "name": "Office Printer",
      "target": "192.168.1.50",
      "checkType": "icmp",
      "enabled": true,
      "pingIntervalSeconds": 60,
      "retryIntervalSeconds": 10,
      "timeoutMs": 1000,
      "failureThreshold": 2,
      "recoveryThreshold": 2,
      "dependsOnEndpointIds": [
        "f5b72b2d-3d4d-4736-91dd-530b4a88c501"
      ],
      "tags": [
        "printer"
      ]
    }
  ]
}
```

### Response schema

| Field | Type | Required | Notes |
|---|---|---:|---|
| `configVersion` | string | yes | opaque config version |
| `generatedAtUtc` | string | yes | UTC generation time |
| `assignments` | array | yes | list of assignment objects |

### Assignment schema

| Field | Type | Required | Notes |
|---|---|---:|---|
| `assignmentId` | string | yes | unique assignment ID |
| `endpointId` | string | yes | endpoint ID |
| `name` | string | yes | display name |
| `target` | string | yes | hostname or IP |
| `checkType` | string | yes | initially `icmp` |
| `enabled` | boolean | yes | active assignment flag |
| `pingIntervalSeconds` | integer | yes | normal interval |
| `retryIntervalSeconds` | integer | yes | retry interval after failure |
| `timeoutMs` | integer | yes | timeout per check attempt |
| `failureThreshold` | integer | yes | consecutive failures required |
| `recoveryThreshold` | integer | yes | consecutive recoveries required |
| `dependsOnEndpointIds` | array of string | yes | direct parent endpoint IDs; empty array means no dependencies |
| `tags` | array of string | yes | optional descriptive tags |

### Validation rules

- `assignments` returned must belong only to the authenticated agent
- `name` and `target` must not be blank
- `pingIntervalSeconds` must be `>= 1`
- `retryIntervalSeconds` must be `>= 1`
- `timeoutMs` must be `>= 1`
- `failureThreshold` must be `>= 1`
- `recoveryThreshold` must be `>= 1`
- `checkType` must be supported by both server policy and agent capability set
- disabled assignments may be omitted or included with `enabled = false`

## Behaviour

- the agent must treat this response as the source of truth for assignments
- the agent must replace or reconcile cached assignments using this response
- the agent must not invent assignments locally
- the agent must not apply dependency suppression locally
- `dependsOnEndpointIds` is informational/config metadata only in v1

## Refresh rules

The agent should fetch config:

- immediately after successful hello
- when starting with no cached config
- every `configRefreshSeconds`
- immediately after heartbeat response indicates `configChanged = true`

---

# 3. Heartbeat

Used by the agent to report liveness and current operating state.

## Request

`POST /api/v1/agent/heartbeat`

### Request body

```json
{
  "agentVersion": "0.1.1",
  "sentAtUtc": "2026-03-21T21:31:00Z",
  "configVersion": "cfg_20260321_213000_01",
  "activeAssignments": 24,
  "queuedResultCount": 12,
  "status": "online"
}
```

### Request schema

| Field | Type | Required | Notes |
|---|---|---:|---|
| `agentVersion` | string | yes | current agent version |
| `sentAtUtc` | string | yes | UTC send time |
| `configVersion` | string | yes | current config version in use |
| `activeAssignments` | integer | yes | non-negative |
| `queuedResultCount` | integer | yes | non-negative |
| `status` | string | yes | initially `online` |

### Validation rules

- `sentAtUtc` must be valid UTC ISO-8601
- `activeAssignments` must be `>= 0`
- `queuedResultCount` must be `>= 0`
- `status` must be one of the documented allowed values for v1

### Allowed v1 status values

- `online`
- `degraded`

## Response

### Success response

`200 OK`

```json
{
  "ok": true,
  "serverTimeUtc": "2026-03-21T21:31:00Z",
  "configChanged": false
}
```

### Response schema

| Field | Type | Required | Notes |
|---|---|---:|---|
| `ok` | boolean | yes | confirmation |
| `serverTimeUtc` | string | yes | current UTC server time |
| `configChanged` | boolean | yes | whether immediate config refresh is required |

## Behaviour

- heartbeat does not carry check results
- heartbeat should be sent even if there are no active failures
- server uses heartbeat to detect agent offline state
- if `configChanged` is true, the agent should request config immediately

---

# 4. Results

Used by the agent to submit raw check results in batches.

## Request

`POST /api/v1/agent/results`

### Request body

```json
{
  "sentAtUtc": "2026-03-21T21:31:10Z",
  "batchId": "res_20260321_213110_0001",
  "results": [
    {
      "assignmentId": "e601f66e-8c39-4d91-a73e-2fef70a8c001",
      "endpointId": "f5b72b2d-3d4d-4736-91dd-530b4a88c501",
      "checkType": "icmp",
      "checkedAtUtc": "2026-03-21T21:31:09Z",
      "success": true,
      "roundTripMs": 2,
      "errorCode": null,
      "errorMessage": null
    },
    {
      "assignmentId": "167e77fe-14be-4702-982d-3bc2f184df12",
      "endpointId": "23f79f65-0cd1-4eeb-8fd8-20a0d0a18e90",
      "checkType": "icmp",
      "checkedAtUtc": "2026-03-21T21:31:08Z",
      "success": false,
      "roundTripMs": null,
      "errorCode": "PING_TIMEOUT",
      "errorMessage": "Ping request timed out"
    }
  ]
}
```

### Request schema

| Field | Type | Required | Notes |
|---|---|---:|---|
| `sentAtUtc` | string | yes | UTC send time |
| `batchId` | string | yes | idempotency key for the batch |
| `results` | array | yes | one or more result objects |

### Result schema

| Field | Type | Required | Notes |
|---|---|---:|---|
| `assignmentId` | string | yes | assignment ID |
| `endpointId` | string | yes | endpoint ID |
| `checkType` | string | yes | initially `icmp` |
| `checkedAtUtc` | string | yes | actual completion time |
| `success` | boolean | yes | success/failure |
| `roundTripMs` | integer or null | yes | null on failure |
| `errorCode` | string or null | yes | null on success |
| `errorMessage` | string or null | yes | null on success, optional on failure |

### Validation rules

- `batchId` must be unique per batch submission from the agent
- `results` must contain at least one record
- each result must reference a valid assignment owned by the agent
- `checkedAtUtc` must be a valid UTC timestamp
- `roundTripMs` must be `>= 0` when present
- `roundTripMs` must be null on failure
- `errorCode` must be null on success
- `errorMessage` may be null on failure but should be populated when useful
- the server should reject obviously malformed batches with `400 Bad Request`

## Response

### Success response

`200 OK`

```json
{
  "accepted": true,
  "acceptedCount": 2,
  "duplicate": false,
  "serverTimeUtc": "2026-03-21T21:31:10Z"
}
```

### Response schema

| Field | Type | Required | Notes |
|---|---|---:|---|
| `accepted` | boolean | yes | whether the batch was accepted |
| `acceptedCount` | integer | yes | accepted result count |
| `duplicate` | boolean | yes | whether this batch was already processed |
| `serverTimeUtc` | string | yes | current UTC server time |

## Behaviour

- results must be raw facts only
- the agent must not send:
  - down state
  - suppressed state
  - alert state
  - incident state
- the server is responsible for all interpretation
- the server should validate that assignment IDs belong to the authenticated agent
- the agent should batch results for efficiency

---

## Idempotency rules

The results endpoint must support idempotent batch submission using `batchId`.

### Required behaviour

- if the same authenticated agent submits the same `batchId` more than once, the server must not duplicate stored results
- the server should return `200 OK` with `duplicate = true` for safe retries where the batch has already been accepted
- the agent may safely retry a batch if it is uncertain whether the previous submission succeeded

### Rationale

This prevents duplicated result rows when network failures occur after the server has accepted a batch but before the agent receives the response.

---

## Error response model

All non-2xx responses should use a consistent JSON error envelope.

### Error response schema

```json
{
  "error": {
    "code": "invalid_request",
    "message": "One or more fields are invalid.",
    "details": [
      {
        "field": "results[0].checkedAtUtc",
        "message": "Value must be a valid UTC ISO-8601 timestamp."
      }
    ],
    "traceId": "00-1c4f8f4d0b1f4a8f8b9c8e1f2d3c4b5a-6f7e8d9c0b1a2d3e-00"
  }
}
```

### Error response fields

| Field | Type | Required | Notes |
|---|---|---:|---|
| `error.code` | string | yes | machine-readable error code |
| `error.message` | string | yes | human-readable summary |
| `error.details` | array | no | optional field-level issues |
| `error.traceId` | string | no | request trace ID for diagnostics |

### Recommended error codes

- `invalid_request`
- `unauthorized`
- `forbidden`
- `not_found`
- `conflict`
- `rate_limited`
- `server_error`
- `service_unavailable`

---

## Status codes

### `200 OK`
Request succeeded.

### `400 Bad Request`
Malformed request body, invalid field values, unsupported enum values, or failed validation.

### `401 Unauthorized`
Missing credentials or invalid credentials.

### `403 Forbidden`
Authenticated agent is disabled or not permitted.

### `404 Not Found`
Unknown endpoint path.

### `409 Conflict`
Optional use for assignment mismatch, invalid version conflict, or other safe conflict cases.

### `429 Too Many Requests`
Agent is sending too aggressively.

### `500 Internal Server Error`
Unexpected server-side failure.

### `503 Service Unavailable`
Temporary server unavailability.

---

## Rate limiting

The server should support defensive rate limiting.

### Recommended behaviour

- rate limiting should be per authenticated agent
- hello, config, heartbeat, and results may use different thresholds
- `429 Too Many Requests` responses should include a `Retry-After` header where practical

### Agent behaviour

- on `429`, the agent should back off before retrying
- repeated `429` responses should increase backoff duration
- the agent should log rate-limit events clearly

---

## Retry and interval semantics

The server provides both normal and retry timing guidance.

### `pingIntervalSeconds`

Normal check cadence while an assignment is healthy or operating normally.

### `retryIntervalSeconds`

Shorter retry cadence after a failed attempt, used by the agent to gather additional evidence sooner.

### Important rule

The retry interval is an execution hint only. The agent still submits raw results. The server decides whether enough consecutive failures or recoveries have occurred to change endpoint state.

---

## Dependency semantics

Assignments may include `dependsOnEndpointIds`.

In v1:

- this is configuration metadata only
- it allows the server to define dependency relationships
- the agent must not use this field to suppress checks
- the agent must not suppress result submission for dependent endpoints
- the server applies dependency logic centrally

This keeps monitoring truth consistent and avoids divergent behaviour across agents.

---

## Offline and cached config behaviour

The agent should cache the most recent valid configuration locally.

If the server is temporarily unavailable:

- the agent may continue operating using the last valid config
- the agent should continue collecting results
- the agent should queue results locally for later upload
- the agent should log connectivity failures clearly

### Recommended behaviour

- continue using cached config until replaced
- do not silently discard results
- retry uploads with backoff
- keep heartbeat attempts separate from result uploads where possible

### Queueing expectations

- queued results should survive process restarts where practical
- if local queue storage reaches a configured limit, the agent must log explicit warnings
- if dropping results ever becomes necessary, that event must be logged explicitly and audibly

### Server-side expectations

The server should:

- track last successful heartbeat
- mark agents stale/offline based on missed heartbeat threshold
- not assume missing results mean all endpoints are healthy

---

## Validation rules summary

### General

- all timestamps must be UTC ISO-8601
- `INSTANCE_ID` must be unique
- all IDs must be treated as opaque strings by the agent
- unknown JSON response fields should be ignored by the agent

### Hello

- capabilities must contain only supported capability strings
- agent version string is required

### Config

- assignments returned must belong only to the authenticated agent
- disabled assignments may be omitted or included with `enabled = false`

### Results

- each result must reference a valid assignment owned by the agent
- `roundTripMs` must be null on failure
- `errorCode` should be null on success
- `checkedAtUtc` must represent the actual check completion time

---

## Forward compatibility

The contract should be extended carefully without breaking existing agents.

### Rules

- new response fields may be added
- agents should ignore unknown response fields
- new check types may be introduced later
- new endpoints may be added under `/api/v1/agent`
- breaking changes require `/api/v2`

### Likely future additions

- TCP checks
- HTTP checks
- maintenance mode flags
- assignment notes
- packet size / TTL settings
- capability negotiation
- config diff endpoints
- key rotation support
- downloadable preconfigured agent bundles

---

## Non-goals for v1

The following are explicitly out of scope for the initial agent API:

- server push to agent
- inbound ports on the agent
- WebSocket connections
- agent-side alerting
- agent-side dependency suppression
- agent-side state calculation
- remote command execution
- auto-remediation

---

## Example request sequence

1. Agent starts with `SERVER_URL`, `INSTANCE_ID`, and `API_KEY`
2. Agent sends `POST /api/v1/agent/hello`
3. Agent sends `GET /api/v1/agent/config`
4. Agent schedules local checks
5. Agent sends `POST /api/v1/agent/results` in batches
6. Agent sends `POST /api/v1/agent/heartbeat` periodically
7. If heartbeat response says `configChanged = true`, agent refreshes config immediately

---

## Summary

The v1 agent API follows this rule:

**The agent owns execution timing. The server owns monitoring truth.**

The agent is responsible for running checks and submitting raw results. The server is responsible for interpreting those results, applying dependency logic, generating alerts, and calculating uptime.
