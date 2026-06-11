using ShelfGuard.Infrastructure.AI;
using Xunit;

namespace ShelfGuard.Tests.AiOrders;

public sealed class ClaudeAdviceParserTests
{
    private static string Item(string productId, decimal qty, string confidence = "high") =>
        "{\"product_id\":\"" + productId + "\",\"quantity_suggested\":" + qty +
        ",\"reasoning\":\"тест\",\"confidence\":\"" + confidence +
        "\",\"factors\":{\"weather\":1.8,\"event\":1.0,\"promo\":1.0}}";

    [Fact]
    public void Parses_valid_advice_items()
    {
        var id = Guid.NewGuid();
        var json = "{\"items\":[" + Item(id.ToString(), 144) + "]}";

        var items = ClaudeOrderAdvisor.ParseAdvice(json);

        Assert.Single(items);
        Assert.Equal(id, items[0].ProductId);
        Assert.Equal(144m, items[0].QuantitySuggested);
        Assert.Equal("high", items[0].Confidence);
        Assert.Contains("\"weather\":1.8", items[0].FactorsJson.Replace(" ", ""));
    }

    [Fact]
    public void Skips_hallucinated_product_ids()
    {
        var good = Guid.NewGuid();
        var json = "{\"items\":[" + Item("not-a-uuid", 10, "low") + "," + Item(good.ToString(), 20, "medium") + "]}";

        var items = ClaudeOrderAdvisor.ParseAdvice(json);

        Assert.Single(items);
        Assert.Equal(good, items[0].ProductId);
        Assert.Equal(20m, items[0].QuantitySuggested);
    }

    [Fact]
    public void Empty_items_array_is_fine()
    {
        Assert.Empty(ClaudeOrderAdvisor.ParseAdvice("{\"items\":[]}"));
    }
}
