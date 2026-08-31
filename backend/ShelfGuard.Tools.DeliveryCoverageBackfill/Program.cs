using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShelfGuard.Application;
using ShelfGuard.Infrastructure;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Tools.DeliveryCoverageBackfill;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(
        """
        TASK-661 (T14) — supplier_profiles.DeliveryRegions -> DeliveryCoverage backfill

          dotnet run --project backend/ShelfGuard.Tools.DeliveryCoverageBackfill [-- --apply]

        Options:
          --apply    Persist the changes. Without it the tool runs a DRY RUN (computes and
                     prints every change, then rolls back — no writes).
          --help     Show this text.

        Idempotent: only rows with DeliveryCoverage IS NULL and a non-empty DeliveryRegions
        are considered, so re-running after a partial/aborted run is safe.

        Connection: ConnectionStrings:DefaultConnection in appsettings.json, override with the
        env var ConnectionStrings__DefaultConnection. Must be the non-superuser app role — the
        tool sets 'app.role = provider' itself (SET LOCAL, one transaction) for the
        cross-tenant read+write on supplier_profiles.
        """);
    return 0;
}

var apply = args.Contains("--apply");

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

var runner = new BackfillRunner(scope.ServiceProvider.GetRequiredService<AppDbContext>());

try
{
    return await runner.RunAsync(apply, CancellationToken.None);
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
