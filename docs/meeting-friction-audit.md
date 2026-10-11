# Meetings Friction Audit

Date: 2026-09-29. This is the baseline for the Meetings simplification work.
It inventories action identifiers and routes only; it contains no meeting
content, path, title, transcript, audio, attendee, or live UI data.

## Authority and disposition

[`meeting-capability-inventory.json`](meeting-capability-inventory.json) is the
machine-readable ledger. `MeetingActionCatalog` remains the source owner for
action family, selection cardinality, confirmation, outcome, and accessible
label. Supplemental toolbar, cleanup, and import controls are listed separately
so no operational route disappears during a catalog-focused UI change. The
validator requires every catalog action exactly once, every supplemental control
to still exist in Meetings XAML, a future home, at least one discovery surface,
and explicit typed confirmation for permanent delete.

| Disposition | Default home | Rule |
| --- | --- | --- |
| Default | row, selection, or library | Read/navigation plus one safe recommended route. |
| Grouped | action family/context/cleanup | Available by outcome; preserves catalog eligibility and confirmation. |
| DetailOnly | meeting detail | Contextual editing needs one focused meeting. |
| Advanced | context/diagnostic route | Useful support tooling, not a default task. |
| DestructiveExplicit | separate danger route | Typed permanent-delete confirmation; no recovery claim. |

## Synthetic journeys

| Journey | Entry | Expected safe outcome | Evidence |
| --- | --- | --- | --- |
| Find/read | Library search → row/context | One meeting opens details, transcript, audio, or folder only when the artifact is present. | Catalog selection/artifact eligibility tests. |
| Recover | Recommendation/fix group | Routes to existing review or recovery; never auto-dispatches. | Recommendation and action-catalog tests. |
| Speaker labels | Fix group/detail | Requires labeling readiness and preserves the detail route. | Action-catalog eligibility tests. |
| Archive/delete | Organize/Danger group | Archive stays recoverable; delete always uses typed confirmation. | Catalog confirmation validation. |
| Merge/split | Selected meetings/Fix group | Requires existing explicit review and correct selection scope. | Catalog selection validation. |
| Backlog | Library/Processing group | Queue priority changes only through its existing decision route. | Catalog processing eligibility. |

Native 1280×800/125% keyboard, overflow, and automation-tree capture are
operational checks for the later layout sprint. This audit does not claim those
rendered checks ran; it protects the action map before any control moves.
