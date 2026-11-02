# GPU Transcription Converter Runbook

This is a disposable feasibility procedure. It does not change Meeting
Recorder, its installer, app settings, model catalog, or shipped assets.

## Boundaries

- Use only `C:\\code\\MeetingRecorder-TestRuntime\\gpu-converter-bootstrap` or
  another disposable root outside this repository.
- Use only synthetic or explicitly approved consented WAV files.
- Never copy converter environments, model files, tokens, logs, or benchmark
  outputs into this repository.
- Do not use the experimental Windows ML Runtime API. The candidate runtime is
  self-contained Windows ML supported ONNX APIs with DirectML.

## Converter host gate

The host must be a real, isolated Python 3.11.x with `pip`; the Windows Store
alias is rejected. Verify it before downloading any converter/model package:

```powershell
powershell -ExecutionPolicy Bypass -File .\\scripts\\Test-GpuTranscriptionConverterHost.ps1 `
  -PythonPath C:\\path\\to\\python.exe
```

The target converter is Microsoft ONNX Runtime Whisper conversion source,
pinned to an exact Git revision and package lock inside disposable root. Its
current guide marks module deprecated in favor of Olive recipes. Olive is not
used here because documented Windows setup can require Developer Mode or
administrator execution. This is experiment-only choice, not product dependency
or endorsement of deprecated module.

Before conversion, record all of following in private receipt:

- ONNX Runtime Git SHA and the pinned requirements-file SHA-256;
- Python/pip/package lock versions and wheel hashes;
- `openai/whisper-base.en` source revision, license, and source hash;
- converter arguments, timestamp/segment mapping, and output file hashes;
- CPU fallback result and Windows ML candidate result for same approved input.

Stop if any input is unpinned, unavailable, or produces incompatible
timestamp/segment contract. Do not substitute a different Whisper size, model,
or tokenizer silently.

## Bundle admission

Only converter result containing `tokenizer.json` plus either a single
`model.onnx` or paired `encoder.onnx` and `decoder.onnx` is admitted. The
current Windows ML-safe no-beam-search conversion produces the paired layout;
it does not rely on the ONNX Runtime-only beam-search operator. Generate a
source-control-external manifest after validation:

```powershell
powershell -ExecutionPolicy Bypass -File .\\scripts\\New-GpuTranscriptionAssetManifest.ps1 `
  -BundleRoot C:\\external\\whisper-base-en-onnx `
  -OutputPath C:\\external\\whisper-base-en-onnx.manifest.json `
  -SourceRevision <pinned-source-revision> `
  -ConverterVersion <pinned-converter-version> `
  -ModelLicense Apache-2.0 `
  -TimestampContractVersion <pinned-contract-version>
```

Manifest stores only bundle-relative file paths, byte counts, and SHA-256
hashes. It never writes source audio, transcript text, private paths, tokens,
or provider responses.

## Current environment result, 2026-10-17

Python 3.11.9 official portable archive was isolated under test root
(SHA-256 `009D6BF7E3B2DDCA3D784FA09F90FE54336D5B60F0E0F305C37F400BF83CFD3B`).
PyPA bootstrap was retrieved by a pinned `pypa/get-pip` Git revision after the
direct bootstrap endpoint returned Cisco HTML. The private package lock is
SHA-256 `0A21F3D65397741C17708A5B6AB2CAE9DA799C9F6FACD3A8152C1C9B0132D056`.

The downloaded `openai/whisper-base.en` snapshot was staged read-only and
hash-checked outside the repository. The current converter source was not
compatible with its own `transformers==5.3.0` requirement, so the isolated
runner is pinned to ONNX Runtime v1.22.0 commit
`f217402897f40ebba457e2421bc0a4702771968e` and Transformers 4.42.4. A local
FP32 no-beam conversion completed: encoder and decoder graphs passed ONNX
validation and their export-time PyTorch comparisons. An isolated .NET 8
Windows ML harness then loaded both graphs with the bundled ONNX Runtime CPU
provider and DirectML provider. A published unpackaged harness included
`Microsoft.Windows.AI.MachineLearning.dll`, `onnxruntime.dll`, and
`DirectML.dll` and also passed the DirectML graph-load gate. A synthetic
zero-valued mel tensor completed an encoder DirectML inference, without using
audio. A decoder start-token step plus one cache-continuation step also
completed through DirectML. This is only graph-load, synthetic
encoder/decoder-execution, and deployment-layout proof: the Windows ML runtime
reported that some shape operations remain on CPU, and no audio inference,
real decoding/tokenization, timestamp behavior, CPU comparison, fallback, or
user-standard package run has been proved. No product asset or converter output
is part of Meeting Recorder.
