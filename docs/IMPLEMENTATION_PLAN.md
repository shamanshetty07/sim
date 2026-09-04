# Implementation Plan & Status

Tracks the phase order from the project brief. Update the table after each
phase. This is the source of truth for "what's done" across sessions —
check it before assuming a phase needs to start from scratch.

| Phase | Description | Status |
|---|---|---|
| 1 | Inspect repository | ✅ Done — empty dir, no existing project. Greenfield build, Unity 2022.3 LTS. |
| 2 | Architecture + interfaces | ✅ Done — `docs/ARCHITECTURE.md`, folder structure, git init, Unity project skeleton (`ProjectSettings/`, `Packages/manifest.json`). |
| 3 | Drone flies correctly | ⏳ Next |
| 4 | FPV camera + OSD | ⬜ Not started |
| 5 | WorldSpecification models | ⬜ Not started |
| 6 | Mock AI service (hardcoded JSON → world) | ⬜ Not started |
| 7 | Connect real AI service (Reactor/Lingbot) | ⬜ Blocked — awaiting API key/docs from user |
| 8 | Prompt UI | ⬜ Not started |
| 9 | Procedural terrain | ⬜ Not started |
| 10 | Environment objects | ⬜ Not started |
| 11 | Racing obstacles | ⬜ Not started |
| 12 | Save/load | ⬜ Not started |
| 13 | Performance optimization | ⬜ Not started |
| 14 | Testing | ⬜ Ongoing — add tests as each system lands, not deferred to the end |

## Notes / decisions carried forward

- No Unity Editor in this environment — every phase's "compile" step is a
  careful manual review, not an actual Editor compile. Flag this at each
  phase summary; ask the user to open the project in their Editor at
  natural checkpoints (end of Phase 3 is the first one worth doing).
- Reactor Lingbot: user will provide API key later. Phase 7 is scaffolded
  with a `NotConfiguredException` stub until then; Phase 6's Mock service is
  the fully working path in the meantime.
- Target packages pinned in `Packages/manifest.json`: Input System 1.7.0,
  TextMeshPro 3.0.6, Test Framework 1.1.33, UnityWebRequest module (for the
  future Reactor client).
