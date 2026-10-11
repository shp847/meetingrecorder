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
