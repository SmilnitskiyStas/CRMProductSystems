namespace ShelfGuard.Infrastructure.Authorization;

/// <summary>
/// Endpoint marker: this action/controller may be called with a cashier-app operator token
/// (audience "shelfguard:register"). Operator tokens are rejected on every unmarked endpoint by
/// the JwtBearer OnTokenValidated check, so a till credential can never reach admin APIs.
/// Marked: PosController, the POS-facing LoyaltyController actions, and registers/session/end.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowOperatorTokenAttribute : Attribute
{
}
