using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Promotions;

public static class PromotionRuleLoader
{
    /// <summary>Parses published rows into engine inputs; unparseable definitions are skipped.</summary>
    public static IReadOnlyList<PromotionRuleInput> ToInputs(IEnumerable<PublishedPromotionRuleRow> rows)
    {
        var result = new List<PromotionRuleInput>();
        foreach (var row in rows)
        {
            var def = PromotionDefinitionJson.TryParse(row.DefinitionJson);
            if (def is null) continue;
            result.Add(new PromotionRuleInput(
                row.RuleId, row.VersionId, row.Name,
                new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)), def));
        }
        return result;
    }
}
