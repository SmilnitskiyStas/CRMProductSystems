using ShelfGuard.Application.Features.Leads;
using Xunit;

namespace ShelfGuard.Tests.Leads;

public sealed class LeadCriticalityEvaluatorTests
{
    // ── (a) network: company AND network hint in message ────────────────────

    [Theory]
    [InlineData("Ми мережа салонів")]
    [InlineData("МЕРЕЖІ з 5 точок")]
    [InlineData("our network of salons")]
    [InlineData("є кілька закладів у місті")]
    [InlineData("відкрили другий філіал")]
    [InlineData("маємо 2 салони")]
    [InlineData("3 заклади у Києві")]
    [InlineData("12 філіалів")]
    public void Network_CompanyAndHint_IsCritical(string message) =>
        Assert.Equal("network", LeadCriticalityEvaluator.Evaluate("Beauty Co", message, false));

    [Theory]
    [InlineData("Ми мережа салонів")]
    [InlineData("3 заклади")]
    public void Network_WithoutCompany_NotCritical(string message)
    {
        Assert.Null(LeadCriticalityEvaluator.Evaluate(null, message, false));
        Assert.Null(LeadCriticalityEvaluator.Evaluate("  ", message, false));
    }

    [Theory]
    [InlineData("1 салон")]
    [InlineData("0 закладів")]
    [InlineData("один салон")]
    [InlineData("Цікавить демо")]
    [InlineData("салон 3")]
    [InlineData("тел 380671234567 салон")] // number not directly before the word / not a count
    public void Network_NoHintOrCountBelowTwo_NotCritical(string message) =>
        Assert.Null(LeadCriticalityEvaluator.Evaluate("Beauty Co", message, false));

    [Fact]
    public void Network_CountBoundary_TwoIsCritical_OneIsNot()
    {
        Assert.NotNull(LeadCriticalityEvaluator.Evaluate("Co", "2 салони", false));
        Assert.Null(LeadCriticalityEvaluator.Evaluate("Co", "1 салон", false));
    }

    // ── (b) urgent ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Потрібно терміново")]
    [InlineData("URGENT please")]
    [InlineData("asap")]
    [InlineData("Зателефонуйте СЬОГОДНІ")]
    public void Urgent_Keyword_IsCriticalEvenWithoutCompany(string message) =>
        Assert.Equal("urgent", LeadCriticalityEvaluator.Evaluate(null, message, false));

    [Fact]
    public void Urgent_NoKeyword_NotCritical() =>
        Assert.Null(LeadCriticalityEvaluator.Evaluate(null, "Завтра, будь ласка", false));

    // ── (c) repeat ──────────────────────────────────────────────────────────

    [Fact]
    public void Repeat_IsCritical() =>
        Assert.Equal("repeat", LeadCriticalityEvaluator.Evaluate(null, null, true));

    [Fact]
    public void IsRepeat_NormalizesPhoneToDigits()
    {
        Assert.True(LeadCriticalityEvaluator.IsRepeat("+38 (067) 123-45-67", ["380671234567"]));
        Assert.False(LeadCriticalityEvaluator.IsRepeat("+380671234567", ["380671234568"]));
    }

    [Fact]
    public void IsRepeat_NoDigits_NeverMatches() =>
        Assert.False(LeadCriticalityEvaluator.IsRepeat("-----", ["-----"]));

    [Fact]
    public void RepeatWindow_IsSevenDays() =>
        Assert.Equal(TimeSpan.FromDays(7), LeadCriticalityEvaluator.RepeatWindow);

    // ── combination / ordinary ──────────────────────────────────────────────

    [Fact]
    public void Combined_ReasonsJoined_FitsColumn()
    {
        var reason = LeadCriticalityEvaluator.Evaluate("Co", "мережа, терміново", true);
        Assert.Equal("network+urgent+repeat", reason);
        Assert.True(reason!.Length <= 50);
    }

    [Fact]
    public void Ordinary_IsNotCritical() =>
        Assert.Null(LeadCriticalityEvaluator.Evaluate("Co", "Цікавить демо", false));
}
