# U2BIO.LabelPrinting

A .NET Framework 4.6.1 DLL provides USB text label printing through the manufacturer Wrapper API. Callers own database access and UI. Existing template editing and binding are optional.

## Current contract

Use `NFC_BT003-M01-04_Programming_Manual_v1.3.md` as the sole feature/API specification. Adopt the provided HFSample_UL BT003CtrlWrapper.cs declarations unchanged; keep our input, sequencing and result policy outside that file so SDK updates can be compared directly. Manufacturer files remain read-only references. Resolve converted-manual declaration errors against the original .cs; undocumented behavior is not a supported feature by assumption.

For public API, input or print changes, read [DESIGN.md](docs/DESIGN.md) and [PRINTER_API.ko.md](docs/PRINTER_API.ko.md). For sequencing and completion, read [IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md). Prior contracts are historical records under docs/history; the current MVP contract takes precedence for this change.

## MVP scope

- USB connection, file/string forms, SDK variable substitution, speed/darkness, printing and optional presentation/disconnection.
- TCP/IP, RS-232, RFID writing/security and new UI are outside this task. Preserve existing compatibility APIs and optional template/binding behavior.
- Manufacturer internal defects, new crash-isolation infrastructure and broad hardware/fault-injection campaigns are outside MVP completion. Our input validation, SDK call order, failure stop, result propagation and cleanup remain our responsibility.
- Reevaluate existing native implementation by responsibility; its presence does not require retaining its architecture. Preserve necessary protection through the simplest supported replacement. Fixed hashes replace general PE parsing for the pinned pair; legacy catalogs/probes belong to Validation. Keep the guarded StartLabelPrint pointer bridge until a StringBuilder replacement has equivalent output protection. Do not claim this exception is a completed sample-only conversion.

## Commands

```text
node tools/verify-repository.js
node tools/verify-build.js
git diff --check
```

Use the installed devflow root through the process-local DEVFLOW_ROOT when needed. Limit checks to the changed surface and existing required path; repeat only after a new change/failure. Do not add broad test campaigns or vendor SDK/device execution. Report unrun P/Invoke binding and StringBuilder/device checks as unverified, rather than making them successful by assumption.

## Boundaries

Work on a dedicated task branch and preserve unrelated tracked/untracked/ignored state. Use the installed devflow-state command for Issue reads and writes. The current request authorizes Issue #15 registration/tracking and local implementation, not push, merge, releases or deployment.

Manufacturer binaries, converted manuals, the BTPL reference and neighboring SDK/phase2 trees are read-only references. Publish only reviewed summaries and synthetic evidence; keep private paths, logs, label values, UID and credentials out of Issues. Preserve baseline tags and historical completion evidence. Interop code stays under Sdk/Interop so the existing protected review classification remains applicable.

## Deployment

Callers reference U2BIO.BT003PROLabelPrinting.dll. Runtime deployment also needs BlackfishNfcLib.dll and HFModule.dll matching the executable process bitness. Wrapper source is compiled into the managed DLL; vendor DLLs remain separate files. Do not add embedding/extraction, global installation or COM registration.
