using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Migrations;
using Xunit;

namespace ShelfGuard.Tests.Erp;

public sealed class InventoryFenceMigrationTests
{
    [Fact]
    public void Integrated_SQL_matches_migration_and_keeps_quality_and_POS_primary_guards_on_downgrade()
    {
        using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=not_connected").Options);
        var generated=db.GetService<IMigrator>().GenerateScript("20261027181539_AddErpIotPosting","20261027193717_AddIntegratedInventoryReadiness",MigrationsSqlGenerationOptions.Idempotent);
        var artifact=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","erp-w4k.sql"));Assert.Equal(generated.Replace("\r\n","\n").Trim(),artifact.Replace("\r\n","\n").Trim());
        Assert.Throws<NotSupportedException>(()=>_=new AddIntegratedInventoryReadiness().DownOperations);
    }
    [Fact]
    public void Delivered_IoT_SQL_matches_migration_and_retains_machine_authorization_and_audit()
    {
        using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=not_connected").Options);
        var generated=db.GetService<IMigrator>().GenerateScript("20261027173037_AddErpProductionPosting","20261027181539_AddErpIotPosting",MigrationsSqlGenerationOptions.Idempotent);
        var artifact=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","erp-w4j.sql"));
        Assert.Equal(generated.Replace("\r\n","\n").Trim(),artifact.Replace("\r\n","\n").Trim());
        Assert.Throws<NotSupportedException>(()=>_=new AddErpIotPosting().DownOperations);
    }
    [Fact]
    public void Delivered_production_SQL_matches_migration_and_downgrade_retains_source_audit()
    {
        using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=not_connected").Options);
        var generated=db.GetService<IMigrator>().GenerateScript("20261027114758_AddErpPosReturnPosting","20261027173037_AddErpProductionPosting",MigrationsSqlGenerationOptions.Idempotent);
        var artifact=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","erp-w4i2.sql"));
        Assert.Equal(generated.Replace("\r\n","\n").Trim(),artifact.Replace("\r\n","\n").Trim());
        Assert.Throws<NotSupportedException>(()=>_=new AddErpProductionPosting().DownOperations);
    }
    [Fact]
    public void Delivered_POS_return_SQL_matches_migration_and_downgrade_retains_compensation()
    {
        using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=not_connected").Options);
        var generated=db.GetService<IMigrator>().GenerateScript("20261027090000_AddErpPosSalePosting","20261027114758_AddErpPosReturnPosting",MigrationsSqlGenerationOptions.Idempotent);
        var artifact=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","erp-w4h2.sql"));
        Assert.Equal(generated.Replace("\r\n","\n").Trim(),artifact.Replace("\r\n","\n").Trim());
        Assert.Throws<NotSupportedException>(()=>_ =new AddErpPosReturnPosting().DownOperations);
    }
    [Fact]
    public void Delivered_POS_SQL_matches_additive_migration_and_downgrade_retains_receipts()
    {
        using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=not_connected").Options);
        var generated=db.GetService<IMigrator>().GenerateScript("20261026190000_AddErpWriteOffPosting","20261027090000_AddErpPosSalePosting",MigrationsSqlGenerationOptions.Idempotent);
        var artifact=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","erp-w4h.sql"));
        Assert.Equal(generated.Replace("\r\n","\n").Trim(),artifact.Replace("\r\n","\n").Trim());
        Assert.Throws<NotSupportedException>(()=>_ =new AddErpPosSalePosting().DownOperations);
    }
    [Fact]
    public void Delivered_writeoff_SQL_matches_additive_migration_and_downgrade_retains_decisions()
    {
        using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=not_connected").Options);
        var generated=db.GetService<IMigrator>().GenerateScript("20261026170000_AddErpTransferPosting","20261026190000_AddErpWriteOffPosting",MigrationsSqlGenerationOptions.Idempotent);
        var artifact=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","erp-w4g.sql"));
        Assert.Equal(generated.Replace("\r\n","\n").Trim(),artifact.Replace("\r\n","\n").Trim());
        Assert.Throws<NotSupportedException>(()=>_ =new AddErpWriteOffPosting().DownOperations);
    }
    [Fact]
    public void Delivered_transfer_SQL_matches_additive_migration_and_downgrade_retains_postings()
    {
        using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=not_connected").Options);
        var generated=db.GetService<IMigrator>().GenerateScript("20261026120000_AddErpReceiptPosting","20261026170000_AddErpTransferPosting",MigrationsSqlGenerationOptions.Idempotent);
        var artifact=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","erp-w4f.sql"));
        Assert.Equal(generated.Replace("\r\n","\n").Trim(),artifact.Replace("\r\n","\n").Trim());
        Assert.Throws<NotSupportedException>(()=>_ =new AddErpTransferPosting().DownOperations);
    }
    [Fact]
    public void Delivered_receiving_SQL_matches_its_additive_migration_and_retains_fences_on_downgrade()
    {
        using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=not_connected").Options);
        var generated=db.GetService<IMigrator>().GenerateScript("20261026070311_AddInventoryPostingScopeFence","20261026120000_AddErpReceiptPosting",MigrationsSqlGenerationOptions.Idempotent);
        var artifact=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","erp-w4d.sql"));
        Assert.Equal(generated.Replace("\r\n","\n").Trim(),artifact.Replace("\r\n","\n").Trim());
        Assert.Throws<NotSupportedException>(()=>_ = new AddErpReceiptPosting().DownOperations);
    }
    [Fact]
    public void Delivered_fence_SQL_is_the_exact_idempotent_migration_and_downgrade_preserves_audit()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=not_connected").Options);
        var generated = db.GetService<IMigrator>().GenerateScript("20261024220000_AddErpParallelSchema",
            "20261026070311_AddInventoryPostingScopeFence", MigrationsSqlGenerationOptions.Idempotent);
        var artifact = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "erp-w4b.sql"));
        Assert.Equal(generated.Replace("\r\n", "\n").Trim(), artifact.Replace("\r\n", "\n").Trim());
        Assert.Throws<NotSupportedException>(() => _ = new AddInventoryPostingScopeFence().DownOperations);
    }
}
