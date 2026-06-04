# Skill: Create Migration

Command:
dotnet ef migrations add {Name} --project ShelfGuard.Infrastructure --startup-project ShelfGuard.Api

Naming convention:
- AddProductsTable
- AddStockIndexes
- AddRlsPolicies

After creating migration:
1. Review generated SQL in Migrations/{timestamp}_{Name}.cs
2. Verify Up() and Down() are correct
3. Add RLS policies manually in Up() if needed (EF does not generate them)
4. Run: dotnet ef database update --project ShelfGuard.Infrastructure --startup-project ShelfGuard.Api
