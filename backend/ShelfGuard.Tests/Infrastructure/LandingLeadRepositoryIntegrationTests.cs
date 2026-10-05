using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ShelfGuard.Application.Features.Leads;
using ShelfGuard.Application.Features.Leads.Dtos;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-721: live-Postgres flow for landing leads: capture, list (status filter + ILIKE search),
/// mark processed, count. Skips when no Postgres is reachable.
/// </summary>
public sealed class LandingLeadRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private string _connectionString = TestPostgres.DefaultConnectionString;
    private bool _dbAvailable;
    private readonly string _run = Guid.NewGuid().ToString("N")[..10];

    public LandingLeadRepositoryIntegrationTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        _connectionString = TestPostgres.ResolveConnectionString();
        try
        {
            await using var probe = new NpgsqlConnection(_connectionString);
            await probe.OpenAsync();
            _dbAvailable = true;
        }
        catch (Exception ex)
        {
            _dbAvailable = false;
            _output.WriteLine($"Skipping landing-lead integration tests, no reachable Postgres: {ex.Message}");
        }
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;
        await using var db = NewContext();
        var like = "%" + _run + "%";
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM landing_leads WHERE \"Name\" LIKE {like}");
    }

    [Fact]
    public async Task Capture_List_MarkProcessed_Count_Flow()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available, skipped."); return; }

        await using var db = NewContext();
        var service = new LandingLeadService(new LandingLeadRepository(db), NullLogger<LandingLeadService>.Instance);

        var name = "Lead " + _run;
        var error = await service.CaptureAsync(
            new CaptureLeadRequest(name, "+380671234567", "Acme " + _run, "hi", null,
                Source: "retail", PageUrl: "/uk/retail", Locale: "uk", UtmSource: "google"),
            CancellationToken.None);
        Assert.Null(error);

        var before = (await service.CountUnprocessedAsync(CancellationToken.None)).Unprocessed;
        Assert.True(before >= 1);

        var found = await service.ListAsync("new", _run, 1, 20, CancellationToken.None);
        var dto = Assert.Single(found.Items);
        Assert.Equal("retail", dto.Source);
        Assert.Equal("/uk/retail", dto.PageUrl);
        Assert.Equal("google", dto.UtmSource);
        Assert.False(dto.IsProcessed);

        var actor = Guid.NewGuid();
        var (updated, updErr) = await service.UpdateAsync(
            dto.Id, new UpdateLeadRequest(true, "called"), actor, CancellationToken.None);
        Assert.Null(updErr);
        Assert.True(updated!.IsProcessed);

        Assert.Empty((await service.ListAsync("new", _run, 1, 20, CancellationToken.None)).Items);
        var processed = Assert.Single((await service.ListAsync("processed", _run, 1, 20, CancellationToken.None)).Items);
        Assert.Equal("called", processed.AdminNote);
        Assert.Equal(actor, processed.ProcessedByUserId);
        Assert.Equal(before - 1, (await service.CountUnprocessedAsync(CancellationToken.None)).Unprocessed);
    }

    private AppDbContext NewContext() => TestPostgres.NewContext(_connectionString);
}
