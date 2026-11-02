# GPU Transcription Feasibility Scorecard

Status: Sprint 1 complete, candidate rejected, 2026-10-18. No runtime, model,
asset, setting, or product execution path is wired into the product by this
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
| Windows ML with included DirectML | [Windows ML execution providers](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/supported-execution-providers?tabs=winml2), [deployment guidance](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/distributing-your-app) | Windows ML includes CPU and legacy DirectML providers. A self-contained runtime adds about 41 MB and requires a supported Windows/.NET packaging contract. The product owner selected this as the sole isolated investigation path on 2026-10-17: use supported ONNX APIs, not the experimental Windows ML Runtime API; produce a reproducible OpenAI Whisper Standard ONNX model/tokenizer/timestamp bundle with pinned provenance. | Approved for isolated feasibility work only; not selected for product integration. |
| Direct ONNX Runtime with DirectML | [DirectML guidance](https://learn.microsoft.com/en-us/windows/ai/directml/dml-get-started) | DirectML remains supported but is in sustained engineering; Microsoft directs new Windows ONNX work toward Windows ML. Direct model conversion/provenance and full output compatibility remain unproven. | Deferred pending a reproducible Standard-model contract. |
| whisper.cpp with Vulkan | [whisper.cpp build source](https://github.com/ggml-org/whisper.cpp/blob/master/CMakeLists.txt), [project readme](https://github.com/ggml-org/whisper.cpp/blob/master/README.md?plain=1) | The project supports Windows and has a Vulkan build path, but this review has no signed standard-user Windows runtime/asset bundle, no package/update rollback proof, and no app-to-CLI transcript contract. | Not eligible for a user-facing spike until a no-user-setup distributable exists. |

## Candidate decision, 2026-10-18

The isolated DirectML candidate is rejected for product integration. It does
not change the CPU-first product path.

- Mechanics passed in the private approved corpus: 26/26 timestamp-mode
  DirectML decodes had exact token parity with the pinned Hugging Face CPU
  reference, and all 26 timestamp mappings were structurally valid.
- The candidate's private quality result failed: aggregate word error rate was
  0.991 against approved local transcript reference, with timestamp-boundary
  precision 0.100 and recall 0.635 at a one-second tolerance. Candidate output
  has no speaker labels, so speaker identity was deliberately not scored.
- Two complete private recordings (80 seconds and 230 seconds) replayed via
  resumable 30-second windows, with 11/11 valid timestamp-window receipts.
  This proves only isolated chunk mechanics, not product-quality output.
- Unavailable DirectML, CPU fallback, missing-resource failure, and subsequent
  resource recovery all completed cleanly in the disposable harness.
- The candidate can map its private text and timestamps into the basic
  `TranscriptSegment` field shape, but it was not compatible with the real
  production Whisper.NET output fingerprint: language and segment counts
  differed, so timestamp compatibility also failed.

No shipped asset, installer, setting, provider, or worker behavior is
authorized from these experiments. Any future candidate must restart the
private quality and production-schema gates; mechanical token parity alone is
not sufficient.

## Original experiment protocol

Before any product dependency or asset change, run a disposable, isolated
Standard-model experiment using the S0 corpus protocol. Pin source, toolchain,
runtime, model, and package-file hashes; capture only capability class and
aggregated cold/warm metrics. Compare CPU/candidate shape with
`GpuTranscriptionOutputFingerprint`, then manually review quality and CPU
fallback. Stop the candidate on any S0 no-go result.

Focused verification: `GpuTranscriptionPolicyTests`,
`GpuTranscriptionOutputFingerprintTests`, and `SessionProcessorTests` (20/20
passed on 2026-09-27).

## Approved investigation contract

- Runtime: self-contained `Microsoft.Windows.AI.MachineLearning`, using its
  supported ONNX APIs and DirectML execution provider. Do not use the
  experimental Windows ML Runtime API.
- Model source: OpenAI Whisper Standard model and tokenizer under the source
  repository's Apache-2.0 model license, converted reproducibly outside the
  repository. The conversion tool,
  source revision, input/output SHA-256 values, timestamp mapping, and package
  file list must be captured before any benchmark.
- Scope: a disposable CPU-versus-GPU harness only. It must consume only
  synthetic or approved consented WAV fixtures, create only redacted reports,
  and leave app, worker, installer, settings, and shipped assets unchanged.
- Stop: absence of a reproducible conversion, output/timestamp compatibility,
  CPU fallback, standard-user execution, or material measured benefit rejects
  the candidate and leaves CPU as the product path.

## Isolated result, 2026-10-17

The supplied Whisper Standard snapshot converted to a no-beam paired ONNX
bundle (encoder plus decoder). Both graphs passed ONNX validation. A disposable
.NET 8 harness using `Microsoft.Windows.AI.MachineLearning` loaded both graphs
through CPU and DirectML, including after unpackaged publish with the Windows
ML, ONNX Runtime, and DirectML DLLs beside the executable. A synthetic
zero-valued mel tensor also completed an encoder DirectML inference, followed
by a decoder start-token step and one cache-continuation step. This clears only
the conversion, graph-load, synthetic encoder/decoder-execution, and
deployment-layout sub-gates. It does not prove audio inference, real
generation/tokenization, timestamps, output parity, CPU fallback, performance,
or user-standard packaging; no product integration is authorized from this
result.

## Converter gate

`docs/gpu-transcription-converter-runbook.md` and
`scripts/Test-GpuTranscriptionConverterHost.ps1` define isolated converter host
gate. `scripts/New-GpuTranscriptionAssetManifest.ps1` accepts only external
converted bundle and records bundle-relative paths, sizes, and hashes. No
converter, model, runtime, or installer asset is part of product.
