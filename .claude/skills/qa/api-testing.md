# Skill: API Testing

Tools:
- Swagger UI at http://localhost:5000/swagger (dev)
- .http files in /backend/ShelfGuard.Api/Http/

.http file format:
###
GET http://localhost:5000/api/products
Authorization: Bearer {{token}}

### Create product
POST http://localhost:5000/api/products
Content-Type: application/json
Authorization: Bearer {{token}}

{
  "sku": "TEST-001",
  "name": "Test Product"
}

Test matrix: 200, 201, 400, 401, 403, 404, 409
