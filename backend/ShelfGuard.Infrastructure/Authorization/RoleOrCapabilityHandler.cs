using Microsoft.AspNetCore.Authorization;

namespace ShelfGuard.Infrastructure.Authorization;

/// <summary>
/// Evaluates <see cref="RoleOrCapabilityRequirement"/>: succeeds when the caller's role is one
/// of <see cref="RoleOrCapabilityRequirement.AllowedRoles"/> OR their JWT "capabilities" claim
/// contains <see cref="RoleOrCapabilityRequirement.Capability"/> (ADR-020, TASK-346).
/// Stateless — registered as a singleton in Program.cs (services.AddSingleton&lt;IAuthorizationHandler, RoleOrCapabilityHandler&gt;()).
/// </summary>
public sealed class RoleOrCapabilityHandler : AuthorizationHandler<RoleOrCapabilityRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, RoleOrCapabilityRequirement requirement)
    {
        if (requirement.AllowedRoles.Any(context.User.IsInRole) ||
            TenantRoleAuthorization.HasCapability(context.User, requirement.Capability))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
