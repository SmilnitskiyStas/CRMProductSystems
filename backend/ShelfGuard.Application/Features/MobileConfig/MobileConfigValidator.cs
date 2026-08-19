using System.Text.Json;
using ShelfGuard.Application.Features.MobileConfig.Dtos;

namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// See <see cref="IMobileConfigValidator"/>. Walks the parsed JSON manually (rather than
/// deserializing into typed DTOs) so every rejection can carry a precise field path and message —
/// the DoD requirement is "which field, what was wrong", not a generic failure. Whitelist values
/// live in <see cref="MobileConfigWhitelists"/>; this class only enforces shape (which JSON keys
/// are allowed on each object) and, for <c>features</c>/<c>navigation[].type</c>/
/// <c>navigation[].icon</c>/<c>pages</c>/<c>pages.*.blocks[].type</c>, restricts values to those
/// whitelists.
/// </summary>
public sealed class MobileConfigValidator : IMobileConfigValidator
{
    // Shape whitelists — which JSON keys each object level may contain. Distinct from
    // MobileConfigWhitelists, which restricts the *values* certain fields may take.
    private static readonly HashSet<string> TopLevelKeys = new() { "schemaVersion", "features", "navigation", "pages" };
    private static readonly HashSet<string> NavigationItemKeys = new() { "type", "label", "icon" };
    private static readonly HashSet<string> PageKeys = new() { "blocks" };
    private static readonly HashSet<string> BlockKeys = new() { "id", "type", "order", "props" };

    public MobileConfigValidationResult Validate(string configurationJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(configurationJson);
        }
        catch (JsonException ex)
        {
            return MobileConfigValidationResult.Failure(
                [new MobileConfigValidationError("$", $"Configuration is not valid JSON: {ex.Message}")]);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return MobileConfigValidationResult.Failure(
                    [new MobileConfigValidationError("$", "Configuration root must be a JSON object.")]);
            }

            var errors = new List<MobileConfigValidationError>();

            RejectUnknownKeys(root, TopLevelKeys, fieldPrefix: "", errors);
            ValidateSchemaVersion(root, errors);
            ValidateFeatures(root, errors);
            ValidateNavigation(root, errors);
            ValidatePages(root, errors);

            return errors.Count == 0
                ? MobileConfigValidationResult.Success()
                : MobileConfigValidationResult.Failure(errors);
        }
    }

    // ── schemaVersion ────────────────────────────────────────────────────────

    private static void ValidateSchemaVersion(JsonElement root, List<MobileConfigValidationError> errors)
    {
        if (!root.TryGetProperty("schemaVersion", out var schemaVersionEl))
        {
            errors.Add(new("schemaVersion", "schemaVersion is required."));
            return;
        }

        if (schemaVersionEl.ValueKind != JsonValueKind.Number || !schemaVersionEl.TryGetInt32(out var version))
        {
            errors.Add(new("schemaVersion", "schemaVersion must be an integer."));
            return;
        }

        if (!MobileConfigWhitelists.SupportedSchemaVersions.Contains(version))
        {
            errors.Add(new(
                "schemaVersion",
                $"Unsupported schemaVersion {version}. Supported: " +
                $"{string.Join(", ", MobileConfigWhitelists.SupportedSchemaVersions)}."));
        }
    }

    // ── features ─────────────────────────────────────────────────────────────

    private static void ValidateFeatures(JsonElement root, List<MobileConfigValidationError> errors)
    {
        if (!root.TryGetProperty("features", out var featuresEl))
        {
            errors.Add(new("features", "features is required."));
            return;
        }

        if (featuresEl.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new("features", "features must be a JSON object."));
            return;
        }

        foreach (var prop in featuresEl.EnumerateObject())
        {
            var field = $"features.{prop.Name}";

            if (!MobileConfigWhitelists.FeatureKeys.Contains(prop.Name))
            {
                errors.Add(new(field, $"Unknown feature key '{prop.Name}'."));
                continue;
            }

            if (prop.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                errors.Add(new(field, $"Feature '{prop.Name}' must be a boolean."));
        }
    }

    // ── navigation ───────────────────────────────────────────────────────────

    private static void ValidateNavigation(JsonElement root, List<MobileConfigValidationError> errors)
    {
        if (!root.TryGetProperty("navigation", out var navEl))
        {
            errors.Add(new("navigation", "navigation is required."));
            return;
        }

        if (navEl.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new("navigation", "navigation must be a JSON array."));
            return;
        }

        var items = navEl.EnumerateArray().ToList();
        if (items.Count < MobileConfigWhitelists.NavigationMinItems || items.Count > MobileConfigWhitelists.NavigationMaxItems)
        {
            errors.Add(new(
                "navigation",
                $"navigation must have between {MobileConfigWhitelists.NavigationMinItems} and " +
                $"{MobileConfigWhitelists.NavigationMaxItems} items (got {items.Count})."));
        }

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var field = $"navigation[{i}]";

            if (item.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new(field, "Each navigation item must be a JSON object."));
                continue;
            }

            RejectUnknownKeys(item, NavigationItemKeys, field, errors);

            if (!item.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            {
                errors.Add(new($"{field}.type", "type is required and must be a string."));
            }
            else if (!MobileConfigWhitelists.NavigationTypes.Contains(typeEl.GetString()!))
            {
                errors.Add(new($"{field}.type", $"Unknown navigation type '{typeEl.GetString()}'."));
            }

            // label stays a free string (MASTER SPEC §8) — only type-checked here, not
            // whitelisted. icon is whitelisted (TASK-542).
            RequireString(item, "label", field, errors);

            if (!item.TryGetProperty("icon", out var iconEl) || iconEl.ValueKind != JsonValueKind.String)
            {
                errors.Add(new($"{field}.icon", "icon is required and must be a string."));
            }
            else if (!MobileConfigWhitelists.NavigationIcons.Contains(iconEl.GetString()!))
            {
                errors.Add(new($"{field}.icon", $"Unknown navigation icon '{iconEl.GetString()}'."));
            }
        }
    }

    // ── pages ────────────────────────────────────────────────────────────────

    private static void ValidatePages(JsonElement root, List<MobileConfigValidationError> errors)
    {
        if (!root.TryGetProperty("pages", out var pagesEl))
        {
            errors.Add(new("pages", "pages is required."));
            return;
        }

        if (pagesEl.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new("pages", "pages must be a JSON object."));
            return;
        }

        foreach (var pageProp in pagesEl.EnumerateObject())
        {
            var pageField = $"pages.{pageProp.Name}";

            if (!MobileConfigWhitelists.PageNames.Contains(pageProp.Name))
            {
                errors.Add(new(pageField, $"Unknown page '{pageProp.Name}'."));
                continue;
            }

            if (pageProp.Value.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new(pageField, "Each page must be a JSON object."));
                continue;
            }

            RejectUnknownKeys(pageProp.Value, PageKeys, pageField, errors);

            if (!pageProp.Value.TryGetProperty("blocks", out var blocksEl))
            {
                errors.Add(new($"{pageField}.blocks", "blocks is required."));
                continue;
            }

            ValidateBlocks(blocksEl, pageField, errors);
        }
    }

    private static void ValidateBlocks(JsonElement blocksEl, string pageField, List<MobileConfigValidationError> errors)
    {
        if (blocksEl.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new($"{pageField}.blocks", "blocks must be a JSON array."));
            return;
        }

        var blocks = blocksEl.EnumerateArray().ToList();
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var field = $"{pageField}.blocks[{i}]";

            if (block.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new(field, "Each block must be a JSON object."));
                continue;
            }

            RejectUnknownKeys(block, BlockKeys, field, errors);

            // id is a referenceable identifier — unlike label/icon, empty defeats its purpose.
            RequireNonEmptyString(block, "id", field, errors);

            if (!block.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            {
                errors.Add(new($"{field}.type", "type is required and must be a string."));
            }
            else if (!MobileConfigWhitelists.BlockTypes.Contains(typeEl.GetString()!))
            {
                errors.Add(new($"{field}.type", $"Unknown block type '{typeEl.GetString()}'."));
            }

            if (!block.TryGetProperty("order", out var orderEl) ||
                orderEl.ValueKind != JsonValueKind.Number || !orderEl.TryGetInt32(out _))
            {
                errors.Add(new($"{field}.order", "order is required and must be an integer."));
            }

            // props stays loosely-validated free-form JSON — only its container type is checked.
            // Per-block-type prop schemas are TASK-538's scope (Block Registry validationSchema).
            if (!block.TryGetProperty("props", out var propsEl))
            {
                errors.Add(new($"{field}.props", "props is required."));
            }
            else if (propsEl.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new($"{field}.props", "props must be a JSON object."));
            }
        }
    }

    // ── shared helpers ───────────────────────────────────────────────────────

    private static void RejectUnknownKeys(
        JsonElement obj, HashSet<string> allowedKeys, string fieldPrefix, List<MobileConfigValidationError> errors)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (allowedKeys.Contains(prop.Name)) continue;

            var field = string.IsNullOrEmpty(fieldPrefix) ? prop.Name : $"{fieldPrefix}.{prop.Name}";
            errors.Add(new(field, $"Unknown field '{prop.Name}'."));
        }
    }

    private static void RequireString(
        JsonElement obj, string propertyName, string fieldPrefix, List<MobileConfigValidationError> errors)
    {
        if (!obj.TryGetProperty(propertyName, out var propEl) || propEl.ValueKind != JsonValueKind.String)
            errors.Add(new($"{fieldPrefix}.{propertyName}", $"{propertyName} is required and must be a string."));
    }

    private static void RequireNonEmptyString(
        JsonElement obj, string propertyName, string fieldPrefix, List<MobileConfigValidationError> errors)
    {
        var field = $"{fieldPrefix}.{propertyName}";
        if (!obj.TryGetProperty(propertyName, out var propEl) || propEl.ValueKind != JsonValueKind.String)
        {
            errors.Add(new(field, $"{propertyName} is required and must be a string."));
            return;
        }

        if (string.IsNullOrWhiteSpace(propEl.GetString()))
            errors.Add(new(field, $"{propertyName} must not be empty."));
    }
}
