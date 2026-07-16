using ShelfGuard.Infrastructure.Data;
using Xunit;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// KI-028: the startup canary's decision policy. The DB round-trip in Program.cs is not unit-
/// testable without a WebApplicationFactory harness (which this repo doesn't have), so the policy
/// is factored into <see cref="RlsRoleGuard.Evaluate"/> and pinned here.
/// </summary>
public class RlsRoleGuardTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Non_bypassing_role_is_ok_in_every_environment(bool isDevelopment)
        => Assert.Equal(
            RlsRoleGuard.Decision.Ok,
            RlsRoleGuard.Evaluate(roleBypassesRls: false, isDevelopment));

    [Fact]
    public void Bypassing_role_only_warns_in_development()
        => Assert.Equal(
            RlsRoleGuard.Decision.WarnDevelopment,
            RlsRoleGuard.Evaluate(roleBypassesRls: true, isDevelopment: true));

    [Fact]
    public void Bypassing_role_fails_fast_outside_development()
        => Assert.Equal(
            RlsRoleGuard.Decision.FailFast,
            RlsRoleGuard.Evaluate(roleBypassesRls: true, isDevelopment: false));
}
