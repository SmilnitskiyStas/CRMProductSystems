---
description: Generate or update project documentation and store it in .claude/docs/
argument-hint: <topic, module, or document name, e.g. "domain-model" or "API contracts">
---

# generate-docs.md

Generate or update project documentation for: $ARGUMENTS

Use `CLAUDE.md` as the source of project rules.

Documentation should be stored in `.claude/docs/`.

Prefer small focused documents over one large document.

Possible documents:
- architecture.md
- domain-model.md
- api-contracts.md
- ai-integration.md
- modules.md
- decisions.md
- workflows.md

Output format:
- Document created/updated
- Summary of content
- Important decisions
- Open questions
- Next suggested document

Keep documentation concise, structured, and actionable.
