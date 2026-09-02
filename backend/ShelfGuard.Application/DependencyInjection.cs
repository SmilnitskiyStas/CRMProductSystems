using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Application.Features.Catalog;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Application.Features.Locations;
using ShelfGuard.Application.Features.Suppliers;
using ShelfGuard.Application.Features.Receipts;
using ShelfGuard.Application.Features.Transfers;
using ShelfGuard.Application.Features.WriteOffs;
using ShelfGuard.Application.Features.Analytics;
using ShelfGuard.Application.Features.Notifications;
using ShelfGuard.Application.Features.Integrations;
using ShelfGuard.Application.Features.Users;
using ShelfGuard.Application.Features.Discounts;
using ShelfGuard.Application.Features.Provider;
using ShelfGuard.Application.Features.Movements;
using ShelfGuard.Application.Features.Admin;
using ShelfGuard.Application.Features.Settings;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.AutoService;
using ShelfGuard.Application.Features.Production;
using ShelfGuard.Application.Features.AiAssistant;
using ShelfGuard.Application.Features.Customers;
using ShelfGuard.Application.Features.Schedules;
using ShelfGuard.Application.Features.ServiceDesk;
using ShelfGuard.Application.Features.LegalEntities;
using ShelfGuard.Application.Features.Leads;
using ShelfGuard.Application.Features.Geo;
using Microsoft.Extensions.DependencyInjection;

namespace ShelfGuard.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IItemService, ItemService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IProviderCategoryService, ProviderCategoryService>();
        services.AddScoped<IStockService, StockService>();
        services.AddScoped<ILocationService, LocationService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<IReceiptService, ReceiptService>();
        services.AddScoped<ITransferService, TransferService>();
        services.AddScoped<IWriteOffService, WriteOffService>();
        services.AddScoped<IAnalyticsService, AnalyticsService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IIntegrationService, IntegrationService>();
        services.AddScoped<IPrroSettingsService, PrroSettingsService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IDiscountService, DiscountService>();
        services.AddScoped<Features.Banners.IBannerService, Features.Banners.BannerService>();
        services.AddScoped<Features.PromotionCampaigns.IPromotionCampaignService, Features.PromotionCampaigns.PromotionCampaignService>();
        services.AddScoped<Features.ConsumerContent.IConsumerContentService, Features.ConsumerContent.ConsumerContentService>();
        services.AddScoped<IProviderService, ProviderService>();
        services.AddScoped<IProviderTeamService, ProviderTeamService>();
        services.AddScoped<IMovementService, MovementService>();
        services.AddScoped<Features.Sales.IDailySalesService, Features.Sales.DailySalesService>();
        services.AddScoped<Features.Adu.IAduService, Features.Adu.AduService>();
        services.AddScoped<Features.SupplySchedules.ISupplyScheduleService, Features.SupplySchedules.SupplyScheduleService>();
        services.AddScoped<Features.Buffer.IBufferService, Features.Buffer.BufferService>();
        services.AddScoped<Features.Orders.IOrderCalcService, Features.Orders.OrderCalcService>();
        services.AddScoped<Features.Events.IEventService, Features.Events.EventService>();
        services.AddScoped<Features.Weather.IWeatherService, Features.Weather.WeatherService>();
        services.AddScoped<Features.Cannibalization.ICannibalizationService, Features.Cannibalization.CannibalizationService>();
        services.AddScoped<Features.AiOrders.IAiOrderService, Features.AiOrders.AiOrderService>();
        services.AddScoped<Features.Telegram.ITelegramLinkService, Features.Telegram.TelegramLinkService>();
        services.AddScoped<Features.IoT.IIotDeviceService, Features.IoT.IotDeviceService>();

        // v3.2 - POS (TASK-068)
        services.AddScoped<Features.Pos.IPosService, Features.Pos.PosService>();

        // SaaS Admin Panel (TASK-074)
        services.AddScoped<ITenantAdminService, TenantAdminService>();

        // v4 - Module activation (TASK-208)
        services.AddScoped<IModulesSettingsService, ModulesSettingsService>();

        // v4 Phase 3 - Supplier Marketplace (TASK-221)
        services.AddScoped<IMarketplaceService, MarketplaceService>();

        // v4.1 - Supplier self-service cabinet (TASK-284, ADR-016)
        services.AddScoped<ISupplierCabinetService, SupplierCabinetService>();

        // TASK-306 - Supplier staff roles + task board
        services.AddScoped<ISupplierRolesService, SupplierRolesService>();
        services.AddScoped<ISupplierTaskService, SupplierTaskService>();

        // TASK-313 - Supplier <-> client chat
        services.AddScoped<ISupplierChatService, SupplierChatService>();

        // TASK-317 - Supplier cooperation: agreements + orders + support tickets
        services.AddScoped<ISupplierAgreementService, SupplierAgreementService>();
        services.AddScoped<IMarketplaceOrderService, MarketplaceOrderService>();
        services.AddScoped<ISupplierSupportService, SupplierSupportService>();

        // TASK-586 - Marketplace order receiving (client-confirmed receipt, ADR-033)
        services.AddScoped<IMarketplaceOrderReceiptService, MarketplaceOrderReceiptService>();

        // v4 Phase 4 - Auto Service Module (TASK-231)
        services.AddScoped<IAutoServiceService, AutoServiceService>();

        // v4 Phase 5 - Production Module (TASK-241)
        services.AddScoped<IProductionService, ProductionService>();

        // v4 Phase 6 - AI Business Assistant (TASK-250)
        services.AddScoped<IAiAssistantService, AiAssistantService>();

        // TASK-252 - CRM Customers
        services.AddScoped<ICustomerService, CustomerService>();

        // TASK-253 - Workforce Schedules
        services.AddScoped<IScheduleService, ScheduleService>();

        // TASK-251 - Service Desk
        services.AddScoped<ITicketService, TicketService>();

        // TASK-271 - Provider cross-tenant Service Desk
        services.AddScoped<IProviderTicketService, ProviderTicketService>();

        // TASK-273 - Provider employee statistics
        services.AddScoped<IProviderStatsService, ProviderStatsService>();

        // TASK-274 - Provider schedule
        services.AddScoped<IProviderScheduleService, ProviderScheduleService>();

        // TASK-321/322 - Legal Entities (юридичні особи)
        services.AddScoped<ILegalEntityService, LegalEntityService>();

        // TASK-333 - Landing page lead capture
        services.AddScoped<ILandingLeadService, LandingLeadService>();

        // ADR-020 / TASK-345/346 - Tenant custom role capability templates
        services.AddScoped<Features.TenantRoles.ITenantRoleService, Features.TenantRoles.TenantRoleService>();

        // TASK-405 - Loyalty program Фаза 0 (ConsumerAccount auth + membership/bonus logic)
        services.AddScoped<Features.ConsumerAuth.IConsumerAuthService, Features.ConsumerAuth.ConsumerAuthService>();
        services.AddScoped<Features.Loyalty.ILoyaltyService, Features.Loyalty.LoyaltyService>();

        // TASK-614 - Consumer self-service profile editing (name/email/phone + audit history)
        services.AddScoped<Features.ConsumerProfile.IConsumerProfileService, Features.ConsumerProfile.ConsumerProfileService>();

        // TASK-406 - Marketing analytics Фаза 1 (RFM engine + dashboard)
        services.AddScoped<Features.MarketingAnalytics.IMarketingAnalyticsService, Features.MarketingAnalytics.MarketingAnalyticsService>();

        // TASK-420 - Marketing analytics Фаза 2 (price segments + frequency/reactivation)
        services.AddScoped<
            Features.MarketingAnalytics.PriceSegments.IPriceSegmentsService,
            Features.MarketingAnalytics.PriceSegments.PriceSegmentsService>();

        // TASK-429 - Marketing analytics Фаза 3 (AudienceBuilder)
        services.AddScoped<
            Features.MarketingAnalytics.AudienceBuilder.IAudienceBuilderService,
            Features.MarketingAnalytics.AudienceBuilder.AudienceBuilderService>();

        // TASK-472 - Marketing analytics Фаза 4 (post-campaign audience analysis)
        services.AddScoped<
            Features.MarketingAnalytics.PostCampaign.IPostCampaignService,
            Features.MarketingAnalytics.PostCampaign.PostCampaignService>();

        // TASK-532 - Consumer app-builder platform: config JSON validation + draft CRUD
        services.AddScoped<Features.MobileConfig.IMobileConfigValidator, Features.MobileConfig.MobileConfigValidator>();
        services.AddScoped<Features.MobileConfig.IMobileConfigDraftService, Features.MobileConfig.MobileConfigDraftService>();

        // TASK-534 - Consumer app-builder platform: GET /api/v1/mobile/config (published read)
        services.AddScoped<
            Features.MobileConfig.IMobileConfigPublishedReadService,
            Features.MobileConfig.MobileConfigPublishedReadService>();

        // TASK-536 - Consumer app-builder platform: theme domain validation + PUT endpoint
        services.AddScoped<Features.MobileConfig.IMobileThemeValidator, Features.MobileConfig.MobileThemeValidator>();
        services.AddScoped<Features.MobileConfig.IMobileThemeService, Features.MobileConfig.MobileThemeService>();

        // TASK-538 - Consumer app-builder platform: Block Registry (static catalog, GET /api/v1/mobile/blocks)
        services.AddSingleton<
            Features.MobileConfig.BlockRegistry.IBlockRegistryProvider,
            Features.MobileConfig.BlockRegistry.BlockRegistryProvider>();

        // TASK-543 - Consumer app-builder platform: consumer-session-aware feature flags (Stage D ЕТАП 10)
        services.AddScoped<Features.MobileConfig.IConsumerFeatureFlagService, Features.MobileConfig.ConsumerFeatureFlagService>();
        services.AddScoped<Features.MobileConfig.ISubscriptionPlanFeatureGate, Features.MobileConfig.SubscriptionPlanFeatureGate>();

        // TASK-544 - Consumer app-builder platform: generalized Draft->Validate->Publish (Stage D ЕТАП 11)
        services.AddScoped<Features.MobileConfig.IMobileConfigPublishService, Features.MobileConfig.MobileConfigPublishService>();

        // TASK-545 - Consumer app-builder platform: Version History + Rollback (Stage D ЕТАП 12)
        services.AddScoped<
            Features.MobileConfig.IMobileConfigVersionHistoryService,
            Features.MobileConfig.MobileConfigVersionHistoryService>();

        // TASK-547 - Consumer app-builder platform: staff-only draft Preview API (Stage D ЕТАП 13)
        services.AddScoped<Features.MobileConfig.IMobileConfigPreviewService, Features.MobileConfig.MobileConfigPreviewService>();

        // TASK-616 - Consumer↔tenant support tickets (async channel, mirrors SupplierSupportService)
        services.AddScoped<Features.CustomerSupport.IConsumerSupportService, Features.CustomerSupport.ConsumerSupportService>();

        // TASK-617 - Consumer purchase reviews (rating + comment on a PosTransaction, one staff reply)
        services.AddScoped<Features.Reviews.IReviewService, Features.Reviews.ReviewService>();

        // TASK-648 - Geo taxonomy (UkraineRegions registry -> GET /api/geo/regions)
        services.AddScoped<IGeoService, GeoService>();

        return services;
    }
}
