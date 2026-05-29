# Application updater lock recovery

## Summary

The application updater keeps active state in `App_Data/Updater/state/staged-update.json` and reads the external bootstrapper result from `App_Data/Updater/state/external-updater-status.json` when the web application starts or when the updater page refreshes.

A successful external update can restart the application while the active staged state still says that handoff/apply is in progress. Operators previously had to delete `App_Data/Updater` manually to make future update actions available again.

## Recovery behavior

During updater status refresh, the web application now reconciles the preserved updater state instead of leaving it silently locked forever:

1. If the bootstrapper status reports success and the running application version matches the staged release tag, the app marks the previous update as `ApplySucceeded`.
2. The active staging/in-progress flag is cleared so a future check, stage, or apply cycle is not blocked by the previous successful update.
3. The updater page shows that the previous update succeeded and that no update is currently staged for apply.
4. The bootstrapper status and log paths remain visible for diagnostics.
5. If the bootstrapper still reports `in_progress` but is stale and success cannot be confirmed, the app marks the state as `ApplyInterruptedNeedsAttention` instead of treating it as success.

## Safety notes

The app does **not** blindly delete updater state on startup. It only reconciles success when both of these are true:

- the bootstrapper status indicates success, and
- the running application version matches the target staged release.

If those conditions are not met, stale in-progress state is surfaced as needing operator attention. The updater is unlocked for recovery actions such as re-checking or re-staging, but the previous update is not marked successful.

## Operator guidance

- For `ApplySucceeded`, no manual deletion of `App_Data/Updater` is required before checking or staging future updates.
- For `ApplyInterruptedNeedsAttention`, inspect `external-updater-status.json` and `external-updater.log` from the paths shown on the updater page before retrying.
- Preserve `App_Data/Updater` when collecting diagnostics; it contains the staged metadata, bootstrapper status, and log references needed to understand the previous apply attempt.
