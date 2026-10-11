# GPU Transcription Feasibility Scorecard

Status: Sprint 1 investigation baseline, 2026-09-27. No candidate is selected,
downloaded, copied into the repository, or wired into the product by this
record.

## Comparison harness boundary

`GpuTranscriptionOutputFingerprint` normalizes a `TranscriptionResult` into
language, ordered timestamp shapes, segment count, and character counts. It
does not retain transcript text, audio, paths, speaker data, model identifiers,
or exceptions. `GpuTranscriptionOutputComparison` can reject schema/timestamp
incompatibility but always requires manual quality review; shape parity is not
a transcript-quality claim.

## Candidate review

| Candidate | Primary evidence checked | S0 gate result | S1 status |
| --- | --- | --- | --- |
| Windows ML with included DirectML | [Windows ML execution providers](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/supported-execution-providers?tabs=winml2), [deployment guidance](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/distributing-your-app) | Windows ML includes CPU and legacy DirectML providers. A self-contained runtime adds about 41 MB and requires a supported Windows/.NET packaging contract. No matching Whisper ONNX model, tokenizer, timestamps, license, or rollback asset tuple is proven. | Pending only as an isolated investigation candidate. |
| Direct ONNX Runtime with DirectML | [DirectML guidance](https://learn.microsoft.com/en-us/windows/ai/directml/dml-get-started) | DirectML remains supported but is in sustained engineering; Microsoft directs new Windows ONNX work toward Windows ML. Direct model conversion/provenance and full output compatibility remain unproven. | Deferred pending a reproducible Standard-model contract. |
| whisper.cpp with Vulkan | [whisper.cpp build source](https://github.com/ggml-org/whisper.cpp/blob/master/CMakeLists.txt), [project readme](https://github.com/ggml-org/whisper.cpp/blob/master/README.md?plain=1) | The project supports Windows and has a Vulkan build path, but this review has no signed standard-user Windows runtime/asset bundle, no package/update rollback proof, and no app-to-CLI transcript contract. | Not eligible for a user-facing spike until a no-user-setup distributable exists. |

## Required next experiment

Before any product dependency or asset change, run a disposable, isolated
Standard-model experiment using the S0 corpus protocol. Pin source, toolchain,
runtime, model, and package-file hashes; capture only capability class and
aggregated cold/warm metrics. Compare CPU/candidate shape with
`GpuTranscriptionOutputFingerprint`, then manually review quality and CPU
fallback. Stop the candidate on any S0 no-go result.

Focused verification: `GpuTranscriptionPolicyTests`,
`GpuTranscriptionOutputFingerprintTests`, and `SessionProcessorTests` (20/20
passed on 2026-09-27).
