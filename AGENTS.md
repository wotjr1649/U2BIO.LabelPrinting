# U2BIO.LabelPrinting

A .NET Framework 4.6.1 DLL prints BTPL text on BT-003 Pro labels while preserving existing RFID data. The WinForms / DevExpress Tester consumes the same DLL; database access belongs to callers.

## Read what your task touches

| When changing | Read |
| --- | --- |
| Scope, public APIs, binding, connection or print behavior | [DESIGN.md](docs/DESIGN.md), affected C01-C24 cases |
| Native declarations, loader, ABI or deployment | [SDK_API_COVERAGE.md](docs/SDK_API_COVERAGE.md), DESIGN sections 3-5 and 8 |
| Development order or acceptance conditions | [IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md) and the matching Issue |
| Baseline documents or repository checks | [BASELINE.md](docs/BASELINE.md), [DEVELOPMENT.md](docs/DEVELOPMENT.md) |
| GitHub tracking or handoff | [GITHUB_ISSUES.md](docs/GITHUB_ISSUES.md), the devflow Issue and ledger |

## Commands

```text
node tools/verify-repository.js
git diff --check
```

The repository check prints `ok` and `Repository checks passed`. It checks documents and devflow structure, not DLL builds or printer behavior. Record product build and validation commands with their actual projects when introduced.

Use the installed devflow `devflow-state` command for Issue reads and writes. Its resume card provides the command path; DEVELOPMENT.md explains local setup.

## Boundaries

- Work within this repository on a task branch; preserve unrelated tracked, untracked and ignored files. Local checks and task commits are covered by the requested work.
- Push, merge, tags, releases and GitHub writes require the user's instruction for the specific target and effect. Templates and .devflow.json grant no additional authority.
- SDK binaries, converted manufacturer documents, the local BTPL reference guide and its verification script are read-only references. The neighboring SDK and phase2 source trees are reference inputs; changes there need separate scope.
- Publish reviewed summaries and synthetic test data. Keep local paths, raw logs, real label/RFID values and credentials out of Issues and commits.

## Gotchas

- Implement and verify the DLL before building the Tester. USB comes first; expose TCP/IP and RS-232 connection APIs too.
- BTPL owns layout; JSON owns mapping, format and nullText. Do not add DB access, DPI configuration, overflow checks, automatic wrapping, retries or queues.
- Port Open is not physical readiness. Unverified ABI, responses or font support cannot count as print success. Native timeout cannot forcibly abort a pending call.
- Preserve the baseline tag. Subsequent document revisions are new commits; do not rewrite it.
