# Diarization calibration evidence

The fixture catalog and calibration candidate files in this folder are
synthetic templates. A consented local corpus belongs under the ignored
`.artifacts/diarization-fixtures` folder and must use opaque fixture IDs.
It never ships with the application.

New governed catalogs use `fixture-catalog-v2.schema.json`. Keep inputs and
governance metadata in the private catalog; keep count/name expectations in a
separate restricted truth-store file outside source control. Pass it only to
the catalog runner with `-TruthStorePath`; runner reads truth after worker or
replay output is immutable and never forwards it to those processes.
Each v2 fixture declares one private `inputRoot`; every source/protected
artifact must stay under it and cannot traverse a symlink/reparse point, use
repo/report output, or Meeting Recorder app-data/profile paths.

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
