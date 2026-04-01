# UI consistency rules

## Layout boundaries

- Login/auth entry flows (for example `/account/login`) use a simplified unauthenticated shell and do not render the authenticated site navigation.
- Authenticated operational/admin pages (for example `/users`) render the standard authenticated navigation shell.

## Shared visual language

- Auth and authenticated pages must use the same theme tokens from `_ThemeHead` to preserve light, dark, and system mode behaviour.
- Forms, buttons, cards, spacing, and tables should use shared reusable classes/partials rather than page-specific one-off inline styling.
- Identity-related pages should look consistent with the rest of the control-plane UI while preserving existing behavior and safety constraints.
