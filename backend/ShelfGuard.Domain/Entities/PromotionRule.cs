using System.Security.Cryptography;
using System.Text;

namespace ShelfGuard.Domain.Entities;

/// <summary>
/// A POS price rule (promotion). The rule row holds identity + lifecycle only; the actual rule
/// (type, targets, scope, audience, params, limits, priority) lives in an immutable
/// <see cref="PromotionRuleVersion"/>. Publishing points <see cref="PublishedVersionId"/> at a
/// version; editing a published rule creates a new version. See .claude/docs/promotion-rules.md.
/// </summary>
public sealed class PromotionRule
{
    public const string StatusDraft = "draft";
    public const string StatusPublished = "published";
    public const string StatusPaused = "paused";
    public const string StatusArchived = "archived";

    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = StatusDraft;
    /// <summary>Latest version (the one being edited / last saved).</summary>
    public Guid? CurrentVersionId { get; set; }
    /// <summary>Version the POS applies while Status = published.</summary>
    public Guid? PublishedVersionId { get; set; }
    public Guid? CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Immutable snapshot of a rule definition (JSON, camelCase - the shape in promotion-rules.md).
/// Never updated after insert (also enforced by a BEFORE UPDATE trigger in the DB).
/// </summary>
public sealed class PromotionRuleVersion
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid RuleId { get; init; }
    public int VersionNumber { get; init; }
    public string DefinitionJson { get; init; } = "{}";
    /// <summary>SHA-256 (hex, lowercase) of <see cref="DefinitionJson"/> as stored.</summary>
    public string Checksum { get; init; } = string.Empty;
    public string? ChangeComment { get; init; }
    public Guid? CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public static PromotionRuleVersion Create(
        Guid tenantId, Guid ruleId, int versionNumber, string definitionJson,
        Guid? createdBy = null, string? changeComment = null) => new()
    {
        TenantId = tenantId,
        RuleId = ruleId,
        VersionNumber = versionNumber,
        DefinitionJson = definitionJson,
        Checksum = ComputeChecksum(definitionJson),
        CreatedBy = createdBy,
        ChangeComment = changeComment,
    };

    public static string ComputeChecksum(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
}

/// <summary>Execution log: one row per (sale, rule) that changed the price of at least one unit.</summary>
public sealed class PromotionRuleApplication
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid RuleId { get; init; }
    public Guid VersionId { get; init; }
    public Guid TransactionId { get; init; }
    public Guid StoreId { get; init; }
    public Guid? RegisterId { get; init; }
    /// <summary>The moment the rule was evaluated for (sale time; OccurredAt for offline sales).</summary>
    public DateTime AppliedAt { get; init; } = DateTime.UtcNow;
    /// <summary>Units sold at the promo price under this rule.</summary>
    public decimal Units { get; init; }
    /// <summary>Money given away by this rule on this sale (vs the pre-promo unit price).</summary>
    public decimal DiscountAmount { get; init; }
}
