# Skill: Create Component

Location: frontend/features/{domain}/components/

Pattern:
- Named export (no default for feature components)
- Props interface defined above component
- shadcn/ui primitives for all UI elements
- 'use client' only when hooks or events needed

Rules:
- One responsibility per component
- No API calls in components — receive data via props
- Shared/reusable UI goes to frontend/components/ui/
