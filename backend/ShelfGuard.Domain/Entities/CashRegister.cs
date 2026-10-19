namespace ShelfGuard.Domain.Entities;

/// <summary>
/// A physical/logical cash register (cashier workstation or self-checkout kiosk) registered to a
/// store. Authenticated by a long-lived device token (stored only as a hash) obtained once via a
/// single-use activation code; operators then sign in on top of it with a numeric PIN.
/// </summary>
public sealed class CashRegister
{
    public const string TypeLine = "line";
    public const string TypeSelfCheckout = "self_checkout";

    public const string StatusPending = "pending";   // created, not activated yet
    public const string StatusActive = "active";
    public const string StatusRevoked = "revoked";
    /// <summary>Daily PIN-failure cap hit: sign-in refused until a manager unlocks the register.</summary>
    public const string StatusLocked = "locked";

    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid StoreId { get; set; }
    /// <summary>Human-facing register number, unique per tenant.</summary>
    public int Number { get; init; }
    public string Type { get; set; } = TypeLine;
    public string Name { get; set; } = string.Empty;
    public string Status { get; private set; } = StatusPending;

    /// <summary>SHA-256 (hex) of the device-token secret; null until activated / after revoke.</summary>
    public string? DeviceTokenHash { get; private set; }
    /// <summary>SHA-256 (hex) of the one-time activation code; cleared on use.</summary>
    public string? ActivationCodeHash { get; private set; }
    public DateTime? ActivationCodeExpiresAt { get; private set; }
    public DateTime? ActivatedAt { get; private set; }

    public DateTime? LastSeenAt { get; set; }
    public string? AppVersion { get; set; }

    // Brute-force guard (device level). Failures decay with a fixed time window and are NEVER
    // cleared by a successful sign-in (otherwise an attacker holding one valid PIN could reset the
    // counter between guesses). A per-day cap flips the register to 'locked' until a manager unlocks it.
    public int PinFailCount { get; private set; }
    public DateTime? PinFailWindowStart { get; private set; }
    public int PinFailDailyCount { get; private set; }
    public DateOnly? PinFailDay { get; private set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public Location? Store { get; init; }

    /// <summary>True while the in-window failure count has reached <paramref name="maxInWindow"/>.</summary>
    public bool IsPinLockedOut(int maxInWindow, TimeSpan window, DateTime nowUtc) =>
        PinFailCount >= maxInWindow && PinFailWindowStart.HasValue && PinFailWindowStart.Value > nowUtc - window;

    /// <summary>Manager unlock after the daily cap: clears all counters and re-enables sign-in.</summary>
    public void Unlock()
    {
        PinFailCount = 0;
        PinFailWindowStart = null;
        PinFailDailyCount = 0;
        PinFailDay = null;
        if (Status == StatusLocked)
            Status = DeviceTokenHash is null ? StatusPending : StatusActive;
    }

    /// <summary>Issues a fresh activation code (replacing any previous unused one). Invalid when revoked.</summary>
    public void IssueActivationCode(string codeHash, DateTime expiresAt)
    {
        ActivationCodeHash = codeHash;
        ActivationCodeExpiresAt = expiresAt;
    }

    /// <summary>Marks the register revoked and invalidates both the device token and any pending code.</summary>
    public void Revoke()
    {
        Status = StatusRevoked;
        DeviceTokenHash = null;
        ActivationCodeHash = null;
        ActivationCodeExpiresAt = null;
    }

    /// <summary>
    /// In-memory mirror of the atomic SQL in CashRegisterRepository.RegisterPinFailureAsync
    /// (kept in lockstep; used by fakes/tests). Returns (in-window count, daily count).
    /// </summary>
    public (int WindowCount, int DailyCount) RegisterPinFailure(DateTime nowUtc, TimeSpan window, int maxDaily)
    {
        var day = DateOnly.FromDateTime(nowUtc);
        if (PinFailWindowStart is null || PinFailWindowStart.Value <= nowUtc - window)
        {
            PinFailCount = 1;
            PinFailWindowStart = nowUtc;
        }
        else
        {
            PinFailCount++;
        }

        PinFailDailyCount = PinFailDay == day ? PinFailDailyCount + 1 : 1;
        PinFailDay = day;
        if (Status == StatusActive && PinFailDailyCount >= maxDaily)
            Status = StatusLocked;
        return (PinFailCount, PinFailDailyCount);
    }

    public void Touch(string? appVersion)
    {
        LastSeenAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(appVersion)) AppVersion = appVersion;
    }
}
