# Meeting Continuity Release Matrix

This is a synthetic-evidence ledger for continuity S0–S8. It contains no
recording title, transcript, audio, attendee, process, endpoint, or local path
from a user meeting. A package/install pass proves the stated package boundary;
it does not prove a live meeting outcome.

| Sprint | Required boundary | Evidence command or source | Current result |
| --- | --- | --- | --- |
| S0 | Public corpus is opaque, integrity checked, and contains protected negative cases. | `ContinuityReplayContractsTests` | Passed in continuity focus. |
| S1 | Matcher distinguishes Same/Different/Unknown and bounds grace. | `MeetingIdentitySnapshotTests`, `ContinuityCutoverPolicyTests` | Passed in continuity focus. |
| S2 | Shadow receipts are metadata-only and potential false merge remains reviewable. | `ContinuityShadowEngineTests` | Passed in continuity focus. |
| S3 | Corpus/replay metrics reject false merge and tampered/restricted fixtures. | `ContinuityReplayContractsTests`, `ContinuityDecisionTraceTests` | Passed in continuity focus. |
| S4 | Matcher cutover preserves manual stop and gates active transition mechanics. | `ContinuityCutoverPolicyTests`, `MainWindowStartupSourceTests` | Passed in continuity focus. |
| S5 | Strict current-work heal has complete-artifact, identity, lease, order, and conflict gates. | `OngoingMeetingHealEligibilityTests`, `OngoingMeetingHealTransactionTests` | Passed in continuity focus. |
| S6 | Callback trace is bounded/redacted; reentry and crash/restart recovery remain non-authoritative. | `ContinuityDecisionTraceTests`, callback/recovery source tests | Passed in continuity focus. |
| S7 | Local migration fails closed; review-only creates metadata-only evidence without mutation. | `AppConfigStoreTests`, `OngoingMeetingHealTransactionTests` | Passed in continuity focus. |
| S8 source journey | The release-built continuity suite exercises protected false merge, manual stop, Unknown grace, recovery/cutover, review-only, and trace redaction. | `scripts/Test-Continuity-Release.ps1` | Passed 2026-09-29: 187 tests against the exact bundled Core DLL. |
| Package layout | Required app/worker/CLI payloads and integrity manifest exist; diagnostic continuity traces cannot ship. | `scripts/Test-Continuity-Release.ps1` | Passed 2026-09-29: no trace payload; bundled and installed Core DLL hashes match. |
| Package/install startup | Portable, MSI install, installed integrity, and installed app survive their smoke windows while no user app is running. | `scripts/Smoke-Test-Release.ps1 -Runtime win-x64` | Passed 2026-09-29. |
| Full regression | Core, integration, and deployment boundaries report independently. | `Test-All.ps1`; direct integration and AppPlatform runs | Core 1,561/1,568; seven pre-existing debug-apphost/diarization fixture failures. Integration 8/8 and AppPlatform 7/7 pass. |
| Native UI/accessibility | Keyboard, Narrator, high contrast, and DPI status states are inspected in a controlled desktop session. | Manual rendered review | Open: no controlled native UI harness. |
| Live meeting behavior | A consented synthetic/non-user meeting validates auto-stop/restart without protected-process inspection. | Controlled live procedure | Open: no live meeting run in this package gate. |

## Release procedure

1. Confirm the user-owned app is closed. Never terminate it for this procedure.
2. Build with the stable apphosts in `C:\Users\psharm04\MeetingRecorder` only
   after their hashes match the current generated apphosts.
3. Run `scripts/Test-Continuity-Release.ps1 -Runtime win-x64`; it runs the
   release continuity journey and requires the tested Core DLL to match the
   portable and installed bundle. It fails if a trace sidecar is packaged.
4. Run `scripts/Smoke-Test-Release.ps1 -Runtime win-x64` from PowerShell 7.
5. Record ZIP/MSI hashes, test count, and source/package/installed/live status
   separately in `docs/auto-detection-cyberark-decision-log.md`.

## Support interpretation

- `Unknown` kept a meeting only through bounded grace; it never authorizes a
  new recording or merge.
- A heal receipt means strict identity and artifact checks passed; originals
  remain archive-first and can be reversed through the receipt flow.
- A review-only recommendation means the same checks found a candidate but no
  archive, lease, receipt, or artifact mutation occurred. Keep review-only or
  switch `Live` back to it if a potential false merge is reported.
- A corrupt or missing callback trace is diagnostic absence, not a recovery
  decision. Recover from normal manifest checkpoints and preserve explicit
  manual stop authority.
