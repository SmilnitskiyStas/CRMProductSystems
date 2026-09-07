using ShelfGuard.Infrastructure.AI;
using Xunit;

namespace ShelfGuard.Tests.AiOrders;

/// <summary>
/// Managed-AI Phase 1 — the mandatory per-tenant isolation guardrail. Pure composition, no I/O
/// (the DB lookups live in <c>AiPromptResolver</c>).
/// </summary>
public sealed class AiGuardrailTests
{
    private const string Base = "Ти — AI консультант. Відповідай коротко.";

    [Fact]
    public void Prefix_names_the_business_when_known()
    {
        var p = AiGuardrail.Prefix("Свіжий Кут");

        Assert.Contains("«Свіжий Кут»", p);
        Assert.Contains("конкурент", p);
    }

    [Fact]
    public void Prefix_still_enforces_isolation_without_a_name()
    {
        foreach (var name in new[] { null, "", "   " })
        {
            var p = AiGuardrail.Prefix(name);
            Assert.Contains("цього бізнесу", p);
            Assert.Contains("конкурент", p);
            Assert.DoesNotContain("«»", p);
        }
    }

    [Fact]
    public void Compose_puts_guardrail_first_and_base_prompt_last()
    {
        var result = AiGuardrail.Compose("Свіжий Кут", null, Base);

        Assert.StartsWith(AiGuardrail.Prefix("Свіжий Кут"), result);
        Assert.EndsWith(Base, result);
    }

    [Fact]
    public void Compose_inserts_extra_instructions_between_guardrail_and_base()
    {
        const string extra = "Це аптека — акцентуй на термінах придатності.";

        var result = AiGuardrail.Compose("Аптека №5", extra, Base);

        var guardrailEnd = result.IndexOf(extra, System.StringComparison.Ordinal);
        var baseStart = result.IndexOf(Base, System.StringComparison.Ordinal);

        Assert.True(guardrailEnd > 0);
        Assert.True(baseStart > guardrailEnd, "base prompt must come after the extra instructions");
        Assert.StartsWith(AiGuardrail.Prefix("Аптека №5"), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Compose_omits_blank_extra_instructions(string? extra)
    {
        var result = AiGuardrail.Compose("Свіжий Кут", extra, Base);

        Assert.Equal($"{AiGuardrail.Prefix("Свіжий Кут")}\n\n{Base}", result);
    }

    [Fact]
    public void Compose_trims_and_keeps_base_prompt_intact()
    {
        var result = AiGuardrail.Compose("Свіжий Кут", null, "  " + Base + "  ");
        Assert.EndsWith(Base, result);
        Assert.DoesNotContain("  \n", result);
    }
}
