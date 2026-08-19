using ShelfGuard.Application.Features.MobileConfig;
using Xunit;

namespace ShelfGuard.Tests.MobileConfig;

/// <summary>
/// TASK-532 — whitelist-based validation of the mobile configuration document
/// (<c>MobileConfigurationVersion.ConfigurationJson</c>). Every rejection case asserts both that
/// validation fails AND that the specific offending field is reported, per the DoD requirement
/// ("which field, what was wrong", not a generic failure).
/// </summary>
public sealed class MobileConfigValidatorTests
{
    private readonly MobileConfigValidator _sut = new();

    private const string ValidDocument = """
    {
      "schemaVersion": 1,
      "features": { "loyalty": true, "promotions": false },
      "navigation": [
        { "type": "home", "label": "Головна", "icon": "home" },
        { "type": "profile", "label": "Профіль", "icon": "user" }
      ],
      "pages": {
        "home": {
          "blocks": [
            { "id": "block-1", "type": "heroBanner", "order": 1, "props": {} }
          ]
        }
      }
    }
    """;

    // ── Happy paths ──────────────────────────────────────────────────────────

    [Fact]
    public void Validate_accepts_a_well_formed_document()
    {
        var result = _sut.Validate(ValidDocument);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_accepts_empty_features_and_pages_when_navigation_is_within_limits()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "features": {},
          "navigation": [
            { "type": "home", "label": "A", "icon": "home" },
            { "type": "profile", "label": "B", "icon": "user" }
          ],
          "pages": {}
        }
        """;

        var result = _sut.Validate(json);

        Assert.True(result.IsValid);
    }

    // ── Structural rejections ───────────────────────────────────────────────

    [Fact]
    public void Validate_rejects_malformed_json()
    {
        var result = _sut.Validate("{ not json");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "$");
    }

    [Fact]
    public void Validate_rejects_non_object_root()
    {
        var result = _sut.Validate("[1,2,3]");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "$");
    }

    [Fact]
    public void Validate_rejects_unknown_top_level_field()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "features": {},
          "navigation": [
            { "type": "home", "label": "A", "icon": "home" },
            { "type": "profile", "label": "B", "icon": "user" }
          ],
          "pages": {},
          "extra": true
        }
        """;

        var result = _sut.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "extra");
    }

    // ── schemaVersion ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("\"1\"")]
    [InlineData("1.5")]
    [InlineData("true")]
    public void Validate_rejects_non_integer_schemaVersion(string rawValue)
    {
        var json = ValidDocument.Replace("\"schemaVersion\": 1,", $"\"schemaVersion\": {rawValue},");

        var result = _sut.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "schemaVersion");
    }

    [Fact]
    public void Validate_rejects_unsupported_schemaVersion()
    {
        var json = ValidDocument.Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 2,");

        var result = _sut.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "schemaVersion" && e.Message.Contains("Unsupported"));
    }

    [Fact]
    public void Validate_rejects_missing_schemaVersion()
    {
        const string json = """
        {
          "features": {},
          "navigation": [
            { "type": "home", "label": "A", "icon": "home" },
            { "type": "profile", "label": "B", "icon": "user" }
          ],
          "pages": {}
        }
        """;

        var result = _sut.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "schemaVersion");
    }

    // ── features ─────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_rejects_unknown_feature_key()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "features": { "loyalty": true, "bogusFeature": true },
          "navigation": [
            { "type": "home", "label": "A", "icon": "home" },
            { "type": "profile", "label": "B", "icon": "user" }
          ],
          "pages": {}
        }
        """;

        var result = _sut.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "features.bogusFeature");
    }

    [Fact]
    public void Validate_rejects_non_boolean_feature_value()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "features": { "loyalty": "yes" },
          "navigation": [
            { "type": "home", "label": "A", "icon": "home" },
            { "type": "profile", "label": "B", "icon": "user" }
          ],
          "pages": {}
        }
        """;

        var result = _sut.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "features.loyalty");
    }

    // ── navigation ───────────────────────────────────────────────────────────

    [Fact]
    public void Validate_rejects_fewer_than_2_navigation_items()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "features": {},
          "navigation": [ { "type": "home", "label": "A", "icon": "home" } ],
          "pages": {}
        }
        """;

        var result = _sut.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "navigation");
    }

    [Fact]
    public void Validate_rejects_more_than_5_navigation_items()
    {
        var items = string.Join(",", Enumerable.Range(0, 6)
            .Select(i => $"{{ \"type\": \"home\", \"label\": \"Item {i}\", \"icon\": \"home\" }}"));
        var json = $$"""
        {
          "schemaVersion": 1,
          "features": {},
          "navigation": [ {{items}} ],
          "pages": {}
        }
        """;

        var result = _sut.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "navigation");
    }

    [Fact]
    public void Validate_rejects_unknown_navigation_type()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "features": {},
          "navigation": [
            { "type": "bogus", "label": "A", "icon": "home" },
            { "type": "profile", "label": "B", "icon": "user" }
          ],
          "pages": {}
        }
        """;

        var result = _sut.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "navigation[0].type");
    }

    [Fact]
    public void Validate_rejects_unknown_key_in_navigation_item()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "features": {},
          "navigation": [
            { "type": "home", "label": "A", "icon": "home", "badge": "3" },
            { "type": "profile", "label": "B", "icon": "user" }
          ],
          "pages": {}
        }
        """;

        var result = _sut.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "navigation[0].badge");
    }

    [Theory]
    [InlineData("home")]
    [InlineData("tag")]
    [InlineData("grid")]
    [InlineData("qr")]
    [InlineData("ticket")]
    [InlineData("map")]
    [InlineData("news")]
    [InlineData("user")]
    public void Validate_accepts_every_whitelisted_navigation_icon(string icon)
    {
        var json = $$"""
        {
          "schemaVersion": 1,
          "features": {},
          "navigation": [
            { "type": "home", "label": "A", "icon": "{{icon}}" },
            { "type": "profile", "label": "B", "icon": "user" }
          ],
          "pages": {}
        }
        """;

        var result = _sut.Validate(json);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_rejects_unknown_navigation_icon()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "features": {},
          "navigation": [
            { "type": "home", "label": "A", "icon": "bogus" },
            { "type": "profile", "label": "B", "icon": "user" }
          ],
          "pages": {}
        }
        """;

        var result = _sut.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "navigation[0].icon" && e.Message.Contains("bogus"));
    }

    [Fact]
    public void Validate_rejects_navigation_item_missing_icon()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "features": {},
          "navigation": [
            { "type": "home", "label": "A" },
            { "type": "profile", "label": "B", "icon": "user" }
          ],
          "pages": {}
        }
        """;

        var result = _sut.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "navigation[0].icon");
    }

    [Fact]
    public void Validate_rejects_navigation_item_missing_label()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "features": {},
          "navigation": [
            { "type": "home", "icon": "home" },
            { "type": "profile", "label": "B", "icon": "user" }
          ],
          "pages": {}
        }
        """;

        var result = _sut.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "navigation[0].label");
    }

    // ── pages ────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_rejects_unknown_page_name()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "features": {},
          "navigation": [
            { "type": "home", "label": "A", "icon": "home" },
            { "type": "profile", "label": "B", "icon": "user" }
          ],
          "pages": { "bogusPage": { "blocks": [] } }
        }
        """;

        var result = _sut.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "pages.bogusPage");
    }

    [Fact]
    public void Validate_rejects_page_missing_blocks()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "features": {},
          "navigation": [
            { "type": "home", "label": "A", "icon": "home" },
            { "type": "profile", "label": "B", "icon": "user" }
          ],
          "pages": { "home": {} }
        }
        """;

        var result = _sut.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "pages.home.blocks");
    }

    // ── blocks ───────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_rejects_unknown_block_type()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "features": {},
          "navigation": [
            { "type": "home", "label": "A", "icon": "home" },
            { "type": "profile", "label": "B", "icon": "user" }
          ],
          "pages": {
            "home": { "blocks": [ { "id": "b1", "type": "bogusBlock", "order": 1, "props": {} } ] }
          }
        }
        """;

        var result = _sut.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "pages.home.blocks[0].type");
    }

    [Fact]
    public void Validate_rejects_block_with_empty_id()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "features": {},
          "navigation": [
            { "type": "home", "label": "A", "icon": "home" },
            { "type": "profile", "label": "B", "icon": "user" }
          ],
          "pages": {
            "home": { "blocks": [ { "id": "", "type": "heroBanner", "order": 1, "props": {} } ] }
          }
        }
        """;

        var result = _sut.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "pages.home.blocks[0].id");
    }

    [Fact]
    public void Validate_rejects_block_with_non_integer_order()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "features": {},
          "navigation": [
            { "type": "home", "label": "A", "icon": "home" },
            { "type": "profile", "label": "B", "icon": "user" }
          ],
          "pages": {
            "home": { "blocks": [ { "id": "b1", "type": "heroBanner", "order": "first", "props": {} } ] }
          }
        }
        """;

        var result = _sut.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "pages.home.blocks[0].order");
    }

    [Fact]
    public void Validate_rejects_block_with_non_object_props()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "features": {},
          "navigation": [
            { "type": "home", "label": "A", "icon": "home" },
            { "type": "profile", "label": "B", "icon": "user" }
          ],
          "pages": {
            "home": { "blocks": [ { "id": "b1", "type": "heroBanner", "order": 1, "props": [] } ] }
          }
        }
        """;

        var result = _sut.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "pages.home.blocks[0].props");
    }

    [Fact]
    public void Validate_rejects_unknown_key_in_block()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "features": {},
          "navigation": [
            { "type": "home", "label": "A", "icon": "home" },
            { "type": "profile", "label": "B", "icon": "user" }
          ],
          "pages": {
            "home": {
              "blocks": [ { "id": "b1", "type": "heroBanner", "order": 1, "props": {}, "visible": true } ]
            }
          }
        }
        """;

        var result = _sut.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "pages.home.blocks[0].visible");
    }

    [Fact]
    public void Validate_accepts_multiple_whitelisted_block_types_on_multiple_pages()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "features": { "loyalty": true, "news": true },
          "navigation": [
            { "type": "home", "label": "Головна", "icon": "home" },
            { "type": "loyalty", "label": "Картка", "icon": "qr" },
            { "type": "news", "label": "Новини", "icon": "news" }
          ],
          "pages": {
            "home": {
              "blocks": [
                { "id": "b1", "type": "heroBanner", "order": 1, "props": {} },
                { "id": "b2", "type": "loyaltyBalance", "order": 2, "props": { "showQr": true } }
              ]
            },
            "news": {
              "blocks": [ { "id": "b3", "type": "newsList", "order": 1, "props": {} } ]
            }
          }
        }
        """;

        var result = _sut.Validate(json);

        Assert.True(result.IsValid);
    }
}
