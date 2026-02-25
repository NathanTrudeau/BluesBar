# COBALT Commands

COBALT is safe-by-default: it writes only to `__COBALT__/` during Phase 1–2.

## Bootstrap
- `cobalt init`
  - ensures `__COBALT__/` exists and validates config files

## Phase 1: Repo Guardian
- `cobalt scan`
  - read-only scan of repo
  - writes outputs to `__COBALT__/_scanresults/`
- `cobalt report`
  - prints last report summary + changes since previous scan (if available)
- `cobalt pr-review <base> <head>`
  - reviews diff between refs and reports:
    - policy violations
    - risk hotspots
    - missing tests signals
    - summary + must-fix list

## Phase 2: QA Engineer
- `cobalt qa-run`
  - runs discovered build/test commands
  - writes logs into `__COBALT__/_runs/` and summary into `_scanresults/`
- `cobalt triage --last`
  - clusters failures and produces repro steps and suspects
- `cobalt flake-check <test>`
  - reruns and classifies likely flake (basic)

## Phase 3: Patch Mode (proposal + audited)
- `cobalt propose "<task>"`
  - produces:
    - plan
    - clarifying questions if needed
    - optionally a patch proposal folder in `_patches/`
- `cobalt patch create --issue <id>`
  - generates `__COBALT__/_patches/<id>/` with:
    - patch.yaml + files/ copies + notes.md
- `cobalt patch verify <id>`
  - auditor verifies by running checks
  - writes `audit.md`
- `cobalt patch apply <id> --branch cobalt/<id>`
  - applies ONLY the files in `_patches/<id>/files/` onto a new branch
  - re-runs verification commands

## Phase 4: Watch Mode
- `cobalt watch --once`
  - runs scan + qa-run + branch health summary
- `cobalt watch --schedule nightly`
  - config-driven scheduled runs (local time)

## Notes
- COBALT never merges. Operator reviews PRs and merges.
- COBALT asks questions when ambiguous or risky.
