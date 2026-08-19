using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShelfGuard.Application;
using ShelfGuard.Application.Features.Catalog;
using ShelfGuard.Application.Features.Customers;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Tools.PchilkaImport;
using ShelfGuard.Tools.PchilkaImport.Source;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddApplication();
services.AddInfrastructure(configuration);
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();

var options = ImportOptions.FromConfiguration(configuration);

var runner = new ImportRunner(
    scope.ServiceProvider.GetRequiredService<AppDbContext>(),
    scope.ServiceProvider.GetRequiredService<ITenantSessionOverride>(),
    scope.ServiceProvider.GetRequiredService<IItemService>(),
    scope.ServiceProvider.GetRequiredService<ICustomerService>(),
    new PchilkaSourceReader(),
    options);

try
{
    await runner.RunAsync(CancellationToken.None);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"FAILED: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    var inner = ex.InnerException;
    while (inner is not null)
    {
        Console.Error.WriteLine($"  ---> {inner.GetType().FullName}: {inner.Message}");
        inner = inner.InnerException;
    }
    return 1;
}
