namespace ShelfGuard.Application.Features.MarketingAnalytics;

/// <summary>
/// Shared PII masking for every export under <c>Features/MarketingAnalytics/</c> (Фаза 1 RFM +
/// Фаза 2 price segments). TASK-420: moved here verbatim from
/// <see cref="MarketingAnalyticsService"/> (design doc §0 explicitly calls this a safe, behavior-
/// preserving move, not a rewrite) so <c>PriceSegments.PriceSegmentsService</c> can reuse the
/// exact same masking rules instead of a second, potentially-drifting copy. No logic changed —
/// same inputs still produce byte-for-byte the same masked output as before the move.
/// </summary>
internal static class PiiMasking
{
    /// <summary>Keeps country code + operator prefix + last 2 digits visible, e.g.
    /// "+380 XX *** ** 67" (brief's exact masking example) for a normalized 12-digit
    /// "380XXXXXXXXX" number. Falls back to a generic last-4-visible mask for any other shape —
    /// Customer.Phone (unlike ConsumerAccount.Phone) is free-form CRM input, not guaranteed to
    /// be PhoneNormalizer-shaped.</summary>
    public static string? MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return phone;

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 12 && digits.StartsWith("380", StringComparison.Ordinal))
        {
            var operatorCode = digits.Substring(3, 2);
            var lastTwo = digits.Substring(10, 2);
            return $"+380 {operatorCode} *** ** {lastTwo}";
        }

        return phone.Length <= 4 ? phone : new string('*', phone.Length - 4) + phone[^4..];
    }

    /// <summary>
    /// TASK-414 (security review TASK-412, finding C): email was previously written to every
    /// export unconditionally, regardless of <c>unmaskPii</c>/capability — inconsistent with the
    /// "PII masked by default" design this same method already applies to Phone. Keeps the first
    /// character of the local part and the full domain visible (e.g. "i***@gmail.com" for
    /// "ivan@gmail.com") — enough for a marketer to eyeball a pattern (which domain, roughly
    /// whose address) without exposing the actual contactable address. Uses a fixed-length mask
    /// (not proportional to local-part length like <see cref="MaskPhone"/>'s fallback branch) so
    /// the mask itself doesn't leak how long the local part is. Falls back to masking everything
    /// but the last 2 characters for any string with no '@' at all (shouldn't happen for a real
    /// email column, but mirrors MaskPhone's own defensive fallback for non-conforming input).
    /// </summary>
    public static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return email;

        var at = email.IndexOf('@');
        if (at <= 0)
            return email.Length <= 2 ? email : new string('*', email.Length - 2) + email[^2..];

        var domain = email[at..];
        return $"{email[..1]}***{domain}";
    }
}
