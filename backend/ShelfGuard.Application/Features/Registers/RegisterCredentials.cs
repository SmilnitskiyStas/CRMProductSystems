using System.Security.Cryptography;
using System.Text;

namespace ShelfGuard.Application.Features.Registers;

/// <summary>
/// Opaque credential formats for the cashier app.
///
/// Both credentials embed the tenant id because the device endpoints run before any user JWT
/// exists, so RLS has no tenant context yet; the embedded id is only used to open a tenant-scoped
/// session (ITenantSessionOverride) and the secret part is then verified against the stored hash.
/// A forged tenant id therefore just fails verification inside that tenant.
///
///   device token   = "sgr1.{tenantId:N}.{registerId:N}.{secret}"   (secret: 32 random bytes, base64url)
///   activation code = "{tenantId:N}.{XXXX-XXXX}"                    (8 chars, unambiguous alphabet)
///
/// Only SHA-256 hex of the secret / normalized code is stored (both are high-entropy random values,
/// so a fast hash is appropriate; they are not user-chosen like a PIN).
/// </summary>
public static class RegisterCredentials
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I
    private const string DeviceTokenPrefix = "sgr1";

    public static (string Code, string Display) NewActivationCode()
    {
        var chars = new char[8];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        var code = new string(chars);
        return (code, $"{code[..4]}-{code[4..]}");
    }

    public static string FormatActivationCode(Guid tenantId, string display) => $"{tenantId:N}.{display}";

    public static bool TryParseActivationCode(string? raw, out Guid tenantId, out string normalizedCode)
    {
        tenantId = Guid.Empty;
        normalizedCode = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var parts = raw.Trim().Split('.', 2);
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out tenantId) || tenantId == Guid.Empty)
            return false;

        var code = new string(parts[1].Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (code.Length != 8 || code.Any(c => !CodeAlphabet.Contains(c))) return false;

        normalizedCode = code;
        return true;
    }

    public static string NewDeviceTokenSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string FormatDeviceToken(Guid tenantId, Guid registerId, string secret) =>
        $"{DeviceTokenPrefix}.{tenantId:N}.{registerId:N}.{secret}";

    public static bool TryParseDeviceToken(string? raw, out Guid tenantId, out Guid registerId, out string secret)
    {
        tenantId = registerId = Guid.Empty;
        secret = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var parts = raw.Trim().Split('.');
        if (parts.Length != 4 || parts[0] != DeviceTokenPrefix) return false;
        if (!Guid.TryParseExact(parts[1], "N", out tenantId) || tenantId == Guid.Empty) return false;
        if (!Guid.TryParseExact(parts[2], "N", out registerId) || registerId == Guid.Empty) return false;
        if (parts[3].Length < 20) return false;

        secret = parts[3];
        return true;
    }

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static bool HashesEqual(string? a, string? b) =>
        a is not null && b is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
