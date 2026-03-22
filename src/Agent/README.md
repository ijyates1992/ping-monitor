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
