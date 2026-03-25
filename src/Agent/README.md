# PingMonitor Agent Skeleton

This directory contains the phase 1 outbound-only Python agent skeleton.

## Current scope

- loads required environment configuration
- prepares authenticated HTTPS calls to the web application
- fetches hello/config/heartbeat/results using the documented v1 API paths
- keeps result handling as raw fact submission only
- executes real ICMP checks using the system `ping` command
- uses each assignment's configured `target` and `timeoutMs` when running ICMP
- keeps scheduling behaviour intentionally minimal

## Out of scope in phase 1

- persistent queue storage
- retry/backoff implementation
- agent-side state, suppression, or alert logic

## Windows startup helper

Use `run-agent.cmd` for local Windows development or manual testing:

1. Open `src/Agent`
2. Run `run-agent.cmd` (double-click or from `cmd.exe`)

The script will:

- detect `py` first, then fall back to `python`
- create `src/Agent/.venv` if needed and reuse it on later runs
- upgrade `pip` and install `requirements.txt`
- start the agent in the foreground via `python -m app.main`

`SERVER_URL`, `INSTANCE_ID`, and `API_KEY` still need to be configured (for example via `.env`) before startup.

## Provisioning package download

The web control plane exposes `GET/POST /agents/deploy` to provision a new agent package.

- Enter an agent name and submit the form to download a ZIP package.
- The downloaded ZIP includes a generated `.env` with `SERVER_URL`, `INSTANCE_ID`, and a newly generated `API_KEY`.
- The API key is generated server-side and only delivered at package creation time; only the hash is stored by the server.
- Rotating credentials later requires a dedicated reprovisioning/rotation flow (not included in this phase).
