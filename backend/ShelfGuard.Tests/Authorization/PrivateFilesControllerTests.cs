using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Api.Infrastructure;
using ShelfGuard.Application.Common;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using Xunit;

namespace ShelfGuard.Tests.Authorization;

public sealed class PrivateFilesControllerTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly IAuthorizationService _authorization = Substitute.For<IAuthorizationService>();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "private-assets-test-" + Guid.NewGuid());
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _location = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();

    public PrivateFilesControllerTests()
    {
        _authorization.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Any<string>())
            .Returns(AuthorizationResult.Success());
        _db.Locations.Add(new Location { Id = _location, TenantId = _tenant, Name = "Synthetic store" });
        _db.SaveChanges();
    }

    private PrivateFilesController Controller(Guid? tenant = null, string role = "store_manager", bool authenticated = true)
    {
        var claims = new[] { new Claim("tenant_id", (tenant ?? _tenant).ToString()),
            new Claim(ClaimTypes.NameIdentifier, _user.ToString()), new Claim(ClaimTypes.Role, role) };
        return new PrivateFilesController(_db, _authorization)
        {
            StorageRoot = Path.Combine(_root, "private"), LegacyRoot = Path.Combine(_root, "legacy"),
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            { User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "synthetic" : null)) } },
        };
    }

    private string AddScan(bool legacy = false, Guid? location = null)
    {
        var name = Guid.NewGuid() + ".jpg";
        _db.ShelfScans.Add(new ShelfScan { TenantId = _tenant, LocationId = location ?? _location,
            ZoneId = Guid.NewGuid(), ImagePath = $"/uploads/shelf-scans/{_tenant}/{name}" });
        _db.SaveChanges();
        WriteFile("shelf-scans", name, legacy);
        return name;
    }

    private void WriteFile(string category, string name, bool legacy = false)
    {
        var folder = Path.Combine(_root, legacy ? "legacy" : "private", category, _tenant.ToString());
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name), [0xFF, 0xD8, 0xFF]);
    }

    private void AssignLocation()
    {
        _db.UserLocations.Add(UserLocation.Create(_tenant, _user, _location, null));
        _db.SaveChanges();
    }

    [Fact]
    public async Task Anonymous_and_foreign_tenant_cannot_read_known_private_path()
    {
        var name = AddScan();
        Assert.IsType<UnauthorizedResult>(await Controller(authenticated: false).Get("shelf-scans", _tenant, name, default));
        Assert.IsType<NotFoundResult>(await Controller(Guid.NewGuid()).Get("shelf-scans", _tenant, name, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Assigned_staff_can_read_private_and_legacy_scan_but_unassigned_staff_cannot(bool legacy)
    {
        var name = AddScan(legacy);
        Assert.IsType<NotFoundResult>(await Controller().Get("shelf-scans", _tenant, name, default));
        AssignLocation();
        var controller = Controller();
        var result = Assert.IsType<PhysicalFileResult>(await controller.Get("shelf-scans", _tenant, name, default));
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.StartsWith(Path.GetFullPath(_root), result.FileName);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task Home_store_claim_does_not_replace_live_location_assignment()
    {
        var name = AddScan();
        var controller = Controller();
        ((ClaimsIdentity)controller.User.Identity!).AddClaim(new Claim("store_id", _location.ToString()));
        Assert.IsType<NotFoundResult>(await controller.Get("shelf-scans", _tenant, name, default));
    }

    [Fact]
    public async Task Enterprise_admin_can_read_own_tenant_scan_without_assignment()
    {
        Assert.IsType<PhysicalFileResult>(await Controller(role: "enterprise_admin")
            .Get("shelf-scans", _tenant, AddScan(), default));
    }

    [Fact]
    public async Task Mismatched_scan_location_tenant_is_denied_even_for_enterprise_admin()
    {
        var otherLocation = Guid.NewGuid();
        _db.Locations.Add(new Location { Id = otherLocation, TenantId = Guid.NewGuid() });
        _db.SaveChanges();
        Assert.IsType<NotFoundResult>(await Controller(role: "enterprise_admin")
            .Get("shelf-scans", _tenant, AddScan(location: otherLocation), default));
    }

    [Fact]
    public async Task Revoked_assignment_removes_file_access()
    {
        var name = AddScan();
        AssignLocation();
        Assert.IsType<PhysicalFileResult>(await Controller().Get("shelf-scans", _tenant, name, default));
        _db.UserLocations.RemoveRange(_db.UserLocations);
        await _db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await Controller().Get("shelf-scans", _tenant, name, default));
    }

    [Fact]
    public async Task Role_policy_is_required_even_for_an_assigned_user()
    {
        AssignLocation();
        var name = AddScan();
        _authorization.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Any<string>())
            .Returns(AuthorizationResult.Failed());
        Assert.IsType<ForbidResult>(await Controller(role: "cashier").Get("shelf-scans", _tenant, name, default));
    }

    [Fact]
    public async Task Floor_plan_upload_is_bound_to_location_before_layout_save()
    {
        var name = $"{_location}_{Guid.NewGuid()}.png";
        WriteFile("floor-plans", name);
        Assert.IsType<NotFoundResult>(await Controller().Get("floor-plans", _tenant, name, default));
        AssignLocation();
        Assert.IsType<PhysicalFileResult>(await Controller().Get("floor-plans", _tenant, name, default));
    }

    [Fact]
    public async Task Legacy_floor_plan_requires_a_reference_in_an_authorized_layout()
    {
        var name = Guid.NewGuid() + ".png";
        WriteFile("floor-plans", name, legacy: true);
        AssignLocation();
        Assert.IsType<NotFoundResult>(await Controller().Get("floor-plans", _tenant, name, default));
        (await _db.Locations.FindAsync(_location))!.FloorPlan = JsonSerializer.Serialize(new
        { background = new { url = $"/uploads/floor-plans/{_tenant}/{name}" } });
        await _db.SaveChangesAsync();
        Assert.IsType<PhysicalFileResult>(await Controller().Get("floor-plans", _tenant, name, default));
    }

    [Fact]
    public async Task Supplier_asset_requires_an_own_settings_reference_and_contracts_use_party_download_api()
    {
        WriteFile("contract-assets", "signature.png");
        var controller = Controller(role: "supplier_admin");
        Assert.IsType<NotFoundResult>(await controller.Get("contract-assets", _tenant, "signature.png", default));
        _db.SupplierContractSettings.Add(new SupplierContractSettings { TenantId = _tenant,
            SignatureImageUrl = $"/uploads/contract-assets/{_tenant}/signature.png" });
        await _db.SaveChangesAsync();
        Assert.IsType<PhysicalFileResult>(await controller.Get("contract-assets", _tenant, "signature.png", default));
        Assert.IsType<NotFoundResult>(await controller.Get("contracts", _tenant, "contract.pdf", default));
    }

    [Theory]
    [InlineData("../secret.png")]
    [InlineData("..\\secret.png")]
    [InlineData("%2e%2e.png")]
    [InlineData("image.svg")]
    [InlineData("image.png?access_token=marker")]
    public async Task Malformed_paths_never_reach_disk(string name)
    {
        Assert.IsType<NotFoundResult>(await Controller().Get("shelf-scans", _tenant, name, default));
    }

    [Theory]
    [InlineData("/uploads/shelf-scans/tenant/image.jpg")]
    [InlineData("/uploads/contracts/tenant/document.pdf")]
    [InlineData("/uploads/contract-assets/tenant/signature.png")]
    [InlineData("/uploads/floor-plans/tenant/image.png")]
    [InlineData("/UPLOADS/CONTRACTS/tenant/document.pdf")]
    [InlineData("/uploads//contracts/tenant/document.pdf")]
    [InlineData("/uploads/contracts./tenant/document.pdf")]
    [InlineData("/uploads/items/../contracts/tenant/document.pdf")]
    [InlineData("/uploads\\contracts\\tenant\\document.pdf")]
    public async Task Static_guard_blocks_all_private_categories(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        var called = false;
        await new PrivateUploadGuard(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);
        Assert.False(called);
        Assert.Equal(404, context.Response.StatusCode);
    }

    [Fact]
    public async Task Static_guard_preserves_public_catalog_images()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/uploads/items/tenant/image.jpg";
        var called = false;
        await new PrivateUploadGuard(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);
        Assert.True(called);
    }

    public void Dispose()
    {
        _db.Dispose();
        // Only remove the random disposable fixture folder created by this test instance.
        if (Directory.Exists(_root) && Path.GetFullPath(_root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            Directory.Delete(_root, recursive: true);
    }
}
