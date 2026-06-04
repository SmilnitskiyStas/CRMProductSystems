# Skill: Regression Testing

Run after any backend change:
1. dotnet test — all unit tests pass
2. Swagger: GET /api/products, /api/stock — still works
3. Frontend: open inventory page, add/edit/delete product

Run after database migration:
1. dotnet ef database update succeeds
2. API starts without exceptions
3. Existing data readable

FEFO regression:
- Add 2 batches with different expiry dates
- Consume — verify oldest consumed first
