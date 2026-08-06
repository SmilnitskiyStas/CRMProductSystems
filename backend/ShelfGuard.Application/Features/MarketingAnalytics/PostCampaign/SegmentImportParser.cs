using ShelfGuard.Application.Common;

namespace ShelfGuard.Application.Features.MarketingAnalytics.PostCampaign;

public enum ImportTokenKind { Guid, Phone }

/// <summary>One token that passed strict validation, after dedup. <see cref="RawText"/> is the
/// original (trimmed) text as submitted — kept for the unknown-tokens sample so a marketer can
/// recognize what they pasted, rather than a normalized form they never typed.</summary>
public sealed record ParsedToken(ImportTokenKind Kind, string RawText, Guid? CustomerId, string? NormalizedPhone);

/// <summary><see cref="UploadedCount"/> counts every non-blank raw token attempted (pre-dedup,
/// pre-validation). The identity <c>UploadedCount == InvalidCount + DuplicateCount + (unique
/// valid tokens count)</c> always holds — see <see cref="SegmentImportParserTests"/>. Entity-level
/// duplicates (two different valid tokens resolving to the SAME Customer row after DB lookup,
/// e.g. a GUID and that same customer's phone both submitted) are NOT reflected in
/// <see cref="DuplicateCount"/> here — that can only be detected after the bulk customer lookup,
/// so <see cref="PostCampaignService"/> adds to this count itself once resolution completes.</summary>
public sealed record SegmentImportParseResult(
    int UploadedCount,
    int DuplicateCount,
    int InvalidCount,
    IReadOnlyList<string> InvalidTokensSample,
    IReadOnlyList<ParsedToken> UniqueValidTokens);

/// <summary>Result of parsing a tabular (CSV/XLSX) upload — <see cref="PreviewRows"/> always
/// includes every column (not just the detected one) so the frontend can render a column-picker
/// table, per the brief's §6.3 "покажи preview, дай підтвердити/змінити колонку" requirement.</summary>
public sealed record DelimitedRowsParseResult(
    IReadOnlyList<string> Headers,
    int DetectedColumnIndex,
    string? DetectedColumnHeader,
    IReadOnlyList<IReadOnlyList<string?>> PreviewRows,
    SegmentImportParseResult Tokens);

/// <summary>
/// Strict, pure token parser (TASK-472, `docs/uployal/AUDIENCE_ANALYSIS.md` §5.3/§5.4/§31.2/§38 —
/// the single most important correctness requirement in the source doc). The competitor's own
/// parser is documented as broken: it extracts digit RUNS from anywhere in arbitrary text,
/// splitting UUIDs into pieces and treating a decimal as two IDs. This class never extracts
/// substrings from a token — each WHOLE token is classified as exactly one of Guid / Phone /
/// Invalid, never partially matched.
///
/// No DB/EF/HTTP dependencies (only <see cref="PhoneNormalizer"/>, a pure string function) —
/// customer resolution against the tenant's real Customer rows happens one layer up, in
/// <see cref="PostCampaignService"/>, via the repository's bulk lookup.
///
/// TASK-474/477: <see cref="ParseTextList"/>/<see cref="ParseCsvText"/> both throw
/// <see cref="ImportTooLargeException"/> before materializing an oversized input — defense in
/// depth alongside the XLSX path's identical guard in <see cref="IExcelImportService"/>, even
/// though this text/CSV path is already bounded by the controller's 10 MB request-size cap.
/// </summary>
public static class SegmentImportParser
{
    /// <summary>
    /// TASK-478 fix (bug writeup: bug-task476-unknown-tokens-export-capped-at-20): was 20, which
    /// silently discarded any invalid token beyond the first 20 at import time — this sample is
    /// the ONLY place these are ever stored, so the downloadable error-report export inherited
    /// the same silent cap with no truncation signal (source doc §8.2's "full error report"
    /// requirement broken by a hidden 20-row ceiling). Bumped by an order of magnitude: still a
    /// trivially cheap JSONB <c>List&lt;string&gt;</c> column even at the import's own 20,000-row
    /// ceiling (<see cref="PostCampaignService.MaxAcceptedRows"/>), and comfortably covers the
    /// overwhelming majority of realistic real-world invalid-token counts without truncating at
    /// all. A segment CAN still exceed this (e.g. a 20,000-row all-garbage upload) — that
    /// residual case is handled by <see cref="PostCampaignService.ExportUnknownTokensAsync"/>'s
    /// own Truncated/TotalInvalidCount signal on the export, not by raising this indefinitely.
    /// Kept at the same value as <c>PostCampaignService.ImportAsync</c>'s own unknown-sample cap
    /// by convention (not a shared constant — the two cap conceptually different things: invalid-
    /// FORMAT tokens here vs well-formed-but-unmatched tokens there).
    /// </summary>
    private const int InvalidSampleCap = 500;
    public const int PreviewRowCap = 10;

    /// <summary>Case-insensitive header names tried in order (brief §6.3) — first column whose
    /// header matches any of these wins; otherwise falls back to the first column.</summary>
    private static readonly string[] KnownIdHeaderNames =
        ["id", "customer_id", "customerid", "guid", "phone", "телефон", "номер"];

    // ── Mode (a): raw pasted text — one ID per line, or several per line separated by commas ──

    public static SegmentImportParseResult ParseTextList(string? rawText)
    {
        var tokens = (rawText ?? string.Empty)
            .Replace("\r", string.Empty)
            .Split(['\n', ',']);

        // TASK-474 finding A (defense-in-depth for the CSV/text path — already bounded by the
        // controller's 10 MB request-size cap, unlike the XLSX path this finding actually targets):
        // reject BEFORE the per-token Classify() loop in ParseTokens runs, not after, so an
        // oversized paste fails with the same clear "too many" error shape the XLSX path now has,
        // rather than doing the full classify/dedup pass first.
        if (tokens.Length > ImportLimits.MaxRows)
            throw new ImportTooLargeException(
                $"Submitted {tokens.Length:N0} entries, exceeding the {ImportLimits.MaxRows:N0} import limit.");

        return ParseTokens(tokens);
    }

    // ── Mode (b): CSV/XLSX file — auto-detect column, dedicated preview ─────────────────────────

    /// <summary>
    /// Minimal CSV split — the same plain <c>Split(',')</c>-per-line approach
    /// <c>DailySalesService.ImportCsvAsync</c> already uses elsewhere in this codebase (no
    /// quoted-comma escaping). Not a new, weaker parser introduced by this feature — matches this
    /// codebase's existing, only CSV-parsing precedent.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string?>> ParseCsvText(string text)
    {
        var lines = text
            .Replace("\r", string.Empty)
            .Split('\n')
            .Where(l => l.Length > 0)
            .ToList();

        // TASK-474 finding A (defense-in-depth — see ParseTextList's identical comment): reject
        // BEFORE the per-line Split(',')/Trim() materialization below runs.
        if (lines.Count > ImportLimits.MaxRows)
            throw new ImportTooLargeException(
                $"Submitted {lines.Count:N0} rows, exceeding the {ImportLimits.MaxRows:N0} import limit.");

        return lines
            .Select(l => (IReadOnlyList<string?>)l.Split(',').Select(c => (string?)c.Trim()).ToList())
            .ToList();
    }

    /// <summary>
    /// Row 0 is ALWAYS treated as a header row and never becomes a token — even when it matches
    /// none of <see cref="KnownIdHeaderNames"/> (a genuine header cell like "ID спожив." would
    /// also fail that lookup; there is no way to reliably tell "unrecognized header" from
    /// "headerless file whose first data row happens to look unlike a token" from content alone,
    /// so this is a deliberate, documented v1 product decision — see TASK-472's task log — rather
    /// than the source doc, which does not reveal the competitor's exact rule here (§37)).
    /// <paramref name="explicitColumnIndex"/> lets a caller override auto-detection on a
    /// resubmit (the brief's "single call that auto-picks... columnIndex override for a second
    /// confirmed call" option).
    /// </summary>
    public static DelimitedRowsParseResult ParseDelimitedRows(
        IReadOnlyList<IReadOnlyList<string?>> rows, int? explicitColumnIndex)
    {
        if (rows.Count == 0)
            return new DelimitedRowsParseResult([], 0, null, [], ParseTokens([]));

        var headers = rows[0].Select(h => h ?? string.Empty).ToList();
        var dataRows = rows.Skip(1).ToList();

        int columnIndex;
        if (explicitColumnIndex is { } explicitIdx && explicitIdx >= 0 && explicitIdx < headers.Count)
            columnIndex = explicitIdx;
        else
            columnIndex = DetectColumnIndex(headers);

        var detectedHeader = columnIndex < headers.Count ? headers[columnIndex] : null;

        var tokenColumn = dataRows.Select(r => columnIndex < r.Count ? r[columnIndex] ?? string.Empty : string.Empty);
        var tokens = ParseTokens(tokenColumn);

        var preview = dataRows.Take(PreviewRowCap).ToList();

        return new DelimitedRowsParseResult(headers, columnIndex, detectedHeader, preview, tokens);
    }

    private static int DetectColumnIndex(IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var normalized = headers[i].Trim().ToLowerInvariant();
            if (KnownIdHeaderNames.Contains(normalized))
                return i;
        }
        return 0; // fallback: first column (brief §6.3: "якщо жоден не збігається... перша колонка")
    }

    // ── Shared token pipeline ────────────────────────────────────────────────────────────────────

    private static SegmentImportParseResult ParseTokens(IEnumerable<string> rawTokens)
    {
        var uploadedCount = 0;
        var invalidCount = 0;
        var duplicateCount = 0;
        var invalidSample = new List<string>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var uniqueValid = new List<ParsedToken>();

        foreach (var raw in rawTokens)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0) continue;
            uploadedCount++;

            var kind = Classify(trimmed, out var guid, out var normalizedPhone);
            if (kind is null)
            {
                invalidCount++;
                if (invalidSample.Count < InvalidSampleCap) invalidSample.Add(trimmed);
                continue;
            }

            var key = kind == ImportTokenKind.Guid ? "G:" + guid.ToString("D") : "P:" + normalizedPhone;
            if (!seenKeys.Add(key))
            {
                duplicateCount++;
                continue;
            }

            uniqueValid.Add(kind == ImportTokenKind.Guid
                ? new ParsedToken(ImportTokenKind.Guid, trimmed, guid, null)
                : new ParsedToken(ImportTokenKind.Phone, trimmed, null, normalizedPhone));
        }

        return new SegmentImportParseResult(uploadedCount, duplicateCount, invalidCount, invalidSample, uniqueValid);
    }

    /// <summary>
    /// Classifies ONE whole, already-trimmed token. Never extracts a substring — a token either
    /// IS a full GUID, or IS a plausible phone number (checked by character-class shape BEFORE
    /// ever calling <see cref="PhoneNormalizer.Normalize"/>, since that method strips ALL
    /// non-digit characters internally and would otherwise silently resurrect exactly the "digits
    /// pulled from arbitrary text" bug this parser exists to avoid — e.g. feeding it
    /// "invoice-0501234567-note" unfiltered would incorrectly normalize to a valid-looking phone).
    /// Everything else — decimals ("12345.67", rejected by the '.' character), free text, partial
    /// UUIDs, negative numbers that don't resolve to a real phone shape — is Invalid.
    /// </summary>
    private static ImportTokenKind? Classify(string trimmed, out Guid guid, out string? normalizedPhone)
    {
        guid = Guid.Empty;
        normalizedPhone = null;

        // Full, standard-format GUID only (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx) — matches how
        // every Customer.Id is rendered elsewhere in this codebase. Guid.TryParseExact never
        // partially matches: an incomplete/malformed UUID fragment simply fails, it is never
        // "split into pieces" the way the competitor's digit-extraction bug does.
        if (Guid.TryParseExact(trimmed, "D", out guid))
            return ImportTokenKind.Guid;

        var looksLikePhoneShape = trimmed.Length > 0 &&
            trimmed.All(c => char.IsDigit(c) || c is '+' or ' ' or '-' or '(' or ')');

        if (looksLikePhoneShape)
        {
            var normalized = PhoneNormalizer.Normalize(trimmed);
            if (normalized is not null)
            {
                normalizedPhone = normalized;
                return ImportTokenKind.Phone;
            }
        }

        return null;
    }
}
