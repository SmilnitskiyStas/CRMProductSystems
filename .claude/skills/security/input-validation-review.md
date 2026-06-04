# Skill: Input Validation Review

Checklist:
- All string inputs have MaxLength
- Numeric inputs have Range(min, max)
- Guid route params validated by [FromRoute] type binding
- Enum values validated (not free string)
- expiry_date: must be in future (on create)
- quantity: must be > 0 for new batches
- Batch number: alphanumeric + dash/slash only

SQL injection: EF Core parameterized queries — verify no raw SQL with user input
