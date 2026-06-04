# Frontend Structure

**Owner:** frontend-developer
**Updated:** 2026-06-03

## Feature Directory Structure
frontend/features/{domain}/
  types.ts           — TypeScript interfaces for domain
  api/
    {domain}.ts      — apiFetch wrapper, API calls
  hooks/
    use{Domain}.ts   — React Query hooks
  components/
    {Domain}Table.tsx
    {Domain}Form.tsx

## Naming Conventions
- Hooks: useProducts, useCreateProduct, useDeleteProduct
- API modules: productsApi.getAll, productsApi.create
- Query keys: ["products"] as const, ["products", id]
- Components: PascalCase, named exports

## State Rules
- Server state: React Query only (never duplicate in Zustand)
- UI state (modal open, selected item): local useState in page
- Global UI state (sidebar, theme): Zustand only

## shadcn/ui Components
Install via: npx shadcn@latest add {component}
Never copy-paste shadcn source — always install through CLI.
