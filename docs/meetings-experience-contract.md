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

## Row-state glossary

`MeetingPresentationStateResolver` is metadata-only and orders row truth as:
Refresh Required, Archived, Needs Attention, Blocked, Processing, Needs Action,
Complete, and Unavailable. Stale catalog or queue state never reports Complete.
Complete requires readable audio and transcript artifacts; archive, failure,
setup block, and active queue states win over a lower-priority recommendation.
The resolver exposes a concise accessible explanation and reason code only—no
path, worker error, transcript, or provider payload.

## Primary recommendations

`MeetingRecommendationResolver` returns zero or one presentational next step
from metadata already collected for that meeting. The ranking is deterministic:
recovery and setup blockers win over speaker repair, missing artifacts and
processing; summary retry wins over cleanup; cleanup wins over cosmetic
metadata work. The result carries a stable reason code, bounded target scope,
freshness, input revision, and a policy-versioned fingerprint. It never reads
audio, transcript, speaker-profile, path, or provider payloads.

Recommendations are suggestions, not execution authority. They may navigate to
a meeting or cleanup review, but they cannot invoke a destructive action,
expand to bulk scope, bypass a confirmation, or dispatch work automatically.
Stale or archived Sprint 2 row state promotes no action. Existing UI surfaces
may continue to show normal catalog actions and use the shared resolved result
for their concise explanation.

Users may dismiss only a matching low- or medium-risk cleanup, summary-retry,
or metadata recommendation. A dismissal expires after 30 days and is invalid
when its fingerprint or policy version changes. It cannot hide a failure,
blocked state, missing artifact, or required repair.

## View presets

Meetings has five named view intents: Recent, Needs Attention, Processing,
Archived, and Custom. A preset projects a fixed view/sort/group choice and
then applies the current search text; search never changes the selected preset.
Custom preserves its own valid table/group/sort/direction choices while a named
preset is active, and returns to those choices when selected again. The initial
choice happens once after a fresh catalog: one or more unresolved meetings use
Needs Attention; otherwise Recent. Background refreshes do not switch it.

Archived is intentionally source-gated. The preset can project supplied
archived records, but the installed workbench has no archive-history catalog
yet, so it clearly reports “Archive catalog is not available” rather than
scanning or presenting archive files as live meetings. This does not alter the
existing recoverable archive operation.
