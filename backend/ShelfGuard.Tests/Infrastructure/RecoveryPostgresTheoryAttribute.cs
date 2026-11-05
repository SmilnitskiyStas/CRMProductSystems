using Xunit;

namespace ShelfGuard.Tests.Infrastructure;

public sealed class RecoveryPostgresTheoryAttribute : TheoryAttribute
{
    public RecoveryPostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SHELFGUARD_RECOVERY_TEST_DB")))
            Skip = "Requires an explicit disposable recovery PostgreSQL database.";
    }
}
