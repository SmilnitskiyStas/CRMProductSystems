using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShelfGuard.Application.Services;
using ShelfGuard.Application.Features.Analytics;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Infrastructure.Interceptors;
using ShelfGuard.Infrastructure.Services;

namespace ShelfGuard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<TenantConnectionInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) =>
            options
                .UseNpgsql(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(sp.GetRequiredService<TenantConnectionInterceptor>()));

        // Auth services
        services.AddSingleton<IJwtService, JwtService>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        // POC repository
        services.AddScoped<IProductRepository, ProductRepository>();

        // Catalog (v1 tenant-aware)
        services.AddScoped<ICatalogProductRepository, CatalogProductRepository>();

        // Stock
        services.AddScoped<IStockRepository, StockRepository>();

        // Stores
        services.AddScoped<IStoreRepository, StoreRepository>();

        // Suppliers
        services.AddScoped<ISupplierRepository, SupplierRepository>();

        // Receipts
        services.AddScoped<IReceiptRepository, ReceiptRepository>();

        // Transfers
        services.AddScoped<ITransferRepository, TransferRepository>();

        // Write-offs
        services.AddScoped<IWriteOffRepository, WriteOffRepository>();

        // Analytics
        services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();

        // Notifications
        services.AddScoped<INotificationRepository, NotificationRepository>();

        // Integrations
        services.AddScoped<IIntegrationRepository, IntegrationRepository>();

        // Discounts
        services.AddScoped<IDiscountRepository, DiscountRepository>();

        // Activity log
        services.AddScoped<IActivityLogRepository, ActivityLogRepository>();

        // v2 - Daily sales (ADU source data)
        services.AddScoped<IDailySalesRepository, DailySalesRepository>();
        services.AddScoped<IAduRepository, AduRepository>();
        services.AddScoped<ISupplyScheduleRepository, SupplyScheduleRepository>();
        services.AddScoped<IBufferRepository, BufferRepository>();
        services.AddScoped<IOrderCalcRepository, OrderCalcRepository>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IWeatherRepository, WeatherRepository>();
        services.AddScoped<ICannibalizationRepository, CannibalizationRepository>();
        services.AddScoped<IAiOrderRepository, AiOrderRepository>();
        services.AddScoped<ITelegramLinkRepository, TelegramLinkRepository>();
        services.AddScoped<IAiOrderAdvisor, AI.ClaudeOrderAdvisor>();
        services.AddHttpClient<Domain.Interfaces.IOpenMeteoClient, Integrations.OpenMeteoClient>();

        // Provider panel (super admin)
        services.AddScoped<ITenantRepository, TenantRepository>();

        // Movements audit log
        services.AddScoped<IMovementRepository, MovementRepository>();

        return services;
    }
}
