using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Application.Features.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace ShelfGuard.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IAuthService, AuthService>();
        return services;
    }
}
