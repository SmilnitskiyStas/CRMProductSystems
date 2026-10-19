using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Promotions;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>Promotion (price) rules - manager CRUD, lifecycle, validation, simulation. Thin: logic in <see cref="IPromotionRuleService"/>.</summary>
[ApiController]
[Route("api/promotion-rules")]
[Authorize(Policy = AppPolicies.AtLeastStoreManager)]
[RequireModule("pos")]
public sealed class PromotionRulesController : ControllerBase
{
    private readonly IPromotionRuleService _rules;

    public PromotionRulesController(IPromotionRuleService rules) => _rules = rules;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<RuleListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] Guid? storeId, CancellationToken ct) =>
        TryGetCaller(out var c) ? Ok(await _rules.ListAsync(c, status, storeId, ct)) : Forbid();

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(RuleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        TryGetCaller(out var c) ? Map(await _rules.GetAsync(c, id, ct)) : Forbid();

    [HttpPost]
    [ProducesResponseType(typeof(RuleDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreatePromotionRuleRequest request, CancellationToken ct) =>
        TryGetCaller(out var c) ? Map(await _rules.CreateAsync(c, request, ct)) : Forbid();

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(RuleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePromotionRuleRequest request, CancellationToken ct) =>
        TryGetCaller(out var c) ? Map(await _rules.UpdateAsync(c, id, request, ct)) : Forbid();

    [HttpPost("{id:guid}/validate")]
    [ProducesResponseType(typeof(RuleValidationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Validate(Guid id, CancellationToken ct) =>
        TryGetCaller(out var c) ? Map(await _rules.ValidateAsync(c, id, ct)) : Forbid();

    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType(typeof(RuleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RuleValidationDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct) =>
        TryGetCaller(out var c) ? Map(await _rules.PublishAsync(c, id, ct)) : Forbid();

    [HttpPost("{id:guid}/pause")]
    [ProducesResponseType(typeof(RuleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Pause(Guid id, CancellationToken ct) =>
        TryGetCaller(out var c) ? Map(await _rules.PauseAsync(c, id, ct)) : Forbid();

    [HttpPost("{id:guid}/resume")]
    [ProducesResponseType(typeof(RuleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Resume(Guid id, CancellationToken ct) =>
        TryGetCaller(out var c) ? Map(await _rules.ResumeAsync(c, id, ct)) : Forbid();

    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType(typeof(RuleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct) =>
        TryGetCaller(out var c) ? Map(await _rules.ArchiveAsync(c, id, ct)) : Forbid();

    [HttpPost("simulate")]
    [ProducesResponseType(typeof(SimulateResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Simulate([FromBody] SimulateRequest request, CancellationToken ct) =>
        TryGetCaller(out var c) ? Map(await _rules.SimulateAsync(c, request, ct)) : Forbid();

    [HttpGet("lookups/items")]
    [ProducesResponseType(typeof(IReadOnlyList<ItemLookupDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> LookupItems(
        [FromQuery] string? search, [FromQuery] Guid? categoryId, [FromQuery] int? limit, [FromQuery] string? ids, CancellationToken ct)
    {
        if (!TryGetCaller(out var c)) return Forbid();
        var parsed = string.IsNullOrWhiteSpace(ids)
            ? null
            : ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => Guid.TryParse(x, out var g) ? g : (Guid?)null)
                .Where(g => g.HasValue).Select(g => g!.Value).ToList();
        return Ok(await _rules.LookupItemsAsync(c, search, categoryId, limit, ct, parsed));
    }

    private IActionResult Map<T>(PromotionOutcome<T> outcome)
    {
        if (outcome.Validation is not null) return StatusCode(outcome.Status, outcome.Validation);
        if (outcome.Error is not null) return StatusCode(outcome.Status, new { error = outcome.Error });
        return StatusCode(outcome.Status, outcome.Value);
    }

    private bool TryGetCaller(out PromotionRuleCaller caller)
    {
        caller = null!;
        if (!Guid.TryParse(User.FindFirstValue("tenant_id"), out var tenantId) || tenantId == Guid.Empty) return false;
        var userIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdRaw, out var userId)) return false;
        Guid? storeId = Guid.TryParse(User.FindFirstValue("store_id"), out var sid) && sid != Guid.Empty ? sid : null;
        caller = new PromotionRuleCaller(tenantId, userId, User.FindFirstValue(ClaimTypes.Role) ?? string.Empty, storeId);
        return true;
    }
}
