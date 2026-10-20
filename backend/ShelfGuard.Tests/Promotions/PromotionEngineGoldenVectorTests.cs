using System.Text.Json;
using ShelfGuard.Application.Features.Promotions;
using Xunit;

namespace ShelfGuard.Tests.Promotions;

/// <summary>
/// Runs every vector of golden-vectors.json (shared with the cashier's TypeScript engine) against
/// the C# <see cref="PromotionEngine"/>.
/// </summary>
public sealed class PromotionEngineGoldenVectorTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed class VectorFile
    {
        public string Schema { get; set; } = string.Empty;
        public List<Vector> Vectors { get; set; } = [];
    }

    public sealed class Vector
    {
        public string Name { get; set; } = string.Empty;
        public VectorContext Context { get; set; } = new();
        public List<VectorRule> Rules { get; set; } = [];
        public List<VectorLine> Lines { get; set; } = [];
        public List<List<VectorSegment>> Expected { get; set; } = [];
        public override string ToString() => Name;
    }

    public sealed class VectorContext
    {
        public Guid StoreId { get; set; }
        public DateTimeOffset At { get; set; }
        public int UtcOffsetMinutes { get; set; }
        public string? StoreTimeZone { get; set; }
        public VectorMembership? Membership { get; set; }
    }

    public sealed class VectorMembership
    {
        public Guid Id { get; set; }
        public Guid? TierId { get; set; }
        public decimal TierPercent { get; set; }
    }

    public sealed class VectorRule
    {
        public Guid Id { get; set; }
        public Guid VersionId { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public JsonElement Definition { get; set; }
    }

    public sealed class VectorLine
    {
        public Guid ItemId { get; set; }
        public Guid? CategoryId { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Quantity { get; set; }
        public bool HasStockDiscount { get; set; }
        public decimal? RegularPrice { get; set; }
    }

    public sealed class VectorSegment
    {
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public Guid? RuleId { get; set; }
        public string? Label { get; set; }
    }

    private static VectorFile Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Promotions", "golden-vectors.json");
        return JsonSerializer.Deserialize<VectorFile>(File.ReadAllText(path), Json)!;
    }

    public static IEnumerable<object[]> VectorNames() => Load().Vectors.Select(v => new object[] { v.Name });

    [Fact]
    public void File_has_schema_and_unique_vector_names()
    {
        var file = Load();
        Assert.Equal("promotion-engine-golden-vectors/v1", file.Schema);
        Assert.True(file.Vectors.Count >= 30);
        Assert.Equal(file.Vectors.Count, file.Vectors.Select(v => v.Name).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void Vector_matches(string name)
    {
        var v = Load().Vectors.Single(x => x.Name == name);

        var rules = v.Rules.Select(r => new PromotionRuleInput(
            r.Id, r.VersionId, r.Name, r.CreatedAt,
            PromotionDefinitionJson.TryParse(r.Definition.GetRawText())
                ?? throw new InvalidOperationException($"{name}: definition did not parse"))).ToList();
        var lines = v.Lines.Select(l => new PromotionLineInput(
            l.ItemId, l.CategoryId, l.UnitPrice, l.Quantity, l.HasStockDiscount, l.RegularPrice)).ToList();
        var ctx = new PromotionContext(v.Context.StoreId, v.Context.At, v.Context.UtcOffsetMinutes,
            v.Context.Membership is { } m ? new PromotionMembership(m.Id, m.TierId, m.TierPercent) : null,
            v.Context.StoreTimeZone);

        var result = PromotionEngine.Evaluate(lines, rules, ctx);

        Assert.Equal(v.Expected.Count, result.Lines.Count);
        for (var i = 0; i < v.Expected.Count; i++)
        {
            var expected = v.Expected[i].Select(s => $"{s.Quantity:0.####} @ {s.UnitPrice:0.00} [{s.RuleId}|{s.Label}]").ToList();
            var actual = result.Lines[i].Segments.Select(s => $"{s.Quantity:0.####} @ {s.UnitPrice:0.00} [{s.RuleId}|{s.Label}]").ToList();
            Assert.True(expected.SequenceEqual(actual),
                $"{name}, line {i}:\n expected {string.Join("; ", expected)}\n actual   {string.Join("; ", actual)}");
            Assert.Equal(v.Lines[i].Quantity, result.Lines[i].Segments.Sum(s => s.Quantity));
        }
    }

    [Fact]
    public void No_rules_and_no_tier_returns_one_regular_segment_per_line()
    {
        var a = Guid.NewGuid();
        var result = PromotionEngine.Evaluate(
            [new PromotionLineInput(a, null, 12.34m, 3m)], [],
            new PromotionContext(Guid.NewGuid(), DateTimeOffset.UtcNow));
        var seg = Assert.Single(Assert.Single(result.Lines).Segments);
        Assert.Equal(3m, seg.Quantity);
        Assert.Equal(12.34m, seg.UnitPrice);
        Assert.Null(seg.RuleId);
    }

    [Fact]
    public void Definition_json_roundtrips_camelCase()
    {
        const string json = """
            {"type":"buy_n_get_m","targets":{"itemIds":["00000000-0000-0000-0000-00000000000a"],"categoryIds":[]},
             "scope":{"storeIds":[],"validFrom":"2026-10-20T00:00:00Z","validTo":null,"weekdays":[1,7],"timeFrom":"09:00","timeTo":null},
             "audience":{"kind":"loyalty_tier","tierIds":["00000000-0000-0000-0000-000000000071"]},
             "params":{"buyQty":2,"getQty":1,"getPrice":0.01,"matchMode":"across_set"},
             "limits":{"maxApplicationsPerReceipt":3},"excludeAlreadyDiscounted":true,"priority":7}
            """;
        var def = PromotionDefinitionJson.TryParse(json)!;
        Assert.True(PromotionEngine.IsValid(def));
        Assert.Equal(PromotionRuleTypes.BuyNGetM, def.Type);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero), def.Scope.ValidFrom);
        Assert.Equal([1, 7], def.Scope.Weekdays);
        Assert.Equal(0.01m, def.Params.GetPrice);
        Assert.Equal(PromotionMatchModes.AcrossSet, def.Params.MatchMode);
        Assert.Equal(3, def.Limits.MaxApplicationsPerReceipt);
        Assert.True(def.ExcludeAlreadyDiscounted);
        Assert.Equal(7, def.Priority);

        var back = PromotionDefinitionJson.Serialize(def);
        Assert.Contains("\"excludeAlreadyDiscounted\":true", back);
        Assert.Contains("\"maxApplicationsPerReceipt\":3", back);
        Assert.Null(PromotionDefinitionJson.TryParse("{not json"));
    }

    [Theory]
    [InlineData("""{"type":"percent_off","targets":{},"params":{"percent":10}}""")]                      // no targets
    [InlineData("""{"type":"percent_off","targets":{"itemIds":["00000000-0000-0000-0000-00000000000a"]},"params":{}}""")] // no param
    [InlineData("""{"type":"fixed_price","targets":{"itemIds":["00000000-0000-0000-0000-00000000000a"]},"params":{"price":0}}""")]
    [InlineData("""{"type":"buy_n_get_m","targets":{"itemIds":["00000000-0000-0000-0000-00000000000a"]},"params":{"buyQty":0,"getQty":1,"getPrice":1}}""")]
    [InlineData("""{"type":"buy_n_get_m","targets":{"itemIds":["00000000-0000-0000-0000-00000000000a"]},"params":{"buyQty":2,"getQty":1}}""")]
    [InlineData("""{"type":"percent_off","targets":{"itemIds":["00000000-0000-0000-0000-00000000000a"]},"params":{"percent":10},"scope":{"timeFrom":"25:00"}}""")]
    [InlineData("""{"type":"unknown","targets":{"itemIds":["00000000-0000-0000-0000-00000000000a"]},"params":{"percent":10}}""")]
    public void Invalid_definitions_are_rejected(string json) =>
        Assert.False(PromotionEngine.IsValid(PromotionDefinitionJson.TryParse(json)));
}
