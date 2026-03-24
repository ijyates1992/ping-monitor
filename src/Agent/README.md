# PingMonitor Agent Skeleton

This directory contains the phase 1 outbound-only Python agent skeleton.

## Current scope

- loads required environment configuration
- prepares authenticated HTTPS calls to the web application
- fetches hello/config/heartbeat/results using the documented v1 API paths
- keeps result handling as raw fact submission only
- leaves ICMP execution and scheduling behaviour intentionally minimal

## Out of scope in phase 1

- real ICMP probing
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
- start the agent in the foreground via `python app/main.py`

`SERVER_URL`, `INSTANCE_ID`, and `API_KEY` still need to be configured (for example via `.env`) before startup.
