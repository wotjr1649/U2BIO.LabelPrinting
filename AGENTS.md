# U2BIO.LabelPrinting

A .NET Framework 4.6.1 DLL provides USB text label printing through the manufacturer Wrapper API. Callers own database access and UI. Existing template editing and binding are optional.

## Current contract

Use `NFC_BT003-M01-04_Programming_Manual_v1.3.md` as the sole feature/API specification. Use the provided C# Wrapper API prototypes as integration code. Preserve manufacturer files and call their SDK rather than reimplementing its functions. Undocumented behavior is not a supported feature by assumption.

For public API, input or print changes, read [DESIGN.md](docs/DESIGN.md) and [PRINTER_API.ko.md](docs/PRINTER_API.ko.md). For sequencing and completion, read [IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md). Prior contracts are historical records under docs/history; the current MVP contract takes precedence for this change.

## MVP scope

- USB connection, file/string forms, SDK variable substitution, speed/darkness, printing and optional presentation/disconnection.
- TCP/IP, RS-232, RFID writing/security and new UI are outside this task. Preserve existing compatibility APIs and optional template/binding behavior.
- Manufacturer internal defects, new crash-isolation infrastructure and broad hardware/fault-injection campaigns are outside MVP completion. Our input validation, SDK call order, failure stop, result propagation and cleanup remain our responsibility.
- Reuse existing loader, input and response protections and mandatory checks. A Wrapper refactor does not authorize removing controls, accepting invalid input or claiming unobserved device success.

## Commands

```text
node tools/verify-repository.js
node tools/verify-build.js
git diff --check
```

Use the installed devflow root through the process-local DEVFLOW_ROOT when needed. Run the checks appropriate to the changed surface; repeat only after a new change/failure. Do not execute vendor SDK, printer/USB commands or SDK logging/cache effects in this task. Document unrun hardware checks without making them development prerequisites.

## Boundaries

Work on a dedicated task branch and preserve unrelated tracked/untracked/ignored state. Use the installed devflow-state command for Issue reads and writes. The current request authorizes Issue #15 registration/tracking and local implementation, not push, merge, releases or deployment.

Manufacturer binaries, converted manuals, the BTPL reference and neighboring SDK/phase2 trees are read-only references. Publish only reviewed summaries and synthetic evidence; keep private paths, logs, label values, UID and credentials out of Issues. Preserve baseline tags and historical completion evidence. Interop code stays under Sdk/Interop so the existing protected review classification remains applicable.

## Deployment

Callers reference U2BIO.BT003PROLabelPrinting.dll. Runtime deployment also needs BlackfishNfcLib.dll and HFModule.dll matching the executable process bitness. Wrapper source is compiled into the managed DLL; vendor DLLs remain separate files. Do not add embedding/extraction, global installation or COM registration.
