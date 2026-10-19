using System.Text.Json;

namespace ShelfGuard.Application.Features.Promotions;

// Phase 2 API contract: .claude/docs/promotion-rules.md "Phase 2 API contract (FINAL)". JSON camelCase.

public sealed record PromotionRuleCaller(Guid TenantId, Guid UserId, string Role, Guid? StoreId);

public sealed record RuleListItemDto(
    Guid Id, string Name, string? Description, string Status, string? Type,
    int CurrentVersionNumber, int? PublishedVersionNumber, IReadOnlyList<Guid> StoreIds,
    DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo, bool HasUnpublishedChanges, DateTime UpdatedAt);

public sealed record RuleVersionDto(
    Guid Id, int VersionNumber, JsonElement Definition, string? ChangeComment, string? CreatedByName, DateTime CreatedAt);

public sealed record RuleVersionSummaryDto(
    Guid Id, int VersionNumber, DateTime CreatedAt, string? CreatedByName, string? ChangeComment, bool IsPublished);

public sealed record RuleDetailDto(
    Guid Id, string Name, string? Description, string Status,
    RuleVersionDto CurrentVersion, RuleVersionDto? PublishedVersion, IReadOnlyList<RuleVersionSummaryDto> Versions);

public sealed record CreatePromotionRuleRequest(string Name, string? Description, JsonElement Definition);

public sealed record UpdatePromotionRuleRequest(
    string Name, string? Description, JsonElement Definition, string? ChangeComment);

public sealed record RuleIssueDto(string Code, string? Field, string Message);

public sealed record RuleValidationDto(IReadOnlyList<RuleIssueDto> Errors, IReadOnlyList<RuleIssueDto> Warnings);

public sealed record SimulateMembershipDto(Guid? TierId);

public sealed record SimulateLineRequest(Guid ItemId, decimal Quantity, decimal? UnitPrice);

public sealed record SimulateRequest(
    Guid? RuleId, JsonElement? Definition, Guid StoreId, DateTimeOffset? At,
    SimulateMembershipDto? Membership, IReadOnlyList<SimulateLineRequest> Lines);

public sealed record SimulateSegmentDto(decimal Quantity, decimal UnitPrice, string? Label);

public sealed record SimulateLineDto(
    Guid ItemId, string Name, decimal Quantity, decimal RegularUnitPrice,
    IReadOnlyList<SimulateSegmentDto> Segments, decimal LineTotal, decimal RegularLineTotal);

public sealed record SimulateResultDto(
    IReadOnlyList<SimulateLineDto> Lines, decimal Total, decimal RegularTotal, decimal Savings);

public sealed record ItemLookupDto(
    Guid Id, string Name, string? Barcode, decimal? PriceRetail, Guid? CategoryId, string? CategoryName);

/// <summary>Outcome of a service call: a value, or an HTTP status with an error / validation body.</summary>
public sealed record PromotionOutcome<T>(T? Value, int Status, string? Error = null, RuleValidationDto? Validation = null)
{
    public bool Ok => Error is null && Validation is null;
    public static PromotionOutcome<T> Success(T value, int status = 200) => new(value, status);
    public static PromotionOutcome<T> Fail(int status, string error) => new(default, status, error);
    public static PromotionOutcome<T> Invalid(RuleValidationDto validation) => new(default, 400, null, validation);
}
