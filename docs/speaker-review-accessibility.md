# Speaker Review accessibility contract

Validate at 1280x800 and 1440x900, at 100% and 125% scaling, using sanitized
fixtures only. The Speaker Review well must remain before Organize & Fix, with
its status and pending count visible when a draft is edited.

Keyboard path: transcript speaker label → matching Speaker Review row → Meeting
Display Name field → Use/Reject suggestion → Apply Names result → transcript
label. Tab and Shift+Tab follow the visible order; Enter/Space activates buttons;
Escape closes only an open disclosure or dialog and does not discard a draft.

Profile path: profile row → Disable/Delete action → confirmation → originating
profile row or its nearest surviving neighbor. Repair and rematch remain separate
routes. Disabled controls expose why they are unavailable. Source, reason, count,
and status use text and accessible names; color alone never conveys readiness.

The Technical Studio acceptance checks are opaque nested wells, 1px structural
edges, maximum 4px radius, no shadows, no clipped essential action, and a visible
keyboard focus indicator. Status announces a completed user action once; ongoing
background work does not repeatedly announce progress.
