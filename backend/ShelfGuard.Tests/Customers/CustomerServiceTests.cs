using NSubstitute;
using ShelfGuard.Application.Features.Customers;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Customers;

/// <summary>
/// TASK-360 (Block 9 pre-launch audit) — Customers had zero test coverage. Focused on the two
/// gaps the audit found: (1) tenant scoping was correct in practice but unverified — the
/// service always stamps the caller's own tenantId regardless of what's in the request, since
/// CreateCustomerDto carries no TenantId field at all; (2) CreateAsync/UpdateAsync only checked
/// Name non-empty + phone uniqueness — Email/Phone had no format validation whatsoever before
/// this task's fix (ValidateContactInfo).
/// </summary>
public sealed class CustomerServiceTests
{
    private readonly ICustomerRepository _repo = Substitute.For<ICustomerRepository>();
    private readonly CustomerService _sut;
    private readonly Guid _tenantId = Guid.NewGuid();

    public CustomerServiceTests() => _sut = new CustomerService(_repo);

    // ── Tenant isolation ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_AlwaysStampsCallersTenantId_RegardlessOfRequest()
    {
        var dto = new CreateCustomerDto("Іван Петренко", null, null, null, null);

        var (customer, error) = await _sut.CreateAsync(_tenantId, dto);

        Assert.Null(error);
        Assert.NotNull(customer);
        await _repo.Received(1).CreateAsync(
            Arg.Is<Customer>(c => c.TenantId == _tenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByIdAsync_PassesCallersTenantId_ToRepository()
    {
        var id = Guid.NewGuid();
        await _sut.GetByIdAsync(id, _tenantId);

        await _repo.Received(1).GetByIdWithTransactionsAsync(id, _tenantId, Arg.Any<CancellationToken>());
    }

    // ── Contact info validation (new — previously no format check at all) ──────

    [Theory]
    [InlineData("not-a-phone")]
    [InlineData("abc")]
    [InlineData("123")] // too short
    public async Task CreateAsync_InvalidPhoneFormat_ReturnsError(string phone)
    {
        var dto = new CreateCustomerDto("Customer", phone, null, null, null);

        var (customer, error) = await _sut.CreateAsync(_tenantId, dto);

        Assert.Null(customer);
        Assert.Contains("phone", error, StringComparison.OrdinalIgnoreCase);
        await _repo.DidNotReceive().CreateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("missing-at-sign.com")]
    [InlineData("no-domain@")]
    public async Task CreateAsync_InvalidEmailFormat_ReturnsError(string email)
    {
        var dto = new CreateCustomerDto("Customer", null, email, null, null);

        var (customer, error) = await _sut.CreateAsync(_tenantId, dto);

        Assert.Null(customer);
        Assert.Contains("email", error, StringComparison.OrdinalIgnoreCase);
        await _repo.DidNotReceive().CreateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("+380501234567")]
    [InlineData("050 123 45 67")]
    [InlineData("(050) 123-45-67")]
    public async Task CreateAsync_ValidPhoneFormats_Succeed(string phone)
    {
        var dto = new CreateCustomerDto("Customer", phone, null, null, null);

        var (customer, error) = await _sut.CreateAsync(_tenantId, dto);

        Assert.Null(error);
        Assert.NotNull(customer);
    }

    [Fact]
    public async Task UpdateAsync_InvalidEmailFormat_ReturnsError_AndDoesNotSave()
    {
        var existing = new Customer { TenantId = _tenantId, Name = "Old" };
        _repo.GetByIdAsync(existing.Id, _tenantId, Arg.Any<CancellationToken>()).Returns(existing);

        var dto = new UpdateCustomerDto("New Name", null, "invalid-email", null, null);
        var (customer, error) = await _sut.UpdateAsync(existing.Id, _tenantId, dto);

        Assert.Null(customer);
        Assert.Contains("email", error, StringComparison.OrdinalIgnoreCase);
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }

    // ── Existing behavior (phone uniqueness) — unaffected by the validation fix ──

    [Fact]
    public async Task CreateAsync_DuplicatePhone_ReturnsConflictError()
    {
        _repo.ExistsByPhoneAsync("+380501234567", _tenantId, null, Arg.Any<CancellationToken>()).Returns(true);
        var dto = new CreateCustomerDto("Customer", "+380501234567", null, null, null);

        var (customer, error) = await _sut.CreateAsync(_tenantId, dto);

        Assert.Null(customer);
        Assert.Contains("already exists", error, StringComparison.OrdinalIgnoreCase);
    }
}
