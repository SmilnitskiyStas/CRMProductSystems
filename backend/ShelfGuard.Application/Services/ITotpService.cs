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

    /// <summary>
    /// TASK-405 (Loyalty Фаза 0): computes the CURRENT 6-digit TOTP code for a secret.
    /// Unlike 2FA (where the authenticator app computes the code and the server only
    /// verifies), the loyalty "live QR" is rendered from a code the SERVER computes and
    /// hands to the client — the secret itself never leaves the server (plan: the mobile
    /// "my code" screen polls GET .../code rather than holding the Base32 secret on-device).
    /// </summary>
    string GenerateCode(string base32Secret);
}
