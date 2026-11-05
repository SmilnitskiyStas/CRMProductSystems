using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Pricing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using ShelfGuard.Application.Features.Pos.Fiscal;
using ShelfGuard.Application.Services;
using ShelfGuard.Application.Features.Analytics;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Infrastructure.Interceptors;
using ShelfGuard.Infrastructure.Resilience;
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
        // Authority configuration is validated at startup; an empty allowlist never means all stores.
        services.AddSingleton<IPosOnlinePricingGate>(new PosOnlinePricingGate(
            configuration.GetValue<bool>("Pricing:PosAuthority:OnlineEnabled"),
            configuration.GetSection("Pricing:PosAuthority:Scopes").Get<InventoryPostingScope[]>() ?? []));
        services.AddScoped<IPosBasePriceResolver, PosBasePriceResolver>();
        services.AddSingleton<IPosOfflinePricingGate>(new PosOfflinePricingGate(
            configuration.GetValue<bool>("Pricing:PosAuthority:OfflineEnabled"),
            configuration.GetSection("Pricing:PosAuthority:Scopes").Get<InventoryPostingScope[]>() ?? []));
        services.AddSingleton<Application.Features.Pos.IPosScopedPrices, PosScopedPrices>();
        services.AddScoped<Application.Features.Pos.IPosScopedCatalog, PosScopedCatalog>();
        services.AddSingleton<PricingShadow>(_ =>
        {
            try
            {
                var enabled = configuration.GetValue<bool>("Pricing:Shadow:Enabled");
                var rows = configuration.GetSection("Pricing:Shadow:Scopes").GetChildren().Take(101).ToArray();
                if (rows.Length > 100) throw new InvalidOperationException("Too many pricing shadow scopes.");
                var scopes = new HashSet<(Guid, Guid)>();
                foreach (var row in rows)
                {
                    if (!Guid.TryParse(row["TenantId"], out var tenant) || tenant == Guid.Empty
                        || !Guid.TryParse(row["LocationId"], out var location) || location == Guid.Empty)
                        throw new InvalidOperationException("Invalid pricing shadow scope.");
                    scopes.Add((tenant, location));
                }
                return new PricingShadow(enabled, scopes);
            }
            catch
            {
                // An optional observer's bad configuration must never prevent resolving PosService.
                var disabled = new PricingShadow(false, new HashSet<(Guid, Guid)>());
                disabled.ConfigurationRejected(); return disabled;
            }
        });
        services.AddSingleton<IPricingShadow>(sp => sp.GetRequiredService<PricingShadow>());
        // W4-B adjustment API requires both this application gate and the persisted database fence.
        services.AddSingleton<IInventoryPostingGate>(new InventoryPostingGate(
            configuration.GetValue<bool>("Erp:InventoryPosting:Enabled"),
            configuration.GetSection("Erp:InventoryPosting:Scopes").Get<InventoryPostingScope[]>() ?? []));
        services.AddScoped<IInventoryAdjustmentStore, InventoryAdjustmentCommandStore>();
        services.AddScoped<IReceiptPostingStore, ReceiptPostingStore>();
        services.AddScoped<ITransferPostingStore, TransferPostingStore>();
        services.AddScoped<TransferPostingStore>();
        services.AddScoped<IRequestShipment, RequestShipmentStore>();
        services.AddScoped<IWriteOffPostingStore, WriteOffPostingStore>();
        services.AddScoped<PosInventoryPostingBridge>();
        services.AddScoped<IPosReturnStore, PosReturnStore>();
        services.AddScoped<IProductionPostingPreviewReader, ProductionPostingPreviewReader>();
        services.AddScoped<ProductionPostingPreviewReader>();
        services.AddScoped<IProductionPostingStore, ProductionPostingStore>();
        services.AddScoped<IProductionDesktop, ProductionDesktopStore>();
        services.AddScoped<IRequestDesktop, RequestDesktopStore>();
        services.AddScoped<ICatalogDesktop, CatalogDesktopStore>();
        services.AddScoped<IPricingDesktop, PricingDesktopStore>();
        services.AddScoped<ILabelTemplates, LabelTemplateStore>();
        services.AddScoped<ILabelJobArchive, LabelJobArchiveStore>();
        services.AddScoped<ILabelPrinterProfiles, LabelPrinterProfileStore>();
        services.AddScoped<Application.Features.Pricing.IPosPricingHistory, PosPricingHistoryStore>();
        services.AddSingleton(new ProductionPostingOptions(configuration.GetValue<bool>("Erp:InventoryPosting:ProductionEnabled")));
        services.AddSingleton(new PosReturnOptions(configuration.GetValue<bool>("Erp:InventoryPosting:PosReturnsEnabled")));
        services.AddSingleton(new PosInventoryPostingOptions(configuration.GetValue<bool>("Erp:InventoryPosting:PosEnabled")));
        services.AddSingleton(new WriteOffPostingOptions(configuration.GetValue<bool>("Erp:InventoryPosting:WriteOffsEnabled")));
        services.AddSingleton(new TransferPostingOptions(configuration.GetValue<bool>("Erp:InventoryPosting:TransfersEnabled")));
        services.AddSingleton(new ReceiptPostingOptions(configuration.GetValue<bool>("Erp:InventoryPosting:ReceiptsEnabled")));
        services.AddScoped<IInventoryPostingStatusReader, InventoryPostingStatusReader>();
        services.AddSingleton<TenantConnectionInterceptor>();
        // TASK-528: centralized replacement for the per-controller ResolveTenantId()/GetTenantId()
        // duplication — see ITenantContext's doc for the contract and how it differs from
        // ITenantSessionOverride below. Scoped: wraps the request-scoped HttpContext.
        services.AddScoped<Services.TenantContext>();
        services.AddScoped<Application.Services.ITenantContext>(sp => sp.GetRequiredService<Services.TenantContext>());

        services.AddSingleton<NpgsqlDataSource>(_ => new NpgsqlDataSourceBuilder(
                configuration.GetConnectionString("DefaultConnection"))
            .EnableDynamicJson()
            .Build());

        services.AddDbContext<AppDbContext>((sp, options) =>
            options
                .UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>())
                .AddInterceptors(sp.GetRequiredService<TenantConnectionInterceptor>()));

        // Auth services
        services.AddSingleton<IJwtService, JwtService>();
        services.AddSingleton<ShelfGuard.Application.Features.Auth.IWorkerTokenIssuer>(s => (ShelfGuard.Application.Features.Auth.IWorkerTokenIssuer)s.GetRequiredService<IJwtService>());
        services.AddSingleton(new ShelfGuard.Application.Features.Auth.WorkerAuthOptions(configuration["WorkerAuth:Email"] ?? ""));
        services.AddSingleton<ShelfGuard.Application.Features.Auth.IWorkerSessionValidator, WorkerSessionValidator>();
        services.AddSingleton<Application.Features.Pos.IPosPriceSnapshots, PosPriceSnapshots>();
        services.AddSingleton<Application.Features.Pos.IPosPromotionSnapshots, PosPromotionSnapshots>();
        services.AddSingleton<Application.Features.Pos.IPosLoyaltySnapshots, PosLoyaltySnapshots>();
        services.AddScoped<Application.Features.Pos.IPosLoyaltyQuoteIssuer, PosLoyaltyQuoteIssuer>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<ITotpService, TotpService>(); // 2FA TOTP (TASK-330)
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IPasswordRecoveryStore, PasswordRecoveryStore>();
        services.AddScoped<IConsumerSessionStore, ConsumerSessionStore>();
        services.AddScoped<IWorkAccessContext, WorkAccessContext>();
        services.AddScoped<IMfaPolicy, LiveMfaPolicy>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, ShelfGuard.Infrastructure.Authorization.WorkPermissionHandler>();
        services.AddSingleton<IAccessSessionValidator, AccessSessionValidator>();
        services.AddSingleton(new ShelfGuard.Application.Features.Auth.PasswordRecoveryOptions(
            configuration["Frontend:BaseUrl"] ?? string.Empty));
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        // POC repository
        services.AddScoped<IProductRepository, ProductRepository>();

        // Catalog (v1 tenant-aware)
        services.AddScoped<IItemRepository, ItemRepository>();
        services.AddScoped<IItemImageRepository, ItemImageRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();

        // Stock
        services.AddScoped<IStockRepository, StockRepository>();

        // Supplier-portal expansion Phase 2 — supplier warehouse batch inventory + receiving
        services.AddScoped<Application.Features.SupplierInventory.ISupplierStockRepository,
            Data.Repositories.SupplierStockRepository>();
        services.AddScoped<Application.Features.SupplierInventory.ISupplierStockReceiptRepository,
            Data.Repositories.SupplierStockReceiptRepository>();

        // Supplier-portal expansion Phase 6b — supplier demand analytics
        services.AddScoped<Application.Features.SupplierAnalytics.ISupplierAnalyticsRepository,
            Data.Repositories.SupplierAnalyticsRepository>();

        // Supplier-portal expansion Phase 8 (TASK-695) — per-employee buyer ratings + team performance
        services.AddScoped<Domain.Interfaces.ISupplierEmployeeReviewRepository,
            Data.Repositories.SupplierEmployeeReviewRepository>();
        services.AddScoped<Application.Features.SupplierAnalytics.ISupplierTeamPerformanceRepository,
            Data.Repositories.SupplierTeamPerformanceRepository>();

        // Locations
        services.AddScoped<ILocationRepository, LocationRepository>();
        services.AddScoped<IShelfRepository, ShelfRepository>();

        // Legal Entities
        services.AddScoped<ILegalEntityRepository, LegalEntityRepository>();

        // Suppliers
        services.AddScoped<ISupplierRepository, SupplierRepository>();

        // Receipts
        services.AddScoped<IReceiptRepository, ReceiptRepository>();
        services.AddScoped<Application.Features.Receipts.IReceiptCategoryWork, ReceiptCategoryWorkStore>();

        // Transfers
        services.AddScoped<ITransferRepository, TransferRepository>();

        // Write-offs
        services.AddScoped<IWriteOffRepository, WriteOffRepository>();

        // Analytics
        services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();
        services.AddScoped<ShelfGuard.Application.Features.Analytics.PromoNoSales.IPromoNoSalesRepository, PromoNoSalesRepository>();

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
        services.AddScoped<ICashRegisterRepository, CashRegisterRepository>();
        services.AddScoped<IRegisterReportRepository, RegisterReportRepository>();
        services.AddMemoryCache();
        services.AddSingleton<Services.OperatorSessionValidator>();
        services.AddSingleton<Application.Services.IOperatorSessionValidator>(sp => sp.GetRequiredService<Services.OperatorSessionValidator>());
        services.AddSingleton<Application.Services.IOperatorSessionCache>(sp => sp.GetRequiredService<Services.OperatorSessionValidator>());
        services.AddScoped<ICatalogSyncRepository, CatalogSyncRepository>();
        services.AddScoped<IPromotionRuleRepository, PromotionRuleRepository>();

        // v2 - Daily sales (ADU source data)
        services.AddScoped<IDailySalesRepository, DailySalesRepository>();

        // TASK-335 - Daily stock status snapshots (dashboard week-over-week diff)
        services.AddScoped<IStockStatusSnapshotRepository, StockStatusSnapshotRepository>();
        services.AddScoped<IAduRepository, AduRepository>();
        services.AddScoped<ISupplyScheduleRepository, SupplyScheduleRepository>();
        services.AddScoped<IBufferRepository, BufferRepository>();
        services.AddScoped<IOrderCalcRepository, OrderCalcRepository>();
        services.AddScoped<IOrderTrackingRepository, OrderTrackingRepository>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IWeatherRepository, WeatherRepository>();
        services.AddScoped<ICannibalizationRepository, CannibalizationRepository>();
        services.AddScoped<IAiOrderRepository, AiOrderRepository>();
        services.AddScoped<ITelegramLinkRepository, TelegramLinkRepository>();
        services.AddScoped<IAiOrderAdvisor, AI.ClaudeOrderAdvisor>();
        // Managed-AI Phase 1: mandatory per-tenant isolation guardrail wrapped around every
        // advisor's system prompt (see IAiPromptResolver / AiGuardrail); provider "test
        // connection" probe for the client-card AI config.
        // Managed-AI Phase 2: provider-agnostic chat client — every advisor calls the model
        // through IAiClientFactory (Claude SDK or a thin OpenAI-compatible HttpClient).
        services.AddScoped<Application.Services.IAiPromptResolver, AI.AiPromptResolver>();
        services.AddScoped<Application.Services.IAiClientFactory, AI.AiClientFactory>();
        services.AddScoped<Application.Services.IAiConnectivityTester, AI.AiConnectivityTester>();
        services.AddHttpClient("openai", http => http.Timeout = TimeSpan.FromSeconds(60));
        // Stage 2 resilience: optional dependencies get retry (GET only) + circuit breaker +
        // attempt timeout, and surface as ExternalServiceUnavailableException when down.
        services.AddHttpClient<Domain.Interfaces.IOpenMeteoClient, Integrations.OpenMeteoClient>()
            .AddUnavailableTranslation("weather")
            .AddExternalResilience("open-meteo", TimeSpan.FromSeconds(10), maxRetries: 2);
        // Nominatim has a strict usage policy — one retry at most.
        services.AddHttpClient<Domain.Interfaces.IGeocodingClient, Integrations.NominatimGeocodingClient>()
            .AddUnavailableTranslation("geocoder")
            .AddExternalResilience("nominatim", TimeSpan.FromSeconds(8), maxRetries: 1);

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
        })
        // No retry: POST receipts/sell is not idempotent (a retry could fiscalize twice) — the
        // fiscalization-retry job already re-submits safely. Breaker + timeout only, so a
        // Checkbox outage fails fast into the offline-first queue (ADR-011).
        .AddExternalResilience("checkbox", TimeSpan.FromSeconds(25), maxRetries: 0);
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
        services.AddScoped<Domain.Interfaces.IProductionDemandRepository,
            Data.Repositories.ProductionDemandRepository>();
        services.AddScoped<Domain.Interfaces.IProductionRequestRepository,
            Data.Repositories.ProductionRequestRepository>();
        services.AddScoped<Domain.Interfaces.IProductionInsightsRepository,
            Data.Repositories.ProductionInsightsRepository>();
        services.AddScoped<Application.Features.Production.IProductionDemandTenantRunner,
            Services.ProductionDemandTenantRunner>();
        services.AddScoped<Application.Features.Receipts.IAutoReplenishmentTenantRunner,
            Services.AutoReplenishmentTenantRunner>();

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
        // Lead owner push (Telegram). Off by default; config only from env/settings (Leads__Telegram__*).
        services.Configure<Integrations.LeadsTelegramOptions>(
            configuration.GetSection(Integrations.LeadsTelegramOptions.Section));
        services.AddHttpClient<Application.Features.Leads.ILeadNotifier, Integrations.TelegramLeadNotifier>(
                http => http.Timeout = TimeSpan.FromSeconds(5))
            .RemoveAllLoggers(); // request URL contains the bot token — keep it out of HttpClient logs
        services.AddScoped<Domain.Interfaces.ILandingLeadRepository,
            Data.Repositories.LandingLeadRepository>();

        // ADR-018 - Beauty CRM control plane. Outbound HMAC client (ShelfGuard -> Beauty) and inbound
        // HMAC verifier (Beauty -> ShelfGuard). Secrets only from env (BeautyControl__*, BeautyFeedbackInbound__*).
        services.TryAddSingleton(TimeProvider.System);
        services.Configure<Application.Features.BeautyControl.BeautyControlOptions>(
            configuration.GetSection(Application.Features.BeautyControl.BeautyControlOptions.Section));
        services.Configure<Application.Features.BeautyControl.BeautyFeedbackInboundOptions>(
            configuration.GetSection(Application.Features.BeautyControl.BeautyFeedbackInboundOptions.Section));
        services.AddHttpClient<Application.Features.BeautyControl.IBeautyControlClient, Integrations.BeautyControlClient>()
            // Signed X-BCRM-* headers must never be replayed to a redirect target.
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers(); // never log request URLs/bodies of the control plane
        services.AddSingleton<Application.Features.BeautyControl.IBeautyNonceStore, Integrations.BeautyNonceStore>();
        services.AddSingleton<Application.Features.BeautyControl.IBeautyInboundKeyProvider, Integrations.BeautyInboundKeyProvider>();
        services.AddSingleton<Application.Features.BeautyControl.IBeautyInboundAuthenticator,
            Application.Features.BeautyControl.BeautyInboundAuthenticator>();
        services.AddScoped<Domain.Interfaces.IBeautyTenantLinkRepository,
            Data.Repositories.BeautyTenantLinkRepository>();

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
        services.AddScoped<Domain.Interfaces.IShelfPhotoAdvisor, AI.ShelfPhotoAdvisor.ShelfPhotoAdvisor>();
        services.AddScoped<Domain.Interfaces.IShelfRestockAdvisor, AI.ShelfRestockAdvisor.ShelfRestockAdvisor>();
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

        // managed-AI Phase 4b - consumer AI assistant audit trail
        services.AddScoped<Domain.Interfaces.IConsumerAiRequestRepository,
            Data.Repositories.ConsumerAiRequestRepository>();

        // TASK-625 - Realtime SignalR transport for consumer support ticket threads
        // (ConsumerSupportHub, mapped in Program.cs at /api/hubs/consumer-support). Keep-alive/
        // client-timeout set explicitly (spec §5 "бажано додати keep-alive") — these values match
        // the framework defaults, made explicit so the choice is documented rather than implicit.
        var signalR = services.AddSignalR(options =>
        {
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
            options.HandshakeTimeout = TimeSpan.FromSeconds(15);
        });
        // Stage 3b: with the API split into role instances (core/consumer/provider) staff replies
        // published from CORE must reach hub clients connected to CONSUMER, so a Redis backplane is
        // used when SignalR:RedisConnection is set. AbortOnConnectFail=false: a Redis outage degrades
        // realtime only and never prevents the API from starting.
        var signalRRedis = configuration["SignalR:RedisConnection"];
        if (!string.IsNullOrWhiteSpace(signalRRedis))
        {
            signalR.AddStackExchangeRedis(o =>
            {
                var opts = StackExchange.Redis.ConfigurationOptions.Parse(signalRRedis);
                opts.AbortOnConnectFail = false;
                opts.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("shelfguard");
                o.Configuration = opts;
            });
        }
        services.AddScoped<Application.Features.CustomerSupport.IConsumerSupportRealtimeNotifier,
            Realtime.ConsumerSupportRealtimeNotifier>();

        return services;
    }
}
