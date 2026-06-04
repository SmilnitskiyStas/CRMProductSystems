---
description: Refactor a module to improve architecture, readability, and maintainability without changing behavior
argument-hint: <file path or module name>
---

# refactor-module.md

Refactor the module specified by: $ARGUMENTS

Act as a senior fullstack architect.

Follow this workflow:

1. Read `CLAUDE.md`.
2. Inspect the target module.
3. Identify architecture, duplication, typing, state, or responsibility issues.
4. Propose a refactor plan before changing code.
5. Keep behavior unchanged unless explicitly requested.
6. Refactor incrementally.
7. Update tests if applicable.
8. Update documentation if architecture changes.

Rules:
- No unnecessary rewrites.
- No new dependencies without justification.
- Preserve public API contracts unless approved.
- Prioritize readability and maintainability.

Output format:
- Current problems
- Refactor plan
- File structure changes
- Code changes
- Risk notes
- Final summary
