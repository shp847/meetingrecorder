# Calibration Parameter Map

Candidates may set only the variables below. They apply only when
`MEETING_RECORDER_CALIBRATION_HARNESS=1`; normal app and worker processes ignore
them. Every rollback is `unset`: the runtime uses the listed default/fallback.
Candidates must state one hypothesis and change the smallest coherent set.

| Variable | Runtime owner | Unit / allowed range | Default or fallback |
| --- | --- | --- | --- |
| `MEETING_RECORDER_DIARIZATION_DEFAULT_THRESHOLD` | `DiarizationThresholdOptions` | fraction, 0.01–1 | 0.50 |
| `MEETING_RECORDER_DIARIZATION_COLLAPSED_THRESHOLDS` | `DiarizationThresholdOptions` | comma/semicolon fractions, each 0.01–1 | 0.45, 0.40, 0.35, 0.30, 0.25 |
| `MEETING_RECORDER_DIARIZATION_OVERSEGMENTED_THRESHOLDS` | `DiarizationThresholdOptions` | comma/semicolon fractions, each 0.01–1 | 0.55 through 0.99 (pinned runtime list) |
| `MEETING_RECORDER_DIARIZATION_MIN_SPEAKER_DURATION_SECONDS` | `DiarizationClusterSelectionOptions` | seconds, 0.1–60 | 2 |
| `MEETING_RECORDER_DIARIZATION_MAX_MIN_SPEAKER_DURATION_SECONDS` | `DiarizationClusterSelectionOptions` | seconds, 0.1–120 | 15 |
| `MEETING_RECORDER_DIARIZATION_MIN_SPEAKER_DURATION_SHARE` | `DiarizationClusterSelectionOptions` | fraction, 0–1 | 0.005 |
| `MEETING_RECORDER_DIARIZATION_MAX_PREFERRED_SPEAKER_COUNT` | `DiarizationClusterSelectionOptions` | count, 2–16 | 8 |
| `MEETING_RECORDER_SPEAKER_CLUSTER_TINY_MAX_SECONDS` | `SpeakerClusterMergeOptions` | seconds, 0–300 | 15 |
| `MEETING_RECORDER_SPEAKER_CLUSTER_SMALL_MAX_SECONDS` | `SpeakerClusterMergeOptions` | seconds, 0–600 | 30 |
| `MEETING_RECORDER_SPEAKER_CLUSTER_SMALL_SHARE` | `SpeakerClusterMergeOptions` | fraction, 0–1 | 0.03 |
| `MEETING_RECORDER_SPEAKER_CLUSTER_HIGH_CONFIDENCE_THRESHOLD` | `SpeakerClusterMergeOptions` | fraction, 0.01–1 | 0.88 |
| `MEETING_RECORDER_SPEAKER_CLUSTER_SMALL_THRESHOLD` | `SpeakerClusterMergeOptions` | fraction, 0.01–1 | 0.60 |
| `MEETING_RECORDER_SPEAKER_NAME_AUTO_APPLY_CONFIDENCE_THRESHOLD` | `SpeakerNameRecognitionOptions` | fraction, 0.01–1 | configured fallback |
| `MEETING_RECORDER_SPEAKER_NAME_SUGGESTION_CONFIDENCE_THRESHOLD` | `SpeakerNameRecognitionOptions` | fraction, 0.01–1 | configured fallback |
| `MEETING_RECORDER_SPEAKER_NAME_MATCH_MARGIN_THRESHOLD` | `SpeakerNameRecognitionOptions` | fraction, 0–1 | configured fallback |
| `MEETING_RECORDER_SPEAKER_NAME_MIN_AUTO_APPLY_PROFILE_SAMPLES` | `SpeakerNameRecognitionOptions` | count, 1–100 | configured fallback |
| `MEETING_RECORDER_SPEAKER_NAME_MIN_AUTO_APPLY_SPEECH_SECONDS` | `SpeakerNameRecognitionOptions` | seconds, 0–600 | configured fallback |

Invalid values and invalid lists revert to the same fallback; no candidate may
set an unlisted environment variable. A redacted baseline, matching candidate,
complete protected coverage, human review, and rollback evidence are required
before a production-default edit.
