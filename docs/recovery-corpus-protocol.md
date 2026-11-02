# Recovery quality corpus protocol

The recovery-quality corpus is local-only evidence for controlled evaluation. It
is not an application feature, a support bundle, or a release asset.

## Boundaries

- Store the corpus outside the repository and outside published transcript,
  installer, release, and diagnostic directories.
- Every clip requires a local consent/provenance register entry with a
  revocation state and expiry. The register may contain local paths; reports
  may not.
- Source audio, reference transcript text, speaker names, meeting titles,
  attendee data, device identifiers, and raw paths never enter source control,
  logs, support bundles, telemetry, or automated test fixtures.
- Revoking a clip removes it from future runs and invalidates aggregates that
  cannot be recomputed without it. Do not retain a copied audio or transcript
  payload after revocation.

## Minimum local register

Keep one local record per clip with opaque clip id, source SHA-256, consent
authority, classification, owner, created/expiry/revocation timestamps, and
reference revision. The 25-clip target must cover long calls, short calls,
split chains, low speech, microphone-heavy calls, and endpoint switches.

## Approved category rules

The following rules are evidence admission rules, not inferred topology. A
candidate that fails any rule remains held.

- `split-chain`: same platform, same non-generic normalized title, same local
  day, complete artifacts, no overlap, and adjacent boundaries within five
  minutes.
- `endpoint-switch`: one active session records two distinct, time-ordered
  endpoint observations. A queued, failed, or otherwise unstable record is not
  a golden fixture.
- `low-speech`: valid audio has an active-sample ratio below 20 percent, using
  the bounded `500 / short.MaxValue` amplitude threshold.
- `microphone-heavy`: both raw loopback and microphone tracks exist; weighted
  microphone activity is at least 20 percent and at least 20 percentage points
  above weighted loopback activity.

Reports retain only rule names, opaque clip IDs, hashes, and bounded aggregate
counts. They never retain endpoint values, titles, paths, transcript text, or
device names.

## Reporting

Persist only aggregate counts and bounded metrics (for example WER bands,
timestamp validity, false-start rate, fragment-chain rate, lease age, and
promotion recovery). A report uses opaque clip ids only and must be scanned for
paths, transcript text, credentials, and audio filenames before it leaves the
local evidence root.

## Review and removal

The evidence owner reviews consent, provenance, expiry, and revocation before
each benchmark. Expired, revoked, or unclassified clips are excluded. A release
claim needs a dated aggregate report plus its tool and corpus-register revision;
it never needs to include the corpus itself.

## Creating or refreshing the local register

Use `scripts/New-RecoveryQualityCorpusRegister.ps1` with a governed v2 private
fixture catalog. The registry destination must remain outside source control
and Meeting Recorder app data; the aggregate report must stay under ignored
`.artifacts` or the system temporary directory.

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\New-RecoveryQualityCorpusRegister.ps1 `
  -FixtureCatalogPath C:\Private\MeetingRecorder\fixture-catalog.local.json `
  -RegistryPath C:\Private\MeetingRecorder\recovery-quality-corpus-register.local.json `
  -AggregateReportPath .\.artifacts\recovery-corpus\aggregate.json
```

The command never emits source paths, owner/consent details, transcript text,
or audio. It creates a local register containing required provenance and emits
only aggregate eligibility/coverage counts. It rejects invalid destination
boundaries and never upgrades a missing category into a claimed category.
The sole automatic coverage tag is `long-call`, derived from an approved audio
header meeting the configured 45-minute threshold. It does not infer low
speech, endpoint switching, microphone use, split history, or any other
physical/capture topology.
