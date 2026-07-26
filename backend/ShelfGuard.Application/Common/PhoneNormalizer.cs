namespace ShelfGuard.Application.Common;

/// <summary>
/// Normalizes a Ukrainian mobile phone number to the canonical <c>+380XXXXXXXXX</c> form.
/// TASK-405 (Loyalty Фаза 0): used ONLY for
/// <see cref="ShelfGuard.Domain.Entities.ConsumerAccount"/>.Phone (globally unique consumer
/// identity). Deliberately separate from the existing, intentionally permissive Customer
/// phone check (CustomerService's PhoneRegex — tenant-scoped, accepts loose regional
/// formats) — that code path is untouched by this task.
/// </summary>
public static class PhoneNormalizer
{
    /// <summary>
    /// Accepts "+380XXXXXXXXX", "380XXXXXXXXX", "0XXXXXXXXX" (10-digit local form with
    /// trunk 0), or a bare 9-digit subscriber number, tolerating spaces/dashes/parens
    /// anywhere. Returns the canonical "+380XXXXXXXXX" form, or null when the input doesn't
    /// resolve to exactly a 9-digit subscriber number after the country/trunk prefix.
    /// Deliberately does not validate the operator-code digit (39/50/63/67/...) — structural
    /// shape only, consistent with this project's existing phone checks elsewhere.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var digits = new string(raw.Where(char.IsDigit).ToArray());

        if (digits.Length == 12 && digits.StartsWith("380", StringComparison.Ordinal))
            return "+" + digits;

        if (digits.Length == 10 && digits.StartsWith("0", StringComparison.Ordinal))
            return "+38" + digits;

        if (digits.Length == 9)
            return "+380" + digits;

        return null;
    }
}
