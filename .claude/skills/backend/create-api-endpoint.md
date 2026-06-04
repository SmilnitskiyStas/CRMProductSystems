# Skill: Create API Endpoint

## Controller Pattern (ASP.NET Core)
`csharp
[HttpGet("{id:guid}")]
[ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
{
    var result = await _service.GetByIdAsync(id, ct);
    return result is null ? NotFound() : Ok(result);
}
```

## Rules
- No business logic in controllers
- CancellationToken on all async methods
- RESTful naming: /api/[controller]
- ProducesResponseType for all HTTP codes
- Always [Authorize] unless explicitly public
