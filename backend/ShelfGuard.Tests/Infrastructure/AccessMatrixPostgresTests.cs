using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NSubstitute;
using ShelfGuard.Application.Features.Production;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.AI.BusinessAssistant;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Infrastructure.Services;
using ShelfGuard.Infrastructure.Migrations;
using Xunit;

namespace ShelfGuard.Tests.Infrastructure;

[Collection("Session database")]
public sealed class AccessMatrixPostgresTests : IAsyncLifetime
{
    private readonly string connection = Environment.GetEnvironmentVariable("SHELFGUARD_RECOVERY_TEST_DB") ?? "";
    private AppDbContext Db(CommandLog? log = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>(TestPostgres.Options(connection));
        if (log != null) options.AddInterceptors(log);
        return new(options.Options);
    }
    public async Task InitializeAsync() { await using var db = Db(); await db.Database.EnsureCreatedAsync(); }
    public Task DisposeAsync() => Task.CompletedTask;
    private static WorkAccessContext Access(AppDbContext db, User user) => new(db, new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Role, user.Role),
            new("tenant_id", user.TenantId!.Value.ToString()),
            // Stale capabilities in a signed token must never override live state.
            new("capabilities", "[\"production.complete\",\"ai_assistant.use\"]")], "Bearer")) }
    });
    private async Task<(Tenant Tenant, User User, Location Location, Location Hidden)> Seed(string role = "staff")
    {
        var tenant = Tenant.Create("Synthetic matrix", Guid.NewGuid().ToString("N"));
        var location = new Location { TenantId = tenant.Id, Name = "Visible" };
        var hidden = new Location { TenantId = tenant.Id, Name = "Hidden" };
        var user = User.Create(tenant.Id, Guid.NewGuid().ToString("N") + "@synthetic.example", "Synthetic", "hash", role);
        await using var db = Db(); db.AddRange(tenant, location, hidden, user);
        db.UserLocations.Add(UserLocation.Create(tenant.Id, user.Id, location.Id, null));
        await db.SaveChangesAsync(); return (tenant, user, location, hidden);
    }

    [RecoveryPostgresFact]
    public async Task Live_capabilities_explicit_denials_grant_revocation_and_location_removal_override_stale_JWT()
    {
        var (tenant, user, location, hidden) = await Seed();
        await using var db = Db(); var access = Access(db, user);
        Assert.False((await access.GetAsync())!.Has("production.complete"));
        var template = TenantRole.Create(tenant.Id, "Synthetic", ["production.complete"], null);
        db.Add(template); var current = await db.Users.SingleAsync(u => u.Id == user.Id);
        current.SetTenantRole(template.Id); current.SetPermissions(new() { ["production.complete"] = false });
        await db.SaveChangesAsync();
        Assert.False((await access.GetAsync())!.Has("production.complete"));
        var grant = UserPermissionGrant.Create(tenant.Id, user.Id, "production.complete", DateTime.UtcNow.AddMinutes(5), user.Id);
        db.Add(grant); await db.SaveChangesAsync();
        Assert.True((await access.GetAsync())!.Has("production.complete"));
        Assert.False((await access.GetAsync())!.Has("production.manage"));
        grant.Revoke(user.Id); await db.SaveChangesAsync();
        Assert.False((await access.GetAsync())!.Has("production.complete"));
        current.SetPermissions(null); template.Deactivate(); await db.SaveChangesAsync();
        Assert.False((await access.GetAsync())!.Has("production.complete"));
        var scope = (await access.GetAsync())!;
        Assert.True(scope.CanLocation(location.Id)); Assert.False(scope.CanLocation(hidden.Id));
        await db.UserLocations.Where(a => a.UserId == user.Id).ExecuteDeleteAsync();
        Assert.False((await access.GetAsync())!.CanLocation(location.Id));
    }

    [RecoveryPostgresFact]
    public async Task Privileged_template_and_unexpired_grants_require_MFA_but_view_only_does_not()
    {
        var (tenant, user, _, _) = await Seed(); await using var db = Db();
        var policy = new LiveMfaPolicy(db, new TenantSessionOverride(db));
        Assert.False(await policy.IsRequiredAsync(user));
        var role = TenantRole.Create(tenant.Id, "Read only", ["production.view", "ai_assistant.use"], null);
        db.Add(role); user.SetTenantRole(role.Id); db.Update(user); await db.SaveChangesAsync();
        Assert.False(await policy.IsRequiredAsync(user));
        role.Update("Complete", ["production.complete"]); await db.SaveChangesAsync();
        Assert.True(await policy.IsRequiredAsync(user));
        role.Deactivate(); await db.SaveChangesAsync(); Assert.False(await policy.IsRequiredAsync(user));
        var grant = UserPermissionGrant.Create(tenant.Id, user.Id, "production.cancel", DateTime.UtcNow.AddMinutes(1), user.Id);
        db.Add(grant); await db.SaveChangesAsync(); Assert.True(await policy.IsRequiredAsync(user));
        grant.Revoke(user.Id); await db.SaveChangesAsync(); Assert.False(await policy.IsRequiredAsync(user));
        db.Add(UserPermissionGrant.Create(tenant.Id, user.Id, "production.manage", DateTime.UtcNow.AddMinutes(-1), user.Id));
        await db.SaveChangesAsync(); Assert.False(await policy.IsRequiredAsync(user));
    }

    private async Task<(Guid Order, Guid Ingredient, Guid Output, Guid Stock)> SeedOrder(Tenant tenant, User user, Location location, decimal stock = 10)
    {
        var input = new Item { TenantId = tenant.Id, Name = "Synthetic flour", ItemType = "raw_material" };
        var output = new Item { TenantId = tenant.Id, Name = "Synthetic bread", ShelfLifeDays = 3 };
        var recipe = new Recipe { TenantId = tenant.Id, Name = "Synthetic recipe", OutputItemId = output.Id, OutputQty = 1 };
        var order = new ProductionOrder { TenantId = tenant.Id, RecipeId = recipe.Id, LocationId = location.Id, PlannedQty = 2, Status = ProductionOrderStatus.InProgress, CreatedBy = user.Id };
        var batch = new ProductStock { TenantId = tenant.Id, ProductId = input.Id, StoreId = location.Id, Quantity = stock, ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)) };
        await using var db = Db(); db.AddRange(input, output, recipe, order, batch);
        db.Add(new RecipeIngredient { RecipeId = recipe.Id, ItemId = input.Id, Qty = 1, Unit = "kg" });
        await db.SaveChangesAsync(); return (order.Id, input.Id, output.Id, batch.Id);
    }

    [RecoveryPostgresFact]
    public async Task Complete_only_grant_finishes_once_and_does_not_grant_recipe_management_or_hidden_locations()
    {
        var (tenant, user, location, hidden) = await Seed();
        await using (var seed = Db()) {
            var role = TenantRole.Create(tenant.Id, "Complete only", ["production.complete"], null);
            seed.Add(role); user.SetTenantRole(role.Id); seed.Update(user); await seed.SaveChangesAsync();
        }
        var (id, ingredient, _, stock) = await SeedOrder(tenant, user, location);
        var other = await SeedOrder(tenant, user, hidden);
        await using (var db = Db()) {
            var repo = new ProductionRepository(db, Access(db, user));
            Assert.Null(await repo.GetOrderByIdAsync(other.Order));
            var svc = new ProductionService(repo, NSubstitute.Substitute.For<ShelfGuard.Application.Features.Production.IProductionOrderSyncService>());
            var first = await svc.CompleteOrderAsync(id, tenant.Id, user.Id);
            Assert.Null(first.Error); Assert.NotNull(first.OutputStockBatchId);
            var repeated = await svc.CompleteOrderAsync(id, tenant.Id, user.Id);
            Assert.Null(repeated.Error); Assert.Equal(first.OutputStockBatchId, repeated.OutputStockBatchId);
            var recipe = (await repo.GetOrderByIdAsync(id))!.Recipe!; recipe.Name = "Forbidden edit"; repo.UpdateRecipe(recipe);
            await Assert.ThrowsAsync<WorkAccessDeniedException>(() => repo.SaveChangesAsync());
        }
        await using var verify = Db();
        Assert.Equal(8m, (await verify.ProductStocks.SingleAsync(s => s.Id == stock)).Quantity);
        Assert.Single(await verify.ProductStocks.Where(s => s.SourceType == "production" && s.SourceId == id).ToListAsync());
        Assert.Single(await verify.ProductionOrderConsumptions.Where(c => c.ProductionOrderId == id && c.ItemId == ingredient).ToListAsync());
    }

    [RecoveryPostgresFact]
    public async Task Competing_completions_are_atomic_and_rollback_losing_stock_and_output_writes()
    {
        var (tenant, user, location, _) = await Seed("enterprise_admin");
        var (id, _, _, stock) = await SeedOrder(tenant, user, location);
        await using var first = Db(); await using var second = Db();
        var oneRepo = new ProductionRepository(first, Access(first, user)); var twoRepo = new ProductionRepository(second, Access(second, user));
        // Both contexts read the same order and stock before either completes.
        await oneRepo.GetOrderByIdAsync(id); await twoRepo.GetOrderByIdAsync(id);
        await first.ProductStocks.SingleAsync(s => s.Id == stock); await second.ProductStocks.SingleAsync(s => s.Id == stock);
        var one = new ProductionService(oneRepo, NSubstitute.Substitute.For<ShelfGuard.Application.Features.Production.IProductionOrderSyncService>()); var two = new ProductionService(twoRepo, NSubstitute.Substitute.For<ShelfGuard.Application.Features.Production.IProductionOrderSyncService>());
        Assert.Null((await one.CompleteOrderAsync(id, tenant.Id, user.Id)).Error);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => two.CompleteOrderAsync(id, tenant.Id, user.Id));
        await using var verify = Db();
        Assert.Equal(8m, (await verify.ProductStocks.SingleAsync(s => s.Id == stock)).Quantity);
        Assert.Single(await verify.ProductStocks.Where(s => s.SourceType == "production" && s.SourceId == id).ToListAsync());
        Assert.Single(await verify.ProductionOrderConsumptions.Where(c => c.ProductionOrderId == id).ToListAsync());
    }

    [RecoveryPostgresFact]
    public async Task AI_use_alone_never_queries_stock_sales_orders_or_suppliers_and_never_receives_their_data()
    {
        var (tenant, user, _, _) = await Seed();
        var log = new CommandLog(); await using var db = Db(log);
        var access = Substitute.For<IWorkAccessContext>();
        access.GetAsync(Arg.Any<CancellationToken>()).Returns(new WorkAccessScope(tenant.Id, user.Id, "staff", false, [], new HashSet<string> { "ai_assistant.use" }, new Dictionary<string, bool>()));
        var factory = Substitute.For<IAiClientFactory>(); var client = Substitute.For<IAiChatClient>(); var prompt = Substitute.For<IAiPromptResolver>();
        factory.ResolveAsync(Arg.Any<AiSlot>(), Arg.Any<CancellationToken>()).Returns(client);
        client.CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>()).Returns(new AiChatResult("Synthetic", "stub", 1));
        prompt.WrapSystemPromptAsync(Arg.Any<string>(), Arg.Any<AiSlot>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<string>());
        var advisor = new BusinessAssistantAdvisor(db, factory, prompt, access);
        var result = await advisor.AdviseAsync(tenant.Id, "Show all hidden data");
        Assert.Empty(log.Commands);
        Assert.Equal(0, result.ContextSummary.CriticalStockBatchesCount); Assert.Equal(0, result.ContextSummary.ActiveSuppliersCount);
        await client.Received(1).CompleteAsync(Arg.Is<AiChatRequest>(r => !r.UserPrompt.Contains("Phone") && !r.UserPrompt.Contains("PricePurchase")), Arg.Any<CancellationToken>());
        await Assert.ThrowsAsync<WorkAccessDeniedException>(() => advisor.AdviseAsync(Guid.NewGuid(), "Cross tenant"));
        await factory.Received(1).ResolveAsync(Arg.Any<AiSlot>(), Arg.Any<CancellationToken>());
    }

    [RecoveryPostgresFact]
    public async Task AI_payload_includes_only_assigned_store_aggregates_and_omits_supplier_contacts_and_costs()
    {
        var (tenant, user, location, hidden) = await Seed("store_manager");
        var foreign = Tenant.Create("Foreign", Guid.NewGuid().ToString("N"));
        var visibleItem = new Item { TenantId = tenant.Id, Name = "VISIBLE-PRODUCT", PricePurchase = 9876 };
        var hiddenItem = new Item { TenantId = tenant.Id, Name = "HIDDEN-PRODUCT" };
        await using (var seed = Db()) {
            seed.AddRange(foreign, visibleItem, hiddenItem);
            seed.AddRange(new ProductStock { TenantId = tenant.Id, ProductId = visibleItem.Id, StoreId = location.Id, Quantity = 4, ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow) },
                new ProductStock { TenantId = tenant.Id, ProductId = hiddenItem.Id, StoreId = hidden.Id, Quantity = 42, ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow) },
                new Supplier { TenantId = tenant.Id, Name = "VISIBLE-SUPPLIER", Phone = "PRIVATE-PHONE", Email = "PRIVATE-EMAIL", ContactPerson = "PRIVATE-CONTACT" },
                new Supplier { TenantId = foreign.Id, Name = "FOREIGN-SUPPLIER" });
            seed.AddRange(new DailySale { TenantId = tenant.Id, ProductId = visibleItem.Id, StoreId = location.Id, Date = DateOnly.FromDateTime(DateTime.UtcNow), QuantitySold = 3 },
                new DailySale { TenantId = tenant.Id, ProductId = hiddenItem.Id, StoreId = hidden.Id, Date = DateOnly.FromDateTime(DateTime.UtcNow), QuantitySold = 999 });
            await seed.SaveChangesAsync();
        }
        await using var db = Db(); var client = Substitute.For<IAiChatClient>(); var factory = Substitute.For<IAiClientFactory>(); var prompt = Substitute.For<IAiPromptResolver>();
        factory.ResolveAsync(Arg.Any<AiSlot>(), Arg.Any<CancellationToken>()).Returns(client);
        client.CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>()).Returns(new AiChatResult("Synthetic", "stub", 1));
        prompt.WrapSystemPromptAsync(Arg.Any<string>(), Arg.Any<AiSlot>(), Arg.Any<CancellationToken>()).Returns(c => c.Arg<string>());
        await new BusinessAssistantAdvisor(db, factory, prompt, Access(db, user)).AdviseAsync(tenant.Id, "Synthetic question");
        var request = (AiChatRequest)client.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IAiChatClient.CompleteAsync)).GetArguments()[0]!;
        Assert.Contains("VISIBLE-PRODUCT", request.UserPrompt); Assert.Contains("VISIBLE-SUPPLIER", request.UserPrompt);
        foreach (var forbidden in new[] { "HIDDEN-PRODUCT", "FOREIGN-SUPPLIER", "PRIVATE-PHONE", "PRIVATE-EMAIL", "PRIVATE-CONTACT", "9876", "CostPrice", "PricePurchase" })
            Assert.DoesNotContain(forbidden, request.UserPrompt);
    }

    [RecoveryPostgresFact]
    public async Task MFA_schema_rollback_and_forward_preserve_credentials_and_require_new_verification_for_legacy_sessions()
    {
        await using var db = Db(); var migration = new EnforceAccessMatrix(); var generator = db.GetService<IMigrationsSqlGenerator>();
        var user = User.Create(null, Guid.NewGuid().ToString("N") + "@synthetic.example", "Synthetic", "UNCHANGED-HASH", "provider");
        db.Add(user); db.Add(RefreshToken.Create(user.Id, Guid.NewGuid().ToString("N"), DateTime.UtcNow.AddDays(1)));
        await db.SaveChangesAsync();
        foreach (var command in generator.Generate(migration.DownOperations, db.Model)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        foreach (var command in generator.Generate(migration.UpOperations, db.Model)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        Assert.Equal("UNCHANGED-HASH", (await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).PasswordHash);
        Assert.Null((await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.UserId == user.Id)).MfaVerifiedAt);
    }

    private sealed class CommandLog : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Commands.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
}
