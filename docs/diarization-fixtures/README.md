# Diarization calibration evidence

The fixture catalog and calibration candidate files in this folder are
synthetic templates. A consented local corpus belongs in a private root outside
source control and Meeting Recorder app-data; reports alone belong under the
ignored `.artifacts/diarization-fixtures` folder. Fixture IDs must be opaque.
The corpus never ships with the application.

New governed catalogs use `fixture-catalog-v2.schema.json`. Keep inputs and
governance metadata in the private catalog; keep count/name expectations in a
separate restricted truth-store file outside source control. Pass it only to
the catalog runner with `-TruthStorePath`; runner reads truth after worker or
replay output is immutable and never forwards it to those processes.
Each v2 fixture declares one private `inputRoot`; every source/protected
artifact must stay under it and cannot traverse a symlink/reparse point, use
repo/report output, or Meeting Recorder app-data/profile paths.

For VTT or Fireflies timestamped Markdown transcripts, use
`New-DiarizationVttFixture.ps1` with exactly one of `-VttPath` or
`-TranscriptMarkdownPath`. It copies one approved audio input into a private
fixture root and produces its manifest, transcript snapshot, restricted
count-only truth record, and v2 catalog. Speaker labels remain only in the
private source transcript; count taxonomy is inferred and speaker-name matching
stays disabled. Builder console output contains only opaque fixture ID,
category, and count—not input paths. To add another approved fixture to an
already reviewed root, pass `-Append`; duplicate IDs and mismatched catalog or
truth-store versions fail before the existing catalog is changed.

The builder accepts both timestamp ranges and Fireflies point timestamps in
the form `**speaker** *[m:ss]*: text`. Point-timestamp end times derive from
the next segment; the final or non-monotonic point receives a one-second
private snapshot interval. This normalization stays inside the private fixture
root and never enters catalog, truth, console, or report data.

When an approved source does not exist for a required category, use
`Add-DiarizationFixtureCoverageBlock.ps1` with an opaque ID and one of its
fixed block reasons. It writes a disabled catalog record only; it neither
creates fake audio/truth nor exposes a path. Run the catalog with
`-IncludeDisabled` to make those coverage gaps visible in its metadata-only
report. Replace the record with an approved fixture before enabling it.
The full-audio runner also supports a locally built worker launched through
`dotnet Worker.dll`; bounded native labeling relaunches that same assembly.
During `-NoBuild` replay, an older runtime-specific worker output is ignored in
favor of the newer framework build, preventing a stale publish from masking
current fixture behavior. If the runtime-specific output must be refreshed,
the runner forces a `win-x64` rebuild and treats successful rebuild completion
as freshness authority: copy operations may preserve timestamps, so timestamps
are a refresh trigger rather than post-build proof.

For bounded full-audio evidence, pass `-FixtureTimeoutSeconds` to the catalog
runner. A timeout writes a metadata-only `timed_out` result, terminates only the
fixture child process tree, and drains rather than exposing its console output.
Use `0` (the default) to rely on the worker's existing processing limit.

Run a metadata-only candidate comparison:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Test-DiarizationCalibration.ps1 `
  -CatalogPath .\.artifacts\diarization-fixtures\fixture-catalog.local.json `
  -CandidatePath .\.artifacts\diarization-fixtures\calibration-candidates.local.json `
  -ReportsRoot .\.artifacts\diarization-fixtures\reports
```

The report records SHA-256 fingerprints for the inputs and script, runtime
version, baseline/candidate aggregates, Wilson 95% speaker-count interval, and
explicit unavailable metrics. It contains no corpus paths, transcript text,
fixture titles, protected-artifact hashes, or profile data. Unsupported
measurements remain unavailable; zero is never used as a substitute.

Changing production thresholds is outside this command. It requires a governed
baseline/candidate run, protected-case review with no false automatic naming,
recorded human sign-off, and rollback evidence. Until then the report must say
`keep_current_defaults` and production defaults remain unchanged.

Calibration variables are ignored by normal app and worker processes. The
comparison script alone marks its child process as the calibration harness.
