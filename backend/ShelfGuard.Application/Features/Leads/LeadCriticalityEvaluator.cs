using System.Text.RegularExpressions;

namespace ShelfGuard.Application.Features.Leads;

/// <summary>
/// Pure rules deciding whether a lead is critical. All rules live here.
/// Critical when ANY holds: (network) company given AND message hints at a chain / several venues;
/// (urgent) message has an urgency keyword; (repeat) same phone submitted again within 7 days.
/// </summary>
public static class LeadCriticalityEvaluator
{
    public static readonly TimeSpan RepeatWindow = TimeSpan.FromDays(7);

    public const string ReasonNetwork = "network";
    public const string ReasonUrgent = "urgent";
    public const string ReasonRepeat = "repeat";

    private static readonly string[] UrgentKeywords = ["терміново", "urgent", "asap", "сьогодні"];

    private static readonly Regex NetworkWords = new(
        @"\bмереж\w*|\bnetwork\w*|кілька\s+заклад\w*|філіал\w*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // number >= 2 directly before "заклад/салон/філіал" (e.g. "3 салони", "12 закладів")
    private static readonly Regex CountedVenues = new(
        @"(?<!\d)(\d{1,6})\s*(?:заклад|салон|філіал)\w*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Digits only; used to compare phones regardless of formatting.</summary>
    public static string NormalizePhone(string? phone) =>
        string.IsNullOrEmpty(phone) ? string.Empty : new string(phone.Where(char.IsAsciiDigit).ToArray());

    public static bool IsRepeat(string phone, IEnumerable<string> recentPhones)
    {
        var digits = NormalizePhone(phone);
        return digits.Length > 0 && recentPhones.Any(p => NormalizePhone(p) == digits);
    }

    /// <returns>Reason codes joined by "+" (max 50 chars), null when the lead is not critical.</returns>
    public static string? Evaluate(string? company, string? message, bool isRepeat)
    {
        var reasons = new List<string>(3);
        if (HasNetworkSignal(company, message)) reasons.Add(ReasonNetwork);
        if (HasUrgentSignal(message)) reasons.Add(ReasonUrgent);
        if (isRepeat) reasons.Add(ReasonRepeat);
        return reasons.Count == 0 ? null : string.Join('+', reasons);
    }

    public static bool HasNetworkSignal(string? company, string? message)
    {
        if (string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(message)) return false;
        if (NetworkWords.IsMatch(message)) return true;
        foreach (Match m in CountedVenues.Matches(message))
            if (int.TryParse(m.Groups[1].Value, out var n) && n >= 2) return true;
        return false;
    }

    public static bool HasUrgentSignal(string? message) =>
        !string.IsNullOrWhiteSpace(message) &&
        UrgentKeywords.Any(k => message.Contains(k, StringComparison.OrdinalIgnoreCase));
}
