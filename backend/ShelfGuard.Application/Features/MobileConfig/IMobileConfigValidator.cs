using ShelfGuard.Application.Features.MobileConfig.Dtos;

namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// Whitelist-based validator for the mobile configuration document stored on
/// <c>MobileConfigurationVersion.ConfigurationJson</c> (TASK-532). This is the core "declarative
/// JSON only, never executable code" security property the consumer app-builder platform is built
/// on: every field and value must resolve to a known key the mobile renderer's registries already
/// understand, or the document is rejected outright — never partially trusted.
/// </summary>
public interface IMobileConfigValidator
{
    /// <summary>
    /// Validates a raw JSON string against <see cref="MobileConfigWhitelists"/>. Never throws on
    /// malformed input — invalid JSON is reported as a validation error like any other rejection.
    /// </summary>
    MobileConfigValidationResult Validate(string configurationJson);
}
