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

        // v3 - IoT devices
        services.AddScoped<IIotDeviceRepository, IotDeviceRepository>();

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

        // v3.2 - ПРРО fiscalization (ADR-012): Checkbox client only when configured,
        // otherwise Noop keeps the offline-first sale flow running without a provider.
        services.Configure<Integrations.Prro.PrroOptions>(
            configuration.GetSection(Integrations.Prro.PrroOptions.SectionName));
        var prroProvider = configuration["PRRO:PROVIDER"];
        if (string.Equals(prroProvider, "checkbox", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<Integrations.Prro.CheckboxTokenStore>();
            services.AddHttpClient<Application.Features.Pos.Fiscal.IFiscalService,
                                   Integrations.Prro.CheckboxFiscalClient>((sp, http) =>
            {
                var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Integrations.Prro.PrroOptions>>().Value;
                http.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
                http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
            });
        }
        else
        {
            services.AddSingleton<Application.Features.Pos.Fiscal.IFiscalService,
                                  Application.Features.Pos.Fiscal.NoopFiscalService>();
        }

        // Provider panel (super admin)
        services.AddScoped<ITenantRepository, TenantRepository>();

        // Movements audit log
        services.AddScoped<IMovementRepository, MovementRepository>();

        return services;
    }
}
