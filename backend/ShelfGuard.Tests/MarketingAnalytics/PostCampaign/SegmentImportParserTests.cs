using ShelfGuard.Application.Features.MarketingAnalytics.PostCampaign;
using Xunit;

namespace ShelfGuard.Tests.MarketingAnalytics.PostCampaign;

/// <summary>
/// TASK-472: the single most important correctness requirement in this task's brief
/// (`docs/uployal/AUDIENCE_ANALYSIS.md` §5.3/§5.4/§31.2/§38) — the competitor's own parser
/// extracts digit RUNS from anywhere in arbitrary text (splitting UUIDs, treating decimals as two
/// IDs, dropping minus signs). Every test here proves <see cref="SegmentImportParser"/> does NOT
/// replicate that: a token is either a full valid GUID, a plausible phone number, or Invalid —
/// never a partial match.
/// </summary>
public sealed class SegmentImportParserTests
{
    // ── Strict classification: valid GUID ───────────────────────────────────────────────────────

    [Fact]
    public void ParseTextList_accepts_a_full_standard_format_guid()
    {
        var guid = Guid.NewGuid();
        var result = SegmentImportParser.ParseTextList(guid.ToString("D"));

        Assert.Single(result.UniqueValidTokens);
        Assert.Equal(ImportTokenKind.Guid, result.UniqueValidTokens[0].Kind);
        Assert.Equal(guid, result.UniqueValidTokens[0].CustomerId);
        Assert.Equal(0, result.InvalidCount);
    }

    [Fact]
    public void ParseTextList_rejects_a_guid_with_a_missing_segment_never_partially_matches_it()
    {
        // A UUID-with-dashes that has been mangled (one hex group truncated) — the competitor's
        // own digit-extraction bug would still find SOME digits in here; this parser must not.
        var mangled = "a1b2c3d4-e5f6-47a8-b9c0-d1e2f3a4b5"; // last group short by 2 chars
        var result = SegmentImportParser.ParseTextList(mangled);

        Assert.Empty(result.UniqueValidTokens);
        Assert.Equal(1, result.InvalidCount);
        Assert.Contains(mangled, result.InvalidTokensSample);
    }

    [Fact]
    public void ParseTextList_treats_a_full_guid_as_ONE_token_never_split_by_its_internal_dashes()
    {
        var guid = Guid.NewGuid();
        var result = SegmentImportParser.ParseTextList(guid.ToString("D"));

        // If the parser were splitting on dashes/extracting digit runs like the competitor's
        // bug, a single GUID would explode into 5 separate numeric fragments. It must not.
        Assert.Single(result.UniqueValidTokens);
        Assert.Equal(1, result.UploadedCount);
    }

    // ── Strict classification: plausible phone ──────────────────────────────────────────────────

    [Theory]
    [InlineData("+380671234567")]
    [InlineData("380671234567")]
    [InlineData("0671234567")]
    [InlineData("671234567")]
    [InlineData("+380 67 123 45 67")]
    [InlineData("(067) 123-45-67")]
    public void ParseTextList_accepts_every_supported_phone_shape(string phoneToken)
    {
        var result = SegmentImportParser.ParseTextList(phoneToken);

        Assert.Single(result.UniqueValidTokens);
        Assert.Equal(ImportTokenKind.Phone, result.UniqueValidTokens[0].Kind);
        Assert.Equal("+380671234567", result.UniqueValidTokens[0].NormalizedPhone);
        Assert.Equal(0, result.InvalidCount);
    }

    [Fact]
    public void ParseTextList_never_extracts_a_phone_number_from_inside_arbitrary_text()
    {
        // The exact bug class this parser exists to avoid (source doc §5.3): pulling digits out
        // of a free-text token that merely CONTAINS a phone-shaped digit run. Because this token
        // contains letters, it fails the phone-shape gate before PhoneNormalizer ever sees it.
        var result = SegmentImportParser.ParseTextList("invoice-0671234567-note");

        Assert.Empty(result.UniqueValidTokens);
        Assert.Equal(1, result.InvalidCount);
    }

    // ── Strict rejection: garbage / decimals / negatives / partial ─────────────────────────────

    [Theory]
    [InlineData("hello world")]
    [InlineData("customer#123")]
    [InlineData("N/A")]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseTextList_rejects_free_text_and_blank_tokens(string token)
    {
        var result = SegmentImportParser.ParseTextList(token);
        Assert.Empty(result.UniqueValidTokens);
    }

    [Fact]
    public void ParseTextList_rejects_a_decimal_number_as_a_single_invalid_token_never_two_ids()
    {
        // Source doc §5.3: "десяткове значення розбивається на дві цифрові частини" is the
        // documented competitor bug. "12345.6789" must become exactly ONE invalid token, not two
        // valid-looking numeric IDs (12345 and 6789).
        var result = SegmentImportParser.ParseTextList("12345.6789");

        Assert.Empty(result.UniqueValidTokens);
        Assert.Equal(1, result.InvalidCount);
        Assert.Equal(1, result.UploadedCount);
    }

    [Fact]
    public void ParseTextList_rejects_a_negative_number_rather_than_silently_dropping_the_sign()
    {
        // Source doc §5.3: "знак мінус відкидається" (the sign is silently dropped) is the
        // documented competitor bug — "-1234567890" must not become a valid positive phone shape.
        var result = SegmentImportParser.ParseTextList("-1234567890");

        Assert.Empty(result.UniqueValidTokens);
        Assert.Equal(1, result.InvalidCount);
    }

    // ── Dedup after normalization ────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseTextList_dedupes_two_differently_formatted_tokens_that_normalize_to_the_same_phone()
    {
        var result = SegmentImportParser.ParseTextList("0671234567,+380671234567");

        Assert.Equal(2, result.UploadedCount);
        Assert.Single(result.UniqueValidTokens);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Equal(0, result.InvalidCount);
    }

    [Fact]
    public void ParseTextList_dedupes_an_identical_guid_repeated_verbatim()
    {
        var guid = Guid.NewGuid();
        var raw = $"{guid:D}\n{guid:D}\n{guid:D}";
        var result = SegmentImportParser.ParseTextList(raw);

        Assert.Equal(3, result.UploadedCount);
        Assert.Single(result.UniqueValidTokens);
        Assert.Equal(2, result.DuplicateCount);
    }

    [Fact]
    public void ParseTextList_supports_both_newline_and_comma_separated_tokens_in_the_same_input()
    {
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        var g3 = Guid.NewGuid();
        var raw = $"{g1:D}\n{g2:D},{g3:D}";

        var result = SegmentImportParser.ParseTextList(raw);

        Assert.Equal(3, result.UploadedCount);
        Assert.Equal(3, result.UniqueValidTokens.Count);
        Assert.Equal(0, result.DuplicateCount);
    }

    // ── UploadedCount identity ───────────────────────────────────────────────────────────────────

    [Fact]
    public void UploadedCount_always_equals_invalid_plus_duplicate_plus_unique_valid_counts()
    {
        var g1 = Guid.NewGuid();
        var raw = string.Join('\n',
            g1.ToString("D"),      // valid guid
            g1.ToString("D"),      // duplicate of above
            "+380671234567",       // valid phone
            "0671234567",          // duplicate (normalizes to the same phone)
            "garbage text",        // invalid
            "12345.6789",          // invalid (decimal)
            "");                   // blank -> not counted at all

        var result = SegmentImportParser.ParseTextList(raw);

        Assert.Equal(6, result.UploadedCount); // blank line excluded
        Assert.Equal(2, result.InvalidCount);
        Assert.Equal(2, result.DuplicateCount);
        Assert.Equal(2, result.UniqueValidTokens.Count);
        Assert.Equal(result.UploadedCount, result.InvalidCount + result.DuplicateCount + result.UniqueValidTokens.Count);
    }

    [Fact]
    public void InvalidTokensSample_is_capped_at_500_but_InvalidCount_reflects_the_true_total()
    {
        // TASK-478 fix (bug writeup: bug-task476-unknown-tokens-export-capped-at-20): cap raised
        // 20 -> 500. Pinned here so a future accidental regression toward the old silent-20-cap
        // bug (which the export inherited with no truncation signal) fails loudly at this level,
        // not just when someone happens to notice a downloaded file is short.
        var raw = string.Join('\n', Enumerable.Range(0, 520).Select(i => $"bad-token-{i}"));
        var result = SegmentImportParser.ParseTextList(raw);

        Assert.Equal(520, result.InvalidCount);
        Assert.Equal(500, result.InvalidTokensSample.Count);
    }

    // ── Delimited rows (CSV/XLSX column detection) ──────────────────────────────────────────────

    [Fact]
    public void ParseDelimitedRows_detects_a_known_header_name_case_insensitively()
    {
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        IReadOnlyList<IReadOnlyList<string?>> rows =
        [
            new List<string?> { "Name", "Customer_ID", "Notes" },
            new List<string?> { "Ivan", g1.ToString("D"), "vip" },
            new List<string?> { "Olena", g2.ToString("D"), "" },
        ];

        var result = SegmentImportParser.ParseDelimitedRows(rows, explicitColumnIndex: null);

        Assert.Equal(1, result.DetectedColumnIndex);
        Assert.Equal("Customer_ID", result.DetectedColumnHeader);
        Assert.Equal(2, result.Tokens.UniqueValidTokens.Count);
    }

    [Fact]
    public void ParseDelimitedRows_falls_back_to_the_first_column_when_no_header_matches()
    {
        var g1 = Guid.NewGuid();
        IReadOnlyList<IReadOnlyList<string?>> rows =
        [
            new List<string?> { "Оригінальний список", "Коментар" },
            new List<string?> { g1.ToString("D"), "щось" },
        ];

        var result = SegmentImportParser.ParseDelimitedRows(rows, explicitColumnIndex: null);

        Assert.Equal(0, result.DetectedColumnIndex);
        Assert.Single(result.Tokens.UniqueValidTokens);
    }

    [Fact]
    public void ParseDelimitedRows_explicit_columnIndex_overrides_auto_detection()
    {
        var g1 = Guid.NewGuid();
        IReadOnlyList<IReadOnlyList<string?>> rows =
        [
            new List<string?> { "id", "phone" },
            new List<string?> { "not-a-guid", g1.ToString("D") }, // real id is actually in col 1 here
        ];

        var result = SegmentImportParser.ParseDelimitedRows(rows, explicitColumnIndex: 1);

        Assert.Equal(1, result.DetectedColumnIndex);
        Assert.Single(result.Tokens.UniqueValidTokens);
        Assert.Equal(g1, result.Tokens.UniqueValidTokens[0].CustomerId);
    }

    [Fact]
    public void ParseDelimitedRows_never_treats_the_header_row_itself_as_a_token()
    {
        // Even a header whose text technically LOOKS unlike any known name must still be
        // dropped — row 0 is always the header (documented v1 decision, see the parser's remarks).
        IReadOnlyList<IReadOnlyList<string?>> rows =
        [
            new List<string?> { "Список ідентифікаторів" },
        ];

        var result = SegmentImportParser.ParseDelimitedRows(rows, explicitColumnIndex: null);

        Assert.Empty(result.Tokens.UniqueValidTokens);
        Assert.Equal(0, result.Tokens.UploadedCount);
    }

    [Fact]
    public void ParseDelimitedRows_preview_is_capped_at_10_data_rows()
    {
        var header = new List<string?> { "id" };
        var dataRows = Enumerable.Range(0, 15).Select(i => (IReadOnlyList<string?>)new List<string?> { Guid.NewGuid().ToString("D") });
        var rows = new List<IReadOnlyList<string?>> { header };
        rows.AddRange(dataRows);

        var result = SegmentImportParser.ParseDelimitedRows(rows, explicitColumnIndex: null);

        Assert.Equal(10, result.PreviewRows.Count);
        Assert.Equal(15, result.Tokens.UniqueValidTokens.Count);
    }

    [Fact]
    public void ParseCsvText_splits_simple_comma_separated_rows()
    {
        var text = "id,phone\nA,B\nC,D";
        var rows = SegmentImportParser.ParseCsvText(text);

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "id", "phone" }, rows[0].ToArray());
        Assert.Equal(new[] { "A", "B" }, rows[1].ToArray());
    }
}
