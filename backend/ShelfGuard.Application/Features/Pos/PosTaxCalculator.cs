using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.Pos;

public static class PosTaxCalculator
{
    /// <summary>Allocate paid cents across lines before computing their inclusive VAT; old rows stay untouched.</summary>
    public static decimal Apply(ICollection<PosTransactionItem> items, decimal paidTotal)
    {
        var lines = items.ToList();
        var gross = lines.Select(i => i.LineTotal ?? Math.Round(i.PriceFinal * i.Quantity, 2, MidpointRounding.AwayFromZero)).ToArray();
        var total = gross.Sum();
        if (paidTotal < 0 || paidTotal > total || Math.Round(paidTotal, 2) != paidTotal)
            throw new InvalidOperationException("Paid total is outside the receipt amount.");
        var paidCents = decimal.ToInt64(Math.Round(paidTotal * 100m, 0, MidpointRounding.AwayFromZero));
        var shares = gross.Select(g => total == 0 ? 0 : g / total * paidCents).ToArray();
        var cents = shares.Select(s => decimal.ToInt64(decimal.Floor(s))).ToArray();
        var remainder = paidCents - cents.Sum();
        foreach (var index in Enumerable.Range(0, lines.Count)
                     .OrderByDescending(i => shares[i] - cents[i]).ThenBy(i => i).Take((int)remainder))
            cents[index]++;
        for (var i = 0; i < lines.Count; i++)
        {
            var rate = lines[i].VatRate ?? throw new InvalidOperationException("New sale lines require a VAT snapshot.");
            if (rate is < 0 or > 100) throw new InvalidOperationException("Invalid VAT snapshot.");
            lines[i].TaxableAmount = cents[i] / 100m;
            lines[i].VatAmount = Math.Round(lines[i].TaxableAmount!.Value * rate / (100m + rate),
                2, MidpointRounding.AwayFromZero);
        }
        return lines.Sum(i => i.VatAmount ?? 0m);
    }
}
