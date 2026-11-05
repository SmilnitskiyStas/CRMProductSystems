using System.Globalization;

namespace ShelfGuard.Application.Features.Production;

/// <summary>
/// Pure need calculation for own-production items (stage 3). Same shape as the purchase-order
/// formula (OrderFormula): the deficit against the CDA buffer target is scaled by the demand
/// multiplier (event x weather x promo), so promo/event uplift reuses the existing coefficients.
///   target  = BufferTotal + SafetyBuffer
///   deficit = target - onHand(non-expired) - inTransit - openRequests
///   raw     = deficit x (event x weather x promo)        (only when deficit &gt; 0)
///   need    = ceil(raw / RoundingStep) x RoundingStep, or 0 when raw &lt; MinNeedQty
/// </summary>
public static class ProductionDemandCalculator
{
    /// <summary>Needs below this quantity are noise and produce no line.</summary>
    public const decimal MinNeedQty = 1m;

    /// <summary>Requested quantities are rounded UP to whole units.</summary>
    public const decimal RoundingStep = 1m;

    /// <summary>Default NeededBy = today + this many days (production + delivery lead time).</summary>
    public const int DefaultLeadDays = 2;

    public sealed record Input(
        decimal BufferTotal, decimal SafetyBuffer,
        decimal OnHandNonExpired, decimal InTransit, decimal OpenRequestQty,
        decimal EventCoefficient = 1m, decimal WeatherCoefficient = 1m, decimal PromoCoefficient = 1m);

    public sealed record Result(
        decimal Need, decimal Target, decimal Deficit, decimal Multiplier, bool PromoDriven, string Reason);

    public static Result Compute(Input i)
    {
        var target = i.BufferTotal + i.SafetyBuffer;
        var deficit = target - i.OnHandNonExpired - i.InTransit - i.OpenRequestQty;
        var multiplier = Math.Round(i.EventCoefficient * i.WeatherCoefficient * i.PromoCoefficient, 2);
        // Promo/event uplift only: weather alone is not a promo, it just scales the buffer need.
        var promoDriven = Math.Round(i.EventCoefficient * i.PromoCoefficient, 2) > 1m;

        var need = 0m;
        if (deficit > 0)
        {
            var raw = multiplier == 1m ? deficit : Math.Round(deficit * multiplier, 2);
            if (raw >= MinNeedQty)
                need = Math.Ceiling(raw / RoundingStep) * RoundingStep;
        }

        return new Result(need, target, deficit, multiplier, promoDriven, BuildReason(i, target, need, multiplier, promoDriven));
    }

    private static string BuildReason(Input i, decimal target, decimal need, decimal multiplier, bool promo)
    {
        var s = $"{(promo ? "Акція/подія" : "Буфер")}: ціль {F(target)}, залишок {F(i.OnHandNonExpired)}, " +
                $"у дорозі {F(i.InTransit)}, у відкритих заявках {F(i.OpenRequestQty)}";
        if (multiplier != 1m) s += $", коефіцієнт попиту ×{F(multiplier)}";
        return s + $" → потреба {F(need)}";
    }

    private static string F(decimal d) => d.ToString("0.###", CultureInfo.InvariantCulture);
}
