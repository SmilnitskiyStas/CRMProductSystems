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
        // TASK-528: centralized replacement for the per-controller ResolveTenantId()/GetTenantId()
        // duplication — see ITenantContext's doc for the contract and how it differs from
        // ITenantSessionOverride below. Scoped: wraps the request-scoped HttpContext.
        services.AddScoped<Application.Services.ITenantContext, Services.TenantContext>();

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
        services.AddSingleton<ITotpService, TotpService>(); // 2FA TOTP (TASK-330)
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        // POC repository
        services.AddScoped<IProductRepository, ProductRepository>();

        // Catalog (v1 tenant-aware)
        services.AddScoped<IItemRepository, ItemRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();

        // Stock
        services.AddScoped<IStockRepository, StockRepository>();

        // Supplier-portal expansion Phase 2 — supplier warehouse batch inventory + receiving
        services.AddScoped<Application.Features.SupplierInventory.ISupplierStockRepository,
            Data.Repositories.SupplierStockRepository>();
        services.AddScoped<Application.Features.SupplierInventory.ISupplierStockReceiptRepository,
            Data.Repositories.SupplierStockReceiptRepository>();

        // Locations
        services.AddScoped<ILocationRepository, LocationRepository>();

        // Legal Entities
        services.AddScoped<ILegalEntityRepository, LegalEntityRepository>();

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

        // Consumer App plan (TASK-521) - Banners (admin CRUD) + consumer-facing content reads
        services.AddScoped<IBannerRepository, BannerRepository>();
        services.AddScoped<IPromotionCampaignRepository, PromotionCampaignRepository>();
        services.AddScoped<Application.Features.ConsumerContent.IConsumerContentRepository,
            ConsumerContentRepository>();

        // Consumer app-builder platform (TASK-531/532) - MobileConfiguration domain
        services.AddScoped<Domain.Interfaces.IMobileConfigurationRepository,
            Data.Repositories.MobileConfigurationRepository>();

        // Activity log
        services.AddScoped<IActivityLogRepository, ActivityLogRepository>();

        // v3 - IoT devices
        services.AddScoped<IIotDeviceRepository, IotDeviceRepository>();

        // v3.2 - POS (TASK-068)
        services.AddScoped<IPosRepository, PosRepository>();

        // v2 - Daily sales (ADU source data)
        services.AddScoped<IDailySalesRepository, DailySalesRepository>();

        // TASK-335 - Daily stock status snapshots (dashboard week-over-week diff)
        services.AddScoped<IStockStatusSnapshotRepository, StockStatusSnapshotRepository>();
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

        // TASK-316 - Supplier cooperation: agreements, marketplace orders, support tickets
        services.AddScoped<Domain.Interfaces.ISupplierContractSettingsRepository,
            Data.Repositories.SupplierContractSettingsRepository>();
        services.AddScoped<Domain.Interfaces.ISupplierAgreementRepository,
            Data.Repositories.SupplierAgreementRepository>();
        services.AddScoped<Domain.Interfaces.IMarketplaceOrderRepository,
            Data.Repositories.MarketplaceOrderRepository>();
        services.AddScoped<Domain.Interfaces.ISupplierSupportTicketRepository,
            Data.Repositories.SupplierSupportTicketRepository>();

        // TASK-586 - Marketplace order receiving (client-confirmed receipt, ADR-033)
        services.AddScoped<Domain.Interfaces.IMarketplaceOrderReceiptRepository,
            Data.Repositories.MarketplaceOrderReceiptRepository>();

        // TASK-317 - Contract PDF generation (QuestPDF) + Вчасно e-signature.
        // License is set here once at startup; ContractPdfGenerator's static ctor
        // re-asserts it defensively for non-DI usage (tests).
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        services.AddSingleton<Application.Features.Marketplace.IContractPdfGenerator,
            Documents.ContractPdfGenerator>();
        services.AddHttpClient(Integrations.Vchasno.VchasnoClientFactory.ServiceName,
            http => http.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<Application.Features.Marketplace.Vchasno.IVchasnoClientFactory,
            Integrations.Vchasno.VchasnoClientFactory>();

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

        // TASK-333 - Landing page leads
        services.AddScoped<Domain.Interfaces.ILandingLeadRepository,
            Data.Repositories.LandingLeadRepository>();

        // TASK-341 - Temporary per-user permission grants (ADR-019)
        services.AddScoped<Domain.Interfaces.IUserPermissionGrantRepository,
            Data.Repositories.UserPermissionGrantRepository>();

        // TASK-345 - Tenant custom role capability templates (ADR-020)
        services.AddScoped<Domain.Interfaces.ITenantRoleRepository,
            Data.Repositories.TenantRoleRepository>();

        // TASK-392/392b - Store-scoped user<->location assignment (Stage 1 plumbing)
        services.AddScoped<Domain.Interfaces.IUserLocationRepository,
            Data.Repositories.UserLocationRepository>();

        // TASK-405 - Loyalty program Фаза 0 (ConsumerAccount / LoyaltyMembership / ledger / settings)
        services.AddScoped<Domain.Interfaces.IConsumerAccountRepository,
            Data.Repositories.ConsumerAccountRepository>();
        services.AddScoped<Domain.Interfaces.ILoyaltyRepository,
            Data.Repositories.LoyaltyRepository>();
        // Backs the resolve-code per-membership rate-limit/lockout (IResolveCodeAttemptTracker) —
        // single-instance-deployment tradeoff, see that interface's doc.
        services.AddMemoryCache();
        services.AddSingleton<Application.Services.IResolveCodeAttemptTracker,
            Services.MemoryResolveCodeAttemptTracker>();
        // TASK-417: lets LoyaltyService.JoinAsync temporarily assume the target tenant's RLS
        // context for the one consumer-session write path (customers) that has no
        // identity-based RLS policy of its own — see ITenantSessionOverride's doc for the
        // security contract. Scoped (not singleton) — wraps the request-scoped AppDbContext.
        services.AddScoped<Application.Services.ITenantSessionOverride,
            Services.TenantSessionOverride>();

        // TASK-508/KI-033 (ADR-028): lets MarketingAnalyticsRepository's queries run under a
        // session role that store_scope's RESTRICTIVE policy on pos_transactions recognizes as
        // exempt, for the duration of one repository method only — see
        // IAnalyticsRlsOverride's doc for the security contract. Scoped, same as
        // ITenantSessionOverride above — wraps the request-scoped AppDbContext.
        services.AddScoped<Application.Services.IAnalyticsRlsOverride,
            Services.AnalyticsRlsOverride>();

        // TASK-643/KI-036 (ADR-035): scopes MarketplaceRepository's cross-tenant provider_bypass
        // reads to one transaction each (SET LOCAL app.role = 'provider'), replacing the
        // session-level SET that leaked the provider role into the rest of the HTTP request —
        // see IProviderRlsOverride's doc for the security contract. Scoped, same as the two
        // overrides above — wraps the request-scoped AppDbContext.
        services.AddScoped<Application.Services.IProviderRlsOverride,
            Services.ProviderRlsOverride>();

        // TASK-406 - Marketing analytics Фаза 1 (RFM engine + dashboard)
        services.AddScoped<Application.Features.MarketingAnalytics.IMarketingAnalyticsRepository,
            Data.Repositories.MarketingAnalyticsRepository>();
        services.AddScoped<Domain.Interfaces.IMarketingAdvisor,
            AI.MarketingAdvisor.MarketingAdvisor>();
        // Stateless — same AddSingleton convention as IContractPdfGenerator above.
        services.AddSingleton<Application.Common.IExcelExportService,
            Export.ExcelExportService>();

        // TASK-420 - Marketing analytics Фаза 2 (price segments + frequency/reactivation)
        services.AddScoped<Application.Features.MarketingAnalytics.PriceSegments.IPriceSegmentsRepository,
            Data.Repositories.PriceSegmentsRepository>();
        services.AddScoped<Domain.Interfaces.IPriceSegmentAdvisor,
            AI.PriceSegmentAdvisor.PriceSegmentAdvisor>();

        // TASK-429 - Marketing analytics Фаза 3 (AudienceBuilder — product/category audiences,
        // competitor conquest audiences)
        services.AddScoped<Application.Features.MarketingAnalytics.AudienceBuilder.IAudienceBuilderRepository,
            Data.Repositories.AudienceBuilderRepository>();

        // TASK-472 - Marketing analytics Фаза 4 (post-campaign audience analysis)
        services.AddScoped<Application.Features.MarketingAnalytics.PostCampaign.IPostCampaignRepository,
            Data.Repositories.PostCampaignRepository>();
        services.AddScoped<Domain.Interfaces.IPostCampaignAdvisor,
            AI.PostCampaignAdvisor.PostCampaignAdvisor>();
        // Stateless — same AddSingleton convention as IExcelExportService above.
        services.AddSingleton<Application.Common.IExcelImportService,
            Export.ExcelImportService>();

        // TASK-616 - Consumer↔tenant support tickets (async channel, mirrors SupplierSupportTicketRepository)
        services.AddScoped<Domain.Interfaces.IConsumerSupportTicketRepository,
            Data.Repositories.ConsumerSupportTicketRepository>();

        // TASK-617 - Consumer purchase reviews (rating + comment on a PosTransaction, one staff reply)
        services.AddScoped<Domain.Interfaces.IPurchaseReviewRepository,
            Data.Repositories.PurchaseReviewRepository>();

        // TASK-625 - Realtime SignalR transport for consumer support ticket threads
        // (ConsumerSupportHub, mapped in Program.cs at /api/hubs/consumer-support). Keep-alive/
        // client-timeout set explicitly (spec §5 "бажано додати keep-alive") — these values match
        // the framework defaults, made explicit so the choice is documented rather than implicit.
        services.AddSignalR(options =>
        {
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
            options.HandshakeTimeout = TimeSpan.FromSeconds(15);
        });
        services.AddScoped<Application.Features.CustomerSupport.IConsumerSupportRealtimeNotifier,
            Realtime.ConsumerSupportRealtimeNotifier>();

        return services;
    }
}
