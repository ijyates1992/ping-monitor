# Data

This folder contains the minimal persistence implementation needed for authenticated agent `/hello` handling.

## Current scope

- EF Core `DbContext` for the `Agent` entity only
- SQLite provider for local development and initial end-to-end wiring
- no result ingestion, state engine, or alerting persistence yet
