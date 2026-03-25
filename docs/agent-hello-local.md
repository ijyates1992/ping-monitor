# Local agent API examples

`docs/agent-api.md` remains the source of truth for the contract. This note shows the minimal local development flow for `/hello`, `/config`, `/heartbeat`, and `/results`.

## Development seed agent

In Development, the web app seeds a single agent record and two ICMP assignments when both of these are true:

- `DevelopmentSeedAgent:Enabled` is `true`
- `DevelopmentSeedAgent__ApiKey` is supplied as an environment variable

Default local credentials:

```text
INSTANCE_ID=dev-agent-01
API_KEY=<set via DevelopmentSeedAgent__ApiKey>
```

Seeded development assignments:

- `assignment-dev-gateway` → endpoint `endpoint-dev-gateway` (`192.0.2.1`)
- `assignment-dev-printer` → endpoint `endpoint-dev-printer` (`192.0.2.55`)

The plaintext development API key is never stored in configuration. Startup hashes the supplied `DevelopmentSeedAgent__ApiKey` value before saving it. Seeded data is development-only.

## Example request: `/hello`

```bash
curl -i \
  -X POST https://localhost:5001/api/v1/agent/hello \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json' \
  -H 'X-Instance-Id: dev-agent-01' \
  -H "Authorization: Bearer ${DevelopmentSeedAgent__ApiKey}" \
  -d '{
    "agentVersion": "0.1.0",
    "machineName": "DEV-WORKSTATION",
    "platform": "windows",
    "capabilities": ["icmp"],
    "startedAtUtc": "2026-03-21T21:30:00Z"
  }'
```

## Example request: `/config`

```bash
curl -i \
  https://localhost:5001/api/v1/agent/config \
  -H 'Accept: application/json' \
  -H 'X-Instance-Id: dev-agent-01' \
  -H "Authorization: Bearer ${DevelopmentSeedAgent__ApiKey}"
```

## Example success response: `/config`

```json
{
  "configVersion": "cfg_dev_v1",
  "generatedAtUtc": "2026-03-21T21:30:10Z",
  "assignments": [
    {
      "assignmentId": "assignment-dev-gateway",
      "endpointId": "endpoint-dev-gateway",
      "name": "Dev Gateway",
      "target": "192.0.2.1",
      "checkType": "icmp",
      "enabled": true,
      "pingIntervalSeconds": 30,
      "retryIntervalSeconds": 5,
      "timeoutMs": 1000,
      "failureThreshold": 3,
      "recoveryThreshold": 2,
      "dependsOnEndpointIds": [],
      "tags": ["dev", "gateway"]
    },
    {
      "assignmentId": "assignment-dev-printer",
      "endpointId": "endpoint-dev-printer",
      "name": "Dev Printer",
      "target": "192.0.2.55",
      "checkType": "icmp",
      "enabled": true,
      "pingIntervalSeconds": 60,
      "retryIntervalSeconds": 10,
      "timeoutMs": 1000,
      "failureThreshold": 2,
      "recoveryThreshold": 2,
      "dependsOnEndpointIds": ["endpoint-dev-gateway"],
      "tags": ["dev", "printer"]
    }
  ]
}
```

## Example request: `/heartbeat`

```bash
curl -i \
  -X POST https://localhost:5001/api/v1/agent/heartbeat \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json' \
  -H 'X-Instance-Id: dev-agent-01' \
  -H "Authorization: Bearer ${DevelopmentSeedAgent__ApiKey}" \
  -d '{
    "agentVersion": "0.1.0",
    "sentAtUtc": "2026-03-21T21:31:00Z",
    "configVersion": "cfg_dev_v1",
    "activeAssignments": 2,
    "queuedResultCount": 0,
    "status": "online"
  }'
```

## Example request: `/results`

```bash
curl -i \
  -X POST https://localhost:5001/api/v1/agent/results \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json' \
  -H 'X-Instance-Id: dev-agent-01' \
  -H "Authorization: Bearer ${DevelopmentSeedAgent__ApiKey}" \
  -d '{
    "sentAtUtc": "2026-03-21T21:31:10Z",
    "batchId": "batch-dev-001",
    "results": [
      {
        "assignmentId": "assignment-dev-gateway",
        "endpointId": "endpoint-dev-gateway",
        "checkType": "icmp",
        "checkedAtUtc": "2026-03-21T21:31:05Z",
        "success": true,
        "roundTripMs": 2,
        "errorCode": null,
        "errorMessage": null
      },
      {
        "assignmentId": "assignment-dev-printer",
        "endpointId": "endpoint-dev-printer",
        "checkType": "icmp",
        "checkedAtUtc": "2026-03-21T21:31:08Z",
        "success": false,
        "roundTripMs": null,
        "errorCode": "PING_TIMEOUT",
        "errorMessage": "Ping request timed out"
      }
    ]
  }'
```

## Example success response: `/results`

```json
{
  "accepted": true,
  "acceptedCount": 2,
  "duplicate": false,
  "serverTimeUtc": "2026-03-21T21:31:10Z"
}
```

## Example duplicate retry response: `/results`

Retry the same request with the same authenticated agent and the same `batchId`:

```json
{
  "accepted": true,
  "acceptedCount": 2,
  "duplicate": true,
  "serverTimeUtc": "2026-03-21T21:31:12Z"
}
```
