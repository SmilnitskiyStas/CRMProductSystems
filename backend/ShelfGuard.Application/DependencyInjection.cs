using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Application.Features.Catalog;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Application.Features.Stores;
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
using Microsoft.Extensions.DependencyInjection;

namespace ShelfGuard.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICatalogProductService, CatalogProductService>();
        services.AddScoped<IStockService, StockService>();
        services.AddScoped<IStoreService, StoreService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<IReceiptService, ReceiptService>();
        services.AddScoped<ITransferService, TransferService>();
        services.AddScoped<IWriteOffService, WriteOffService>();
        services.AddScoped<IAnalyticsService, AnalyticsService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IIntegrationService, IntegrationService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IDiscountService, DiscountService>();
        services.AddScoped<IProviderService, ProviderService>();
        services.AddScoped<IMovementService, MovementService>();
        return services;
    }
}
