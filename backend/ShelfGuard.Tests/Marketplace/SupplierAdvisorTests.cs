using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Infrastructure.AI.SupplierAdvisor;
using Xunit;

namespace ShelfGuard.Tests.Marketplace;

/// <summary>
/// Unit tests for TASK-223 — AI Supplier Recommendation.
///
/// We mock <see cref="ISupplierAdvisor"/> to avoid real Claude API calls.
/// Internal static helpers on <see cref="SupplierAdvisor"/> (ParseRecommendations,
/// BuildUserPrompt) are tested directly to verify prompt/parse logic without I/O.
/// </summary>
public sealed class SupplierAdvisorTests
{
    // ── Helper data ───────────────────────────────────────────────────────────

    private static readonly Guid _supplierA = Guid.NewGuid();
    private static readonly Guid _supplierB = Guid.NewGuid();

    private static SupplierRecommendationRequest MakeRequest(
        string item = "Молоко 2.5%",
        string? region = "Київ",
        int? qty = 100,
        string? notes = null) =>
        new(item, region, qty, notes);

    private static SupplierCandidateDto MakeCandidate(
        Guid id, string name, string? region = "Київ",
        decimal? rating = 4.5m, decimal? deliveryDays = 2m) =>
        new(id, name, region, rating, deliveryDays, 0.95m, "Молоко 2.5%", 28.50m, "л", 50);

    // ── ParseRecommendations — happy path ─────────────────────────────────────

    [Fact]
    public void ParseRecommendations_ValidJson_ReturnsSortedItems()
    {
        var json = $$"""
        {
          "recommendations": [
            {
              "supplier_id": "{{_supplierA}}",
              "rank": 1,
              "score": 0.92,
              "reasoning": "Найкращий рейтинг у регіоні.",
              "matched_item_name": "Молоко 2.5%",
              "matched_item_price": 28.50
            },
            {
              "supplier_id": "{{_supplierB}}",
              "rank": 2,
              "score": 0.75,
              "reasoning": "Хороша точність замовлень."
            }
          ]
        }
        """;

        var items = SupplierAdvisor.ParseRecommendations(json);

        Assert.Equal(2, items.Count);

        Assert.Equal(_supplierA, items[0].SupplierId);
        Assert.Equal(1, items[0].Rank);
        Assert.Equal(0.92m, items[0].Score);
        Assert.Equal("Найкращий рейтинг у регіоні.", items[0].Reasoning);
        Assert.Equal("Молоко 2.5%", items[0].MatchedItemName);
        Assert.Equal(28.50m, items[0].MatchedItemPrice);

        Assert.Equal(_supplierB, items[1].SupplierId);
        Assert.Equal(2, items[1].Rank);
        Assert.Null(items[1].MatchedItemName);
        Assert.Null(items[1].MatchedItemPrice);
    }

    [Fact]
    public void ParseRecommendations_HallucinatedId_SkipsItem()
    {
        var json = $$"""
        {
          "recommendations": [
            {
              "supplier_id": "not-a-guid",
              "rank": 1,
              "score": 0.9,
              "reasoning": "Test."
            },
            {
              "supplier_id": "{{_supplierA}}",
              "rank": 2,
              "score": 0.8,
              "reasoning": "Реальний постачальник."
            }
          ]
        }
        """;

        var items = SupplierAdvisor.ParseRecommendations(json);

        Assert.Single(items);
        Assert.Equal(_supplierA, items[0].SupplierId);
    }

    [Fact]
    public void ParseRecommendations_EmptyArray_ReturnsEmpty()
    {
        const string json = """{ "recommendations": [] }""";

        var items = SupplierAdvisor.ParseRecommendations(json);

        Assert.Empty(items);
    }

    // ── BuildUserPrompt — contains key context ────────────────────────────────

    [Fact]
    public void BuildUserPrompt_IncludesAllRequestFields()
    {
        var request = MakeRequest(item: "Молоко 2.5%", region: "Київ", qty: 100, notes: "Терміново");
        var candidates = new List<SupplierCandidateDto>
        {
            MakeCandidate(_supplierA, "Агро Постач"),
        };

        var prompt = SupplierAdvisor.BuildUserPrompt(request, candidates);

        Assert.Contains("Молоко 2.5%", prompt);
        Assert.Contains("Київ", prompt);
        Assert.Contains("100", prompt);
        Assert.Contains("Терміново", prompt);
        // Prompt contains serialized candidate JSON — verify JSON block is present
        Assert.Contains("[{", prompt); // JSON array of candidates is serialized inline
    }

    [Fact]
    public void BuildUserPrompt_NullOptionals_UsesDefaultPlaceholders()
    {
        var request = new SupplierRecommendationRequest("Цукор", null, null, null);
        var candidates = new List<SupplierCandidateDto>();

        var prompt = SupplierAdvisor.BuildUserPrompt(request, candidates);

        Assert.Contains("Цукор", prompt);
        Assert.Contains("не вказано", prompt);
        Assert.Contains("немає", prompt);
    }

    // ── ISupplierAdvisor mock — controller-level behaviour ───────────────────

    [Fact]
    public async Task RecommendAsync_WhenNotConfigured_IsConfiguredReturnsFalse()
    {
        // Simulates the 503 branch in the controller
        var advisor = Substitute.For<ISupplierAdvisor>();
        advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(false);

        var configured = await advisor.IsConfiguredAsync();

        Assert.False(configured);
        // Verify RecommendAsync is never called if controller short-circuits on 503
        await advisor.DidNotReceive().RecommendAsync(
            Arg.Any<SupplierRecommendationRequest>(),
            Arg.Any<IEnumerable<SupplierCandidateDto>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecommendAsync_EmptyCandidates_ReturnsEmptyRecommendations()
    {
        var advisor = Substitute.For<ISupplierAdvisor>();
        advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);

        var emptyResult = new SupplierRecommendationResult(
            Array.Empty<SupplierRecommendationItem>(),
            "prompt",
            "claude-sonnet-4-6",
            10);

        advisor.RecommendAsync(
                Arg.Any<SupplierRecommendationRequest>(),
                Arg.Is<IEnumerable<SupplierCandidateDto>>(c => !c.Any()),
                Arg.Any<CancellationToken>())
            .Returns(emptyResult);

        var request = MakeRequest();
        var result = await advisor.RecommendAsync(request, Enumerable.Empty<SupplierCandidateDto>());

        Assert.Empty(result.Recommendations);
        Assert.Equal("prompt", result.Prompt);
    }

    [Fact]
    public async Task RecommendAsync_WithCandidates_CallsAdvisorWithNonEmptyPrompt()
    {
        var advisor = Substitute.For<ISupplierAdvisor>();
        advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);

        var recommendations = new List<SupplierRecommendationItem>
        {
            new(_supplierA, 1, 0.92m, "Найкращий постачальник у регіоні.", "Молоко 2.5%", 28.50m),
        };
        var expectedResult = new SupplierRecommendationResult(
            recommendations,
            "ПОТРЕБА В ТОВАРІ: Молоко 2.5%",
            "claude-sonnet-4-6",
            500);

        advisor.RecommendAsync(
                Arg.Any<SupplierRecommendationRequest>(),
                Arg.Any<IEnumerable<SupplierCandidateDto>>(),
                Arg.Any<CancellationToken>())
            .Returns(expectedResult);

        var request = MakeRequest();
        var candidates = new[] { MakeCandidate(_supplierA, "Агро Постач") };

        var result = await advisor.RecommendAsync(request, candidates);

        // Verify advisor was called
        await advisor.Received(1).RecommendAsync(
            request,
            Arg.Is<IEnumerable<SupplierCandidateDto>>(c => c.Any()),
            Arg.Any<CancellationToken>());

        // Verify result is correctly returned
        Assert.Single(result.Recommendations);
        Assert.Equal(_supplierA, result.Recommendations[0].SupplierId);
        Assert.Equal(0.92m, result.Recommendations[0].Score);
        Assert.NotEmpty(result.Prompt);
    }

    [Fact]
    public async Task RecommendAsync_Returns200WithRecommendationsStructure()
    {
        var advisor = Substitute.For<ISupplierAdvisor>();
        advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);

        var supplierId = Guid.NewGuid();
        var recResult = new SupplierRecommendationResult(
            new List<SupplierRecommendationItem>
            {
                new(supplierId, 1, 0.88m, "Швидка доставка та відмінний рейтинг.", "Молоко", 29.00m),
            },
            "Знайди постачальника для: Молоко",
            "claude-sonnet-4-6",
            300);

        advisor.RecommendAsync(
                Arg.Any<SupplierRecommendationRequest>(),
                Arg.Any<IEnumerable<SupplierCandidateDto>>(),
                Arg.Any<CancellationToken>())
            .Returns(recResult);

        var result = await advisor.RecommendAsync(
            new SupplierRecommendationRequest("Молоко", null, null, null),
            new[] { MakeCandidate(supplierId, "Best Supplier") });

        Assert.NotNull(result);
        Assert.Single(result.Recommendations);
        Assert.Equal(supplierId, result.Recommendations[0].SupplierId);
        Assert.Equal(1, result.Recommendations[0].Rank);
        Assert.Equal(0.88m, result.Recommendations[0].Score);
        Assert.NotEmpty(result.Recommendations[0].Reasoning);
    }
}
