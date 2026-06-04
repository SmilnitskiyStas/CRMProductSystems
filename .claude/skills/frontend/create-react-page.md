# Skill: Create React Page

Location: frontend/app/(dashboard)/{feature}/page.tsx

Pattern:
- 'use client' directive at top
- Import feature hooks (useX from features/{domain}/hooks/)
- Import feature components
- Handle isLoading + isError states
- No direct API calls in page — use hooks

Rules:
- Pages are thin orchestrators
- State: formOpen, editingItem — local useState
- Server state: React Query only
