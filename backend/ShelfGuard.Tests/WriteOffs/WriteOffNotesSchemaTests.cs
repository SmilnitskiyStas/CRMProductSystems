using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Migrations;
using Xunit;

namespace ShelfGuard.Tests.WriteOffs;

public sealed class WriteOffNotesSchemaTests
{
    [Fact]
    public void Migration_OnlyAddsNullableBoundedExplanation()
    {
        var operation = Assert.IsType<AddColumnOperation>(Assert.Single(new AddWriteOffNotes().UpOperations));
        Assert.Equal("write_offs", operation.Table);
        Assert.Equal("Notes", operation.Name);
        Assert.Equal(100, operation.MaxLength);
        Assert.True(operation.IsNullable);
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused;Password=unused").Options);
        var sql = db.GetService<IMigrator>().GenerateScript("20261023165833_EnforceAccessMatrix", "20261024140000_AddWriteOffNotes");
        Assert.Contains("ADD \"Notes\" character varying(100)", sql);
        Assert.DoesNotContain("DELETE FROM write_offs", sql);
    }

    [Fact]
    public async Task PostgreSql_ExistingRowsSurvive_AndExplanationRoundTrips()
    {
        var connection = Environment.GetEnvironmentVariable("SHELFGUARD_WRITE_OFF_TEST_DB");
        if (string.IsNullOrEmpty(connection)) return;
        var settings = new NpgsqlConnectionStringBuilder(connection);
        Assert.Contains(settings.Host, new[] { "127.0.0.1", "localhost" });
        Assert.StartsWith("codex_writeoff_notes_", settings.Database);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE write_offs (\"Id\" uuid PRIMARY KEY)");
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO write_offs (\"Id\") VALUES ({id})");
        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(new AddWriteOffNotes().UpOperations);
        foreach (var command in commands) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        Assert.Null(await db.WriteOffs.Where(w => w.Id == id).Select(w => w.Notes).SingleAsync());
        var explanation = "Пошкоджено пакування товару";
        await db.WriteOffs.Where(w => w.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(w => w.Notes, explanation));
        Assert.Equal(explanation, await db.WriteOffs.Where(w => w.Id == id).Select(w => w.Notes).SingleAsync());
        await Assert.ThrowsAsync<PostgresException>(() => db.WriteOffs.Where(w => w.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(w => w.Notes, new string('а', 101))));
        Assert.Equal(explanation, await db.WriteOffs.Where(w => w.Id == id).Select(w => w.Notes).SingleAsync());
    }
}
