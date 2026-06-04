# Skill: Add Validation

## Boundary Validation (request DTOs)
Use [Required], [MaxLength], [Range] on record parameters.
Return HTTP 400 for schema validation failures.

## Business Validation (service layer)
Check business rules in Application service.
Return (null, errorMessage) tuple for business conflicts.
Return HTTP 409 for business conflicts (e.g. duplicate SKU).

## Rule
Validate at boundary only. Never re-validate between internal layers.
