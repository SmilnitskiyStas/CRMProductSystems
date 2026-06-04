# Skill: Write Backend Tests

## Setup
xUnit + NSubstitute
Project: ShelfGuard.Tests/{Domain}/

## Test Naming Convention
MethodName_condition_expectedResult
Example: CreateAsync_returns_error_when_sku_already_exists

## Required Coverage
- Happy path
- Entity not found
- Duplicate / conflict
- Authorization guard

## Rules
- Test behavior, not implementation
- Mock only repos and external services
- Never mock domain entities
- Build + all tests must pass before handoff
