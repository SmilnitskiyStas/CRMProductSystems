---
description: Create a production-ready fullstack feature following project architecture rules
argument-hint: <feature description, e.g. "inventory product catalog">
---

# create-feature.md

Create a production-ready feature based on: $ARGUMENTS

Follow this workflow:

1. Read `CLAUDE.md`.
2. Read relevant files in `.claude/docs/` if they exist.
3. Identify affected modules and layers.
4. Propose the feature architecture.
5. Show the expected file structure before writing code.
6. Explain the implementation plan briefly.
7. Implement incrementally.
8. Keep controllers thin and business logic in the application layer.
9. Keep frontend logic feature-based.
10. Use React Query for server state.
11. Avoid unnecessary global state.
12. Update documentation if architecture or domain behavior changes.

Output format:
- Summary
- Affected modules
- Proposed file structure
- Implementation plan
- Code changes
- Follow-up notes
