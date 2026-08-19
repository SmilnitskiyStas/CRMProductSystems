using System.Text.Json;
using ShelfGuard.Application.Features.MobileConfig;
using Xunit;

namespace ShelfGuard.Tests.MobileConfig;

/// <summary>
/// TASK-533 — asserts <c>/contracts/mobile-config.schema.json</c> (the canonical contract for the
/// full served <c>GET /api/v1/mobile/config</c> document) and <see cref="MobileConfigWhitelists"/>
/// (the pre-publish draft validator's single source of truth, TASK-532) never silently drift apart
/// on the enums/bounds they share: <c>schemaVersion</c>, <c>features</c> keys,
/// <c>navigation[].type</c>, <c>navigation[].icon</c>, <c>navigation</c> min/max item count,
/// <c>pages</c> names, and <c>pages.*.blocks[].type</c>.
///
/// <c>theme</c>'s structural fields (colors/radii shape) are deliberately NOT compared here —
/// <see cref="MobileConfigWhitelists"/> has no theme concept at all (TASK-532's draft document
/// rejects a <c>theme</c> key by design; theme is composed in only at publish time, TASK-544). The
/// schema's <c>theme</c> shape instead comes from <c>MobileTheme.cs</c>'s typed fields.
/// <c>theme.spacing</c>'s enum, however, IS compared below against
/// <see cref="MobileThemeWhitelists.SpacingPresets"/> (TASK-536) — the one theme field that gained
/// its own dedicated whitelist, extending this same schema-agreement pattern to that second,
/// independent source of truth.
/// </summary>
public sealed class MobileConfigSchemaContractTests
{
    private static readonly JsonElement Schema = LoadSchema();

    [Fact]
    public void Schema_file_is_valid_json_and_uses_draft07()
    {
        Assert.Equal(JsonValueKind.Object, Schema.ValueKind);
        Assert.Equal(
            "http://json-schema.org/draft-07/schema#",
            Schema.GetProperty("$schema").GetString());
    }

    [Fact]
    public void SchemaVersion_enum_matches_whitelist()
    {
        var enumEl = Schema.GetProperty("properties").GetProperty("schemaVersion").GetProperty("enum");
        var values = enumEl.EnumerateArray().Select(e => e.GetInt32()).ToHashSet();

        Assert.Equal(MobileConfigWhitelists.SupportedSchemaVersions.ToHashSet(), values);
    }

    [Fact]
    public void Feature_keys_match_whitelist()
    {
        var props = Schema.GetProperty("properties").GetProperty("features").GetProperty("properties");
        var keys = props.EnumerateObject().Select(p => p.Name).ToHashSet();

        Assert.Equal(MobileConfigWhitelists.FeatureKeys.ToHashSet(), keys);
    }

    [Fact]
    public void Navigation_types_match_whitelist()
    {
        var enumEl = Schema.GetProperty("properties").GetProperty("navigation")
            .GetProperty("items").GetProperty("properties").GetProperty("type").GetProperty("enum");

        Assert.Equal(MobileConfigWhitelists.NavigationTypes.ToHashSet(), StringEnum(enumEl));
    }

    [Fact]
    public void Navigation_icons_match_whitelist()
    {
        var enumEl = Schema.GetProperty("properties").GetProperty("navigation")
            .GetProperty("items").GetProperty("properties").GetProperty("icon").GetProperty("enum");

        Assert.Equal(MobileConfigWhitelists.NavigationIcons.ToHashSet(), StringEnum(enumEl));
    }

    [Fact]
    public void Navigation_item_count_bounds_match_whitelist()
    {
        var navigation = Schema.GetProperty("properties").GetProperty("navigation");

        Assert.Equal(MobileConfigWhitelists.NavigationMinItems, navigation.GetProperty("minItems").GetInt32());
        Assert.Equal(MobileConfigWhitelists.NavigationMaxItems, navigation.GetProperty("maxItems").GetInt32());
    }

    [Fact]
    public void Page_names_match_whitelist()
    {
        var props = Schema.GetProperty("properties").GetProperty("pages").GetProperty("properties");
        var keys = props.EnumerateObject().Select(p => p.Name).ToHashSet();

        Assert.Equal(MobileConfigWhitelists.PageNames.ToHashSet(), keys);
    }

    [Fact]
    public void Block_types_match_whitelist()
    {
        var enumEl = Schema.GetProperty("definitions").GetProperty("block")
            .GetProperty("properties").GetProperty("type").GetProperty("enum");

        Assert.Equal(MobileConfigWhitelists.BlockTypes.ToHashSet(), StringEnum(enumEl));
    }

    [Fact]
    public void Theme_spacing_enum_matches_theme_whitelist()
    {
        var enumEl = Schema.GetProperty("properties").GetProperty("theme")
            .GetProperty("properties").GetProperty("spacing").GetProperty("enum");

        Assert.Equal(MobileThemeWhitelists.SpacingPresets.ToHashSet(), StringEnum(enumEl));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static HashSet<string> StringEnum(JsonElement arrayEl) =>
        arrayEl.EnumerateArray().Select(e => e.GetString()!).ToHashSet();

    private static JsonElement LoadSchema()
    {
        var path = FindRepoFile(Path.Combine("contracts", "mobile-config.schema.json"));
        var json = File.ReadAllText(path);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>
    /// Walks up from the test assembly's output directory (<c>backend/ShelfGuard.Tests/bin/.../</c>)
    /// to find the repo root, where <c>/contracts/</c> lives — rather than hardcoding a
    /// <c>../../../../..</c> relative path that would silently break under Debug vs Release or a
    /// moved project.
    /// </summary>
    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException(
            $"Could not locate '{relativePath}' by walking up from '{AppContext.BaseDirectory}'.");
    }
}
