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
using ShelfGuard.Application.Features.Support;
using ShelfGuard.Application.Features.Movements;
using ShelfGuard.Application.Features.Admin;
using ShelfGuard.Application.Features.Settings;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.AutoService;
using ShelfGuard.Application.Features.Production;
using ShelfGuard.Application.Features.AiAssistant;
using ShelfGuard.Application.Features.Customers;
using ShelfGuard.Application.Features.Schedules;
using Microsoft.Extensions.DependencyInjection;

namespace ShelfGuard.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IItemService, ItemService>();
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
        services.AddScoped<IProviderService, ProviderService>();
        services.AddScoped<IProviderTeamService, ProviderTeamService>();
        services.AddScoped<ISupportService, SupportService>();
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

        return services;
    }
}
