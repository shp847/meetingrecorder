# Clean-machine baseline checklist

Product owner accepts best-effort testing on the existing machine to advance
Store work. Label its results as existing-profile evidence; it cannot establish
clean-install behavior. Use generated consent-safe WAV fixtures only. Save
redacted screenshots and a metadata-only observer log outside the repository.

On 2026-10-30 the portable startup, MSI installation, installed bundle integrity,
and MSI-installed startup smoke checks passed on the existing Windows profile.
First-run and Store-package behavior were not exercised.

## Reset record

Record OS build, device type, network state, account type, permission state,
installed .NET Desktop Runtime, prior Meeting Recorder channel/version, model
cache state, and whether the image has been reset since the prior run.

## Ordered journey

1. Acquire MSI or candidate Store package; record warnings and trust copy.
2. Install and launch; record first visible action, keyboard focus, Narrator
   name/help, and any blocked control.
3. Complete the explicit capture-consent and permission path without Settings.
4. Use a generated fixture to record, process, publish, reopen, and find one
   meeting. Record elapsed time and recovery wording only.
5. Exercise model preparation, missing-model recovery, update/rollback where
   supported, uninstall, and reinstall/data-persistence behavior.

## Evidence discipline

For every step capture channel, package/version/hash, source/test/package/
installed/live category, pass/block/fail result, user decision, recovery route,
and owner. Exclude personal audio, transcript content, paths, identifiers,
credentials, and account details. A missing screenshot is a named blocker, not
a passing result.

## Control retirement ledger

For every visible installation, setup, Settings, Help, and update control,
record user outcome, default, config key, source/test owner, support route,
retain/relocate/retire proposal, migration effect, rollback, task-parity proof,
accessibility result, and product-owner approval. No control is retired solely
because it is technical or hidden.
