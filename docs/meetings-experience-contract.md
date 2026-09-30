# Meetings Experience Contract

The Meetings workbench is a presentation contract over existing catalog and
action authority. It does not invoke, broaden, or hide an action.

## States

`RefreshRequired`, `Busy`, and `Empty` take precedence over normal library or
selection presentation. Then the state is `Detail`, `MultiSelection`,
`SingleSelection`, or `Library`. Stale status never yields a primary action;
busy state preserves reading/list context but blocks conflicting work.

One selected meeting may promote one metadata-only recommendation. Promotion is
presentation only: it never dispatches automatically, never replaces action
families, and is withheld while a recording is active. Multi-selection never
borrows a single-meeting primary action.

## Ownership

`MeetingActionCatalog` remains authoritative. No-selection actions belong to
Library; multi-cardinality actions belong to MultiSelection; detail targets
belong to Detail; recommendation actions belong to Cleanup Review; typed
permanent-delete actions belong to an isolated Destructive Explicit route. A
surface may omit an irrelevant action, but cannot redefine eligibility,
confirmation, outcome, or recovery.

Refresh preserves selection and drafts unless the underlying catalog mutation
removes the target; then the UI must report the changed scope instead of
retargeting an action. Advanced and Custom capabilities remain discoverable
through their catalog family and the S0 inventory.
