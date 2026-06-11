using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace ShelfGuard.Api.Infrastructure;

/// <summary>
/// Replaces the default ProblemDetails transformation that [ApiController] applies to
/// body-less client error results (bare NotFound(), Forbid(), etc.) with the project-wide
/// error contract: { "error": "Human-readable message" } (see api-contracts.md).
/// Controllers that already pass an object (e.g. NotFound(new { error })) are unaffected.
/// </summary>
public sealed class ErrorBodyClientErrorFactory : IClientErrorFactory
{
    public IActionResult GetClientError(ActionContext actionContext, IClientErrorActionResult clientError)
    {
        var status = clientError.StatusCode ?? StatusCodes.Status500InternalServerError;
        var message = status switch
        {
            StatusCodes.Status400BadRequest => "Bad request.",
            StatusCodes.Status401Unauthorized => "Unauthorized.",
            StatusCodes.Status403Forbidden => "Forbidden.",
            StatusCodes.Status404NotFound => "Not found.",
            StatusCodes.Status409Conflict => "Conflict.",
            _ => "Request failed.",
        };

        return new ObjectResult(new { error = message }) { StatusCode = status };
    }
}
