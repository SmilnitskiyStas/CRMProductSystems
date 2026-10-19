namespace ShelfGuard.Application.Services;

public static class JwtAudiences
{
    /// <summary>Appended to the API audience for cashier-app operator tokens ("shelfguard:register").</summary>
    public const string RegisterSuffix = ":register";
}
