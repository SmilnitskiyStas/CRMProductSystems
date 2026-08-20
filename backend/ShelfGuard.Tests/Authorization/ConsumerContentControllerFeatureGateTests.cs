using System.Reflection;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Infrastructure.Authorization;
using Xunit;

namespace ShelfGuard.Tests.Authorization;

/// <summary>
/// TASK-558 — reflection-level proof of exactly which <see cref="ConsumerContentController"/>
/// actions carry <see cref="RequireConsumerFeatureAttribute"/> and with which flag key, plus proof
/// that the three banner actions carry none. <c>RequireConsumerFeatureFilterTests</c> already
/// covers the filter's own enabled/disabled/tenant-resolution mechanics in isolation, and
/// <see cref="ConsumerContentFeatureGateRlsIntegrationTests"/> covers the full real-service,
/// real-DB, safety-default wiring end to end — this file only pins the attribute placement itself,
/// the thing an accidental edit (wrong action, wrong flag key string, or a stray attribute on a
/// banner action) would most easily get wrong.
/// </summary>
public sealed class ConsumerContentControllerFeatureGateTests
{
    private static readonly Type ControllerType = typeof(ConsumerContentController);

    [Theory]
    [InlineData(nameof(ConsumerContentController.GetPromotions), "promotions")]
    [InlineData(nameof(ConsumerContentController.GetCatalog), "catalog")]
    [InlineData(nameof(ConsumerContentController.GetCatalogByIds), "catalog")]
    public void Gated_action_carries_RequireConsumerFeature_with_the_expected_flag_key(string methodName, string expectedFlagKey)
    {
        var method = GetPublicMethod(methodName);

        var attribute = method.GetCustomAttributes<RequireConsumerFeatureAttribute>(inherit: true).SingleOrDefault();

        Assert.NotNull(attribute);
        var flagKeyField = typeof(RequireConsumerFeatureAttribute)
            .GetField("_flagKey", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Equal(expectedFlagKey, flagKeyField.GetValue(attribute));
    }

    [Theory]
    [InlineData(nameof(ConsumerContentController.GetBanners))]
    [InlineData(nameof(ConsumerContentController.RecordView))]
    [InlineData(nameof(ConsumerContentController.RecordClick))]
    public void Banner_action_carries_no_RequireConsumerFeature_attribute(string methodName)
    {
        var method = GetPublicMethod(methodName);

        Assert.Empty(method.GetCustomAttributes<RequireConsumerFeatureAttribute>(inherit: true));
    }

    private static MethodInfo GetPublicMethod(string methodName) =>
        ControllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == methodName);
}
