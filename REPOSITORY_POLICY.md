# Repository Policy — SCLOC-Verse

> Governance for the split: **private development** + **public Release Hub**.

## 1. Source code

- Active development is performed **only** in the private repository.
- New source code is not published in this public repository.
- Historical source in legacy branches (`master`, `dev`, …) is kept as a read-only archive.

## 2. Public repository role

- This repository is a **Release Hub**: releases, changelog, documentation, and issue tracker.

## 3. Releases

- Releases are published only by project maintainers following the official release procedure.
- After CI/CD is introduced, releases will be published exclusively through the release pipeline.

## 4. Branches

- `release-hub` — the public default branch (this content).
- Legacy branches (`master`, `dev`, `dev-menu`, `dev-ocr`, `feature/*`) — read-only historical archive; no new commits.

## 5. Rollback

- Releases are immutable. A defective release is superseded by a higher-version hotfix, never regressed.

## 6. Auto-Update

- The desktop client checks `api.github.com/repos/Vova-Bob/SCLOC-Verse/releases/latest`.
- This endpoint and repository identity are stable; existing users keep receiving updates.
