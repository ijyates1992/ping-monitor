# Local agent API examples

`docs/agent-api.md` remains the source of truth for the contract. This note shows the minimal local development flow.

## Development seed agent

In Development, the web app can seed a single agent record when both of these are true:

- `DevelopmentSeedAgent:Enabled` is `true`
- `DevelopmentSeedAgent__ApiKey` is supplied as an environment variable

Default seeded instance ID:

```text
dev-agent-01
```

The plaintext development API key is never stored in configuration. Startup hashes the supplied `DevelopmentSeedAgent__ApiKey` value before saving it.

## Example request: `/hello`

```bash
curl -i   -X POST https://localhost:5001/api/v1/agent/hello   -H 'Content-Type: application/json'   -H 'Accept: application/json'   -H 'X-Instance-Id: dev-agent-01'   -H "Authorization: Bearer ${DevelopmentSeedAgent__ApiKey}"   -d '{
    "agentVersion": "0.1.0",
    "machineName": "DEV-WORKSTATION",
    "platform": "windows",
    "capabilities": ["icmp"],
    "startedAtUtc": "2026-03-21T21:30:00Z"
  }'
```

## Example success response

```json
{
  "agentId": "5e51c7cf-9f7d-4c6b-a83e-8c55bf1f9f77",
  "serverTimeUtc": "2026-03-21T21:30:00Z",
  "configRefreshSeconds": 300,
  "heartbeatIntervalSeconds": 60,
  "resultBatchIntervalSeconds": 10,
  "maxResultBatchSize": 500,
  "configVersion": "cfg_dev_v1"
}
```

## Example request: `/config`

```bash
curl -i \
  https://localhost:5001/api/v1/agent/config \
  -H 'Accept: application/json' \
  -H 'X-Instance-Id: dev-agent-01' \
  -H "Authorization: Bearer ${DevelopmentSeedAgent__ApiKey}"
```

## Example success response

```json
{
  "configVersion": "cfg_dev_v1",
  "generatedAtUtc": "2026-03-21T21:30:10Z",
  "assignments": []
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
    "activeAssignments": 0,
    "queuedResultCount": 0,
    "status": "online"
  }'
```

## Example success response

```json
{
  "ok": true,
  "serverTimeUtc": "2026-03-21T21:31:00Z",
  "configChanged": false
}
```
