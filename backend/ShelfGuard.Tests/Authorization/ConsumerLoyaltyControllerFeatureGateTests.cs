using System.Reflection;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Infrastructure.Authorization;
using Xunit;

namespace ShelfGuard.Tests.Authorization;

/// <summary>
/// TASK-559 — reflection-level proof of exactly which <see cref="ConsumerLoyaltyController"/>
/// action carries <see cref="RequireConsumerFeatureAttribute"/> (Option A: discovery/join only),
/// and proof that <see cref="ConsumerLoyaltyController.GetMemberships"/>/
/// <see cref="ConsumerLoyaltyController.GetCode"/>/
/// <see cref="ConsumerLoyaltyController.SetPreferredStore"/>/
/// <see cref="ConsumerLoyaltyController.GetHistory"/> — the existing-member data actions Option A
/// deliberately never gates — carry none. Same role split as TASK-558's
/// <c>ConsumerContentControllerFeatureGateTests</c>: <c>RequireConsumerFeatureFilterTests</c>
/// covers the filter's own mechanics, <see cref="LoyaltyFeatureGateRlsIntegrationTests"/> covers
/// the full real-service/real-DB wiring including the Option-A-vs-B proof; this file only pins
/// the attribute placement itself — the thing an accidental edit (wrong action, wrong flag key
/// string, or a stray attribute landing on an existing-member action) would most easily get wrong.
/// </summary>
public sealed class ConsumerLoyaltyControllerFeatureGateTests
{
    private static readonly Type ControllerType = typeof(ConsumerLoyaltyController);

    [Fact]
    public void Join_carries_RequireConsumerFeature_with_the_loyalty_flag_key()
    {
        var method = GetPublicMethod(nameof(ConsumerLoyaltyController.Join));

        var attribute = method.GetCustomAttributes<RequireConsumerFeatureAttribute>(inherit: true).SingleOrDefault();

        Assert.NotNull(attribute);
        var flagKeyField = typeof(RequireConsumerFeatureAttribute)
            .GetField("_flagKey", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Equal("loyalty", flagKeyField.GetValue(attribute));
    }

    [Theory]
    [InlineData(nameof(ConsumerLoyaltyController.GetMemberships))]
    [InlineData(nameof(ConsumerLoyaltyController.GetNetworks))]
    [InlineData(nameof(ConsumerLoyaltyController.GetCode))]
    [InlineData(nameof(ConsumerLoyaltyController.SetPreferredStore))]
    [InlineData(nameof(ConsumerLoyaltyController.GetHistory))]
    public void Existing_member_action_carries_no_RequireConsumerFeature_attribute(string methodName)
    {
        var method = GetPublicMethod(methodName);

        Assert.Empty(method.GetCustomAttributes<RequireConsumerFeatureAttribute>(inherit: true));
    }

    private static MethodInfo GetPublicMethod(string methodName) =>
        ControllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == methodName);
}
