using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShelfGuard.Domain.Entities.Erp;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Migrations;
using Xunit;

namespace ShelfGuard.Tests.Erp;

public sealed class ErpSchemaModelTests
{
    [Fact]
    public void Delivered_SQL_artifact_matches_the_migration_and_contains_only_the_W2_range()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=not_connected").Options);
        var generated = db.GetService<IMigrator>().GenerateScript("20261024210000_AddIotMessageReceipts",
            "20261024220000_AddErpParallelSchema", MigrationsSqlGenerationOptions.Idempotent);
        var artifact = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "erp-w2.sql"));
        Assert.Equal(generated.Replace("\r\n", "\n").Trim(), artifact.Replace("\r\n", "\n").Trim());
    }

    [Fact]
    public void W2_only_adds_tables_keys_indexes_and_guard_SQL_and_preserves_history_on_downgrade()
    {
        var migration = new AddErpParallelSchema();
        Assert.Equal(11, migration.UpOperations.OfType<CreateTableOperation>().Count());
        Assert.Equal(5, migration.UpOperations.OfType<AddUniqueConstraintOperation>().Count());
        Assert.All(migration.UpOperations, operation => Assert.True(operation is CreateTableOperation
            or CreateIndexOperation or AddUniqueConstraintOperation or SqlOperation, operation.GetType().Name));
        Assert.Throws<NotSupportedException>(() => _ = migration.DownOperations);
    }

    [Fact]
    public void Current_model_and_snapshot_match_and_all_ERP_references_restrict_cascade_deletion()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=not_connected").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var entities = db.Model.GetEntityTypes().Where(e => e.ClrType.Namespace == typeof(BusinessDocument).Namespace).ToList();
        Assert.Equal(23, entities.Count); // Previous 16 plus P4-B's two, P4-E's four and P4-F's one label table.
        foreach (var entity in entities)
        {
            Assert.False(entity.FindProperty("TenantId")!.IsNullable);
            Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        }
        Assert.Contains(db.Model.FindEntityType(typeof(DocumentPosting))!.GetIndexes(), i => i.IsUnique
            && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "DocumentId" }));
        Assert.Contains(db.Model.FindEntityType(typeof(OutboxEvent))!.GetIndexes(), i => i.IsUnique
            && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "PostingId" }));
    }
}
