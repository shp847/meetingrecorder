# Speaker review release acceptance matrix

| Contract | Current evidence | Release state |
| --- | --- | --- |
| Identity/source precedence and stale edits | `SpeakerReviewSnapshotResolverTests`, `SpeakerCorrectionReceiptResolverTests` | Partial: receipt persistence/UI operation ids pending |
| Review surface and accessibility | `MainWindowXamlTests`; `speaker-review-accessibility.md` | Partial: fixture render matrix pending |
| Evidence, clips, overrides, merge | Existing focused service tests | Partial: remaining S3/S7 integration evidence pending |
| Learning, automatic names, past rematch | correction/matcher, eligibility, and rematch planner tests | Partial: dispatch and durable outcomes pending |
| Repair guidance | `SpeakerQualityDiagnosisResolverTests` | Partial: review binding/preflight pending |
| Summary freshness | `MeetingSummaryAttributionStateResolverTests` | Partial: persisted provenance/manual regeneration pending |
| Profile lifecycle/privacy | store/matcher/lifecycle tests and `TranscriptSchemaTests` | Partial: store serialization and package exclusion scan pending |
| Calibration no-go | calibration-script and promotion-gate tests | Partial: corpus manifest/metrics report pending |
| Installer/package smoke | `Test-All.ps1`, `Build-Installer.ps1`, `Smoke-Test-Release.ps1` | Blocked: 2026-09-29 Test-All built every project, then its core test host hung without results and was cancelled; installer/smoke not run |

Only synthetic or metadata-only fixtures may appear in release evidence. Do not
include profiles, embeddings, audio, transcript content, paths, or secrets.
