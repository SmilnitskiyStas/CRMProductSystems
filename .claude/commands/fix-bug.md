---
description: Investigate and fix a bug with minimal, production-safe changes
argument-hint: <bug description or affected file/module>
---

# fix-bug.md

Investigate and fix the bug described by: $ARGUMENTS

Follow this workflow:

1. Read `CLAUDE.md`.
2. Identify the affected feature/module.
3. Inspect the smallest relevant set of files.
4. Explain the likely root cause.
5. Propose the safest fix.
6. Apply the fix incrementally.
7. Add or update tests if applicable.
8. Explain what changed and why.

Rules:
- Do not rewrite unrelated code.
- Do not introduce new dependencies unless necessary.
- Keep the fix minimal but production-safe.
- Preserve existing architecture.

Output format:
- Root cause
- Files inspected
- Fix plan
- Code changes
- Test notes
- Final summary
