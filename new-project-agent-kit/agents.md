# Project-Specific AGENTS.md Template

Use this as the repo-root `AGENTS.md` for a new project after a global
`~/.codex/AGENTS.md` baseline is in place. Keep this file focused on rules that
are specific to the project. Keep `envrestrictions.md` beside it when local
environment constraints materially affect setup, verification, packaging, or
release work.

Remove any section that does not add concrete project-specific guidance.

## Project Boundary

- Purpose:
- In scope:
- Out of scope:
- External systems or services:

## Local Sources Of Truth

- `README.md`:
- Architecture or design docs:
- Environment restrictions:
- Release or deployment docs:

## Run And Verify

- Main verification:
  - Command: `<command>`
  - Defined in: `<file>`
  - Use when: `<scope>`
- Focused tests:
  - Command: `<command>`
  - Defined in: `<file or project>`
  - Use when: `<scope>`
- Typecheck/lint:
  - Command: `<command>`
  - Defined in: `<file>`
  - Use when: `<scope>`
- Build/package:
  - Command: `<command>`
  - Defined in: `<file>`
  - Artifacts: `<paths>`
- Smoke test:
  - Command: `<command>`
  - Defined in: `<file>`
  - Proves: `<launch path, health check, artifact validation, etc.>`
- Do not use for validation:
  - Command/path: `<command or path>`
  - Reason: `<why it is insufficient or unsafe>`

## Generated, Binary, And Artifact Files

- Source files:
- Generated files:
- Build/package outputs:
- Files that should not be committed:
- Files that are intentionally versioned:

## Data, Security, And Privacy Overrides

- Project-specific sensitive data:
- Approved credential locations:
- Project-specific external systems or write targets:
- Redaction requirements:

## Product, UX, Or Content Constraints

- Binding design system or product brief:
- Human-owned copy or content:
- Brand, naming, or terminology rules:
- UI or accessibility constraints:

## Packaging, Release, And Deployment Rules

- Deployment target:
- Release gate:
- Required artifact rebuilds:
- Release-owner or approval rules:
- Post-deploy smoke tests:

## Local Workflow Notes

- Common local paths:
- Known environment limitations:
- Recovery or troubleshooting commands:
- Project-specific gotchas:
