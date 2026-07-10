namespace ShelfGuard.Application.Services;

/// <summary>
/// TOTP (RFC 6238) code generation/verification abstraction (TASK-330).
/// Implementation lives in Infrastructure (Otp.NET) — Application stays clean.
/// </summary>
public interface ITotpService
{
    /// <summary>Generates a new random secret, Base32-encoded (for authenticator apps).</summary>
    string GenerateSecret();

    /// <summary>
    /// Verifies a 6-digit TOTP code against the secret with a ±1 timestep window.
    /// Returns the matched timestep on success (for anti-replay tracking), null on failure.
    /// </summary>
    long? VerifyCode(string base32Secret, string code);
}
