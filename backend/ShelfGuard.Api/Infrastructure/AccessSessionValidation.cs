using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using ShelfGuard.Application.Services;

namespace ShelfGuard.Api.Infrastructure;

public static class AccessSessionValidation
{
    public static async Task ValidateAsync(TokenValidatedContext context, string registerAudience, string? workerAudience = null)
    {
        var worker = context.Principal?.FindAll("aud").Any(c => c.Value == workerAudience) == true;
        if (worker)
        {
            if (context.Principal!.FindAll("aud").Count() != 1 || context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<AllowWorkerTokenAttribute>() == null)
            { context.Fail("Worker token is not accepted by this endpoint."); return; }
            try {
                var validator = context.HttpContext.RequestServices.GetRequiredService<IWorkerSessionValidator>();
                if (!await validator.IsValidAsync(context.Principal, context.HttpContext.RequestAborted)) context.Fail("Worker identity is no longer valid.");
            }
            catch (Exception error) when (error is not OperationCanceledException) { context.Fail("Worker validation unavailable."); }
            return;
        }
        await OperatorTokenValidation.ValidateAsync(context, registerAudience);
        if (context.Result?.Failure is not null || context.Principal is null) return;
        if (context.Principal.HasClaim("aud", registerAudience)) return; // Separate POS session validator already ran.
        try
        {
            var validator = context.HttpContext.RequestServices.GetRequiredService<IAccessSessionValidator>();
            if (!await validator.IsValidAsync(context.Principal, context.HttpContext.RequestAborted)) context.Fail("Session is no longer valid.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("AccessSessionValidation")
                .LogError("Session validation unavailable ({ErrorType}).", exception.GetType().Name);
            context.Fail("Session validation unavailable.");
        }
    }
}
