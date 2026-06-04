# Coding Standards

**Last updated:** 2026-06-03
**Source:** personal-engineering-standards skill + project conventions

## C# (.NET)
- sealed classes for services and controllers where no inheritance needed
- Primary constructor syntax where applicable (C# 12)
- Records for DTOs (immutable)
- Nullable reference types enabled (Nullable=enable)
- No magic strings — use constants or enums
- Async all the way — no .Result or .Wait()
- CancellationToken on every async method

## TypeScript (Frontend)
- strict: true always
- type for shapes, interface for contracts
- No any — use unknown if type is open
- zod for API boundary validation
- Named exports for components (no default export in features)

## Naming
- C#: PascalCase for everything public, _camelCase for private fields
- TS: camelCase variables, PascalCase components/types, SCREAMING_SNAKE for constants
- DB: snake_case for all table and column names
- Booleans: is_, has_, can_, should_ prefix

## Testing
- Test names: "MethodName_condition_expectedResult"
- No empty catch blocks
- No test that only covers happy path
