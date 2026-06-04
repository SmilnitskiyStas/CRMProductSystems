# Skill: Create DTO

## Location
ShelfGuard.Application/Features/{Domain}/Dtos/

## Naming
- ProductDto — response
- CreateProductRequest — create
- UpdateProductRequest — update

## Pattern
Use C# records for immutability.
Never reference Domain entities in DTOs.
Add validation attributes on request records.

## Mapping
Map in service layer via static ToDto(entity) method.
Do not use AutoMapper unless mapping is complex.
