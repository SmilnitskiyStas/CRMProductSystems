namespace ShelfGuard.Application.Features.MobileConfig.BlockRegistry;

/// <summary>
/// One constraint on a block type's <c>props</c> field (TASK-538) — deliberately a flat, simple
/// per-field descriptor (name/type/required/bounds/allowed values) rather than a full JSON-Schema
/// document. This is enough for TASK-540's Block Property Editor to generate the right input
/// control per field (text box / number stepper / switch / select) and for a future save-time
/// validator to type/range/enum-check a supplied value — without pulling in a general-purpose
/// JSON-Schema engine for what is, for every Core Blocks V1 type, always a shallow flat object.
///
/// <see cref="Required"/> is registry/UI metadata only — it is NOT enforced by
/// <c>MobileConfigValidator</c> today. See <c>BlockRegistry</c>'s class remarks for why wiring
/// per-block <c>props</c> presence/shape enforcement into the draft-save validator was left as
/// flagged follow-up work rather than done in this task.
/// </summary>
public sealed record BlockPropDefinition(
    string Name,
    string Type,
    bool Required,
    object? Default,
    int? MinLength = null,
    int? MaxLength = null,
    int? Min = null,
    int? Max = null,
    IReadOnlyList<string>? AllowedValues = null,
    int? MinItems = null,
    int? MaxItems = null);
