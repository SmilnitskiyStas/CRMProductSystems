# Skill: Manual Test Checklist

For every completed feature, test:

Happy path:
- Create works with valid data
- Read returns correct data
- Update changes only specified fields
- Delete removes or soft-deletes

Edge cases:
- Empty list returns 200 with [] not 404
- Not found returns 404
- Duplicate returns 409
- Invalid data returns 400

Tenant isolation:
- User from tenant A cannot see tenant B data
- Tenant ID in JWT, not in request

FEFO (for stock operations):
- Oldest batch consumed first
- expiry_date unchanged after transfer
