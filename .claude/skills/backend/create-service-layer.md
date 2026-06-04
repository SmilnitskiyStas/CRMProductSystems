# Skill: Create Service Layer

## Interface (Application layer)
- Define in ShelfGuard.Application/Features/{Domain}/
- Return (Result, Error) tuples for business failures
- Throw exceptions only for unexpected/infrastructure errors

## Implementation
- sealed class
- Constructor injection only
- Business logic here, never in controllers

## DI Registration
In ShelfGuard.Application/DependencyInjection.cs:
services.AddScoped<IService, ServiceImpl>();
