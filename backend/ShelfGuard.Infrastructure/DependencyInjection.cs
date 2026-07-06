using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ShelfGuard.Application.Features.Pos.Fiscal;
using ShelfGuard.Application.Services;
using ShelfGuard.Application.Features.Analytics;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Infrastructure.Interceptors;
using ShelfGuard.Infrastructure.Integrations.Prro;
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

        var npgsqlDataSource = new NpgsqlDataSourceBuilder(
                configuration.GetConnectionString("DefaultConnection"))
            .EnableDynamicJson()
            .Build();

        services.AddDbContext<AppDbContext>((sp, options) =>
            options
                .UseNpgsql(npgsqlDataSource)
                .AddInterceptors(sp.GetRequiredService<TenantConnectionInterceptor>()));

        // Auth services
        services.AddSingleton<IJwtService, JwtService>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        // POC repository
        services.AddScoped<IProductRepository, ProductRepository>();

        // Catalog (v1 tenant-aware)
        services.AddScoped<IItemRepository, ItemRepository>();

        // Stock
        services.AddScoped<IStockRepository, StockRepository>();

        // Locations
        services.AddScoped<ILocationRepository, LocationRepository>();

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

        // v3.2 - POS (TASK-068)
        services.AddScoped<IPosRepository, PosRepository>();

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

        // v3.2 - ПРРО fiscalization (ADR-013): per-tenant factory replaces the startup-time
        // PRRO:PROVIDER switch. The factory reads each tenant's integration_configs row and
        // falls back to PRRO__* env vars → NoopFiscalService (offline-first, ADR-011).
        services.Configure<PrroOptions>(configuration.GetSection(PrroOptions.SectionName));
        services.AddSingleton<CheckboxTokenStoreRegistry>();
        // Named HttpClient for Checkbox — the factory creates instances from this pool.
        services.AddHttpClient("checkbox", (sp, http) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PrroOptions>>().Value;
            http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds > 0 ? opts.TimeoutSeconds : 30);
        });
        services.AddSingleton<IFiscalServiceFactory, FiscalServiceFactory>();

        // Provider panel (super admin)
        services.AddScoped<ITenantRepository, TenantRepository>();

        // SaaS Admin Panel (TASK-074)
        services.AddScoped<ITenantAdminRepository, TenantAdminRepository>();

        // Movements audit log
        services.AddScoped<IMovementRepository, MovementRepository>();

        // Support tickets
        services.AddScoped<ISupportTicketRepository,  SupportTicketRepository>();
        services.AddScoped<ISupportMessageRepository, SupportMessageRepository>();

        // v4 Phase 3 - Supplier Marketplace (TASK-221)
        services.AddScoped<IMarketplaceRepository, MarketplaceRepository>();

        // TASK-306 - Supplier staff roles + task board
        services.AddScoped<Domain.Interfaces.ISupplierRolesRepository,
            Data.Repositories.SupplierRolesRepository>();
        services.AddScoped<Domain.Interfaces.ISupplierTaskRepository,
            Data.Repositories.SupplierTaskRepository>();

        // TASK-313 - Supplier <-> client chat
        services.AddScoped<Domain.Interfaces.ISupplierChatRepository,
            Data.Repositories.SupplierChatRepository>();

        // v4 Phase 4 - Auto Service Module (TASK-231)
        services.AddScoped<Domain.Interfaces.IAutoServiceRepository,
            Data.Repositories.AutoServiceRepository>();

        // v4 Phase 5 - Production Module (TASK-241)
        services.AddScoped<Domain.Interfaces.IProductionRepository,
            Data.Repositories.ProductionRepository>();

        // v4 Phase 3 - AI Supplier Recommendation (TASK-223)
        services.AddScoped<Application.Features.Marketplace.ISupplierAdvisor,
            AI.SupplierAdvisor.SupplierAdvisor>();

        // v4 Phase 6 - AI Business Assistant (TASK-250)
        services.AddScoped<Application.Features.AiAssistant.IBusinessAssistantAdvisor,
            AI.BusinessAssistant.BusinessAssistantAdvisor>();

        // TASK-252 - CRM Customers
        services.AddScoped<Domain.Interfaces.ICustomerRepository,
            Data.Repositories.CustomerRepository>();

        // TASK-253 - Workforce Schedules
        services.AddScoped<Domain.Interfaces.IScheduleRepository,
            Data.Repositories.ScheduleRepository>();

        // TASK-251 - Service Desk
        services.AddScoped<Domain.Interfaces.ITicketRepository,
            Data.Repositories.TicketRepository>();

        // TASK-271 - Provider cross-tenant Service Desk
        services.AddScoped<Domain.Interfaces.IProviderTicketRepository,
            Data.Repositories.ProviderTicketRepository>();

        // TASK-273 - Provider employee statistics
        services.AddScoped<Domain.Interfaces.IProviderStatsRepository,
            Data.Repositories.ProviderStatsRepository>();

        // TASK-274 - Provider schedule slots
        services.AddScoped<Domain.Interfaces.IProviderScheduleRepository,
            Data.Repositories.ProviderScheduleRepository>();

        // TASK-278 - Live Chat
        services.AddScoped<Application.Features.Chat.IChatService,
            Services.ChatService>();

        // TASK-279 - Provider custom roles
        services.AddScoped<Application.Features.Provider.IProviderRolesService,
            Services.ProviderRolesService>();

        return services;
    }
}
