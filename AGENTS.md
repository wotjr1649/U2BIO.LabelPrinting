# U2BIO.LabelPrinting

## Commands

Run `node tools/verify-repository.js` and `git diff --check` for documentation changes. Preserve the contract links required by the checker. For product, test, tool or solution changes, run `node tools/verify-build.js`; follow the repository's declared verification gate before handoff. Set `DEVFLOW_ROOT` to the installed plugin root in the process environment. Managed harness and assembly checks do not prove SDK or physical printing success.

## Read when relevant

- When changing printing contracts, read [SYNC_USB_MVP](docs/specs/SYNC_USB_MVP.md) and [the public API](docs/PRINTER_API.ko.md). Use [DESIGN](docs/DESIGN.md) for connection ownership and cleanup flow.
- When changing TXT editing or saving, read [the TXT contract](docs/specs/TXT_TEMPLATE_EDITOR.md) and [the editing API](docs/BTPL_TEMPLATE_API.ko.md).
- When changing the Tester, read [its specification](docs/specs/DEVEXPRESS_TESTER.md). User instructions belong in [Tester and media operations](docs/PRINT_PRESENTATION.ko.md), not in this file.
- When changing media, coordinates, presentation or preparing hardware checks, read [the operating baseline](docs/PRINT_PRESENTATION.ko.md#운영-기준). Changing a `^Q` value can change both the printer's media setting and stop position; its first argument excludes the mark.
- When building or preparing runtime files, read [DEVELOPMENT](docs/DEVELOPMENT.md) and [deployment](docs/SDK_DEPLOYMENT.ko.md). Historical plans and evidence are reference material, not current work instructions. Track current work in its Issue.

## Boundaries

- Keep the owned printing core in `Bt003Printer.cs` and `PrinterModels.cs`. Use the manufacturer's original Wrapper's 39 declarations directly. SDK source, binaries and manuals are read-only.
- Explain SDK sequencing, failure stops and resource lifetime in Korean comments. Keep the Wrapper thin; do not add a custom loader, execution framework or full BTPL interpreter. Reuse the optional Templates API for editing and diagnostics. Keep DevExpress and UI rendering in the Tester.
- One program uses a physical printer at a time. Preserve the single connection owner and serialized calls within the process. SDK success is not physical output completion.
- Execute SDK loading, printing and movement only for a user-authorized test whose output the user observes. Push, merge and deployment require the user's instruction naming their target and effect. Preserve user files and other worktrees.
- Use the installed `devflow-state` for Issue reads and writes. Publish reviewed summaries and synthetic evidence; exclude private paths, label values, UIDs and secrets.
- Keep `tests/` and `examples/OfflineSmoke/` in development verification. Product distribution excludes them; do not implement that policy by deleting sources or bypassing checks.
- Runtime files use the executing process's bitness: the managed DLL plus `BlackfishNfcLib.dll` and `HFModule.dll`. Do not embed/extract manufacturer binaries or register them as COM. Tester-specific runtime files are listed in the deployment guide.
