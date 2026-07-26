using OtpNet;
using ShelfGuard.Application.Services;

namespace ShelfGuard.Infrastructure.Services;

/// <summary>Otp.NET-backed TOTP implementation (TASK-330). 30s step, 6 digits, SHA1 —
/// the de-facto standard supported by Google Authenticator / Authy / 1Password.</summary>
public sealed class TotpService : ITotpService
{
    public string GenerateSecret() =>
        Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

    public long? VerifyCode(string base32Secret, string code)
    {
        if (string.IsNullOrWhiteSpace(base32Secret) || string.IsNullOrWhiteSpace(code))
            return null;

        byte[] key;
        try
        {
            key = Base32Encoding.ToBytes(base32Secret.Trim());
        }
        catch
        {
            return null; // malformed secret — treat as verification failure
        }

        var totp = new Totp(key);
        return totp.VerifyTotp(code.Trim(), out var timestep, VerificationWindow.RfcSpecifiedNetworkDelay)
            ? timestep
            : null;
    }

    /// <summary>TASK-405 (Loyalty Фаза 0): computes the current code — see interface doc.</summary>
    public string GenerateCode(string base32Secret)
    {
        var key = Base32Encoding.ToBytes(base32Secret.Trim());
        var totp = new Totp(key);
        return totp.ComputeTotp(DateTime.UtcNow);
    }
}
