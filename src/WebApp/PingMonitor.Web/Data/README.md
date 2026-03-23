# Data

This folder contains the control-plane persistence model.

## Current scope

- EF Core `DbContext` with MySQL as the only database provider
- ASP.NET Core Identity tables required for initial admin bootstrap
- startup-gate schema marker table (`AppSchemaInfo`)
- agent and monitoring persistence tables needed for the existing API surface

## Startup gate expectations

- the app must start far enough to render the startup gate even when MySQL configuration is missing or invalid
- schema creation is explicit and user-triggered from the startup gate
- SQLite is not supported in any environment
