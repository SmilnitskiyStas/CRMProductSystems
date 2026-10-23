using Microsoft.AspNetCore.Authorization;
using ShelfGuard.Application.Services;

namespace ShelfGuard.Infrastructure.Authorization;

public sealed record WorkPermissionRequirement(string Permission) : IAuthorizationRequirement;
public sealed class WorkPermissionHandler(IWorkAccessContext access) : AuthorizationHandler<WorkPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, WorkPermissionRequirement requirement)
    {
        var scope = await access.GetAsync();
        if (scope?.Has(requirement.Permission) == true) context.Succeed(requirement);
    }
}
