namespace ShelfGuard.Infrastructure.Authorization;

[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowWorkerTokenAttribute : Attribute;
