using NSubstitute;
using ShelfGuard.Application.Features.LegalEntities;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Vchasno;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Marketplace;

/// <summary>
/// TASK-317: cooperation agreement lifecycle — duplicate-request guard,
/// approve preconditions (contract settings), contract number sequencing,
/// and status-dependent operations.
/// </summary>
public sealed class SupplierAgreementServiceTests
{
    private readonly ISupplierAgreementRepository _agreements = Substitute.For<ISupplierAgreementRepository>();
    private readonly ISupplierContractSettingsRepository _settings = Substitute.For<ISupplierContractSettingsRepository>();
    private readonly IMarketplaceRepository _marketplace = Substitute.For<IMarketplaceRepository>();
    private readonly ISupplierChatRepository _tenantNames = Substitute.For<ISupplierChatRepository>();
    private readonly IContractPdfGenerator _pdf = Substitute.For<IContractPdfGenerator>();
    private readonly IVchasnoClientFactory _vchasno = Substitute.For<IVchasnoClientFactory>();
    private readonly ILegalEntityService _legalEntities = Substitute.For<ILegalEntityService>();
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly ITenantSessionOverride _tenantSessionOverride = Substitute.For<ITenantSessionOverride>();
    private readonly SupplierAgreementService _sut;

    private readonly Guid _supplierId = Guid.NewGuid();        // public marketplace supplier id
    private readonly Guid _supplierTenantId = Guid.NewGuid();
    private readonly Guid _clientTenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public SupplierAgreementServiceTests()
    {
        _sut = new SupplierAgreementService(
            _agreements, _settings, _marketplace, _tenantNames, _pdf, _vchasno, _legalEntities, _notifications,
            _tenantSessionOverride);

        _marketplace.GetSupplierTenantIdAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(_supplierTenantId);
        _marketplace.GetTenantBusinessTypeAsync(_supplierTenantId, Arg.Any<CancellationToken>())
            .Returns("supplier");
        _tenantNames.GetTenantDisplayNameAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns("Tenant");
        _pdf.Generate(Arg.Any<ContractPdfData>()).Returns([1, 2, 3]);

        // TASK-582: MarkSignedAsync now runs its notification-enqueue + SaveChanges tail inside
        // _tenantSessionOverride.ExecuteAsync — same pure pass-through convention
        // LoyaltyServiceTests uses for ITenantSessionOverride: invokes the delegate immediately
        // instead of opening a real transaction, so every pre-existing MarkSigned assertion still
        // works unchanged.
        _tenantSessionOverride
            .ExecuteAsync(Arg.Any<Guid>(), Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<Task<bool>>>()());
    }

    // ── SubmitRequestAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitRequest_NoLiveAgreement_CreatesPending()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns((SupplierAgreement?)null);

        SupplierAgreement? created = null;
        _agreements.AddAsync(Arg.Do<SupplierAgreement>(a => created = a), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var (dto, error, isDuplicate) = await _sut.SubmitRequestAsync(
            _clientTenantId, _supplierId, "  Хочемо співпрацювати  ", _userId);

        Assert.Null(error);
        Assert.False(isDuplicate);
        Assert.NotNull(dto);
        Assert.NotNull(created);
        Assert.Equal(SupplierAgreementStatus.Pending, created!.Status);
        Assert.Equal(_supplierTenantId, created.SupplierTenantId);
        Assert.Equal(_clientTenantId, created.ClientTenantId);
        Assert.Equal("Хочемо співпрацювати", created.RequestMessage);
        Assert.Equal(_userId, created.CreatedByUserId);
        await _agreements.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitRequest_LiveAgreementExists_ReturnsDuplicateWithStatus()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(new SupplierAgreement
            {
                SupplierTenantId = _supplierTenantId,
                ClientTenantId   = _clientTenantId,
                Status           = SupplierAgreementStatus.AwaitingSignature,
            });

        var (dto, error, isDuplicate) = await _sut.SubmitRequestAsync(
            _clientTenantId, _supplierId, null, _userId);

        Assert.Null(dto);
        Assert.True(isDuplicate);
        Assert.NotNull(error);
        Assert.Contains(SupplierAgreementStatus.AwaitingSignature, error!);
        await _agreements.DidNotReceive().AddAsync(Arg.Any<SupplierAgreement>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitRequest_WithForeignClientLegalEntity_ReturnsValidationError()
    {
        var legalEntityId = Guid.NewGuid();
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns((SupplierAgreement?)null);
        _legalEntities.BelongsToTenantAsync(_clientTenantId, legalEntityId, Arg.Any<CancellationToken>())
            .Returns(false);

        var (dto, error, _) = await _sut.SubmitRequestAsync(
            _clientTenantId, _supplierId, null, _userId, legalEntityId);

        Assert.Null(dto);
        Assert.Equal(SupplierAgreementService.ClientLegalEntityNotOwnedError, error);
        await _agreements.DidNotReceive().AddAsync(Arg.Any<SupplierAgreement>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitRequest_WithOwnClientLegalEntity_PersistsIt()
    {
        var legalEntityId = Guid.NewGuid();
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns((SupplierAgreement?)null);
        _legalEntities.BelongsToTenantAsync(_clientTenantId, legalEntityId, Arg.Any<CancellationToken>())
            .Returns(true);

        SupplierAgreement? created = null;
        _agreements.AddAsync(Arg.Do<SupplierAgreement>(a => created = a), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var (dto, error, _) = await _sut.SubmitRequestAsync(
            _clientTenantId, _supplierId, null, _userId, legalEntityId);

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.NotNull(created);
        Assert.Equal(legalEntityId, created!.ClientLegalEntityId);
    }

    [Fact]
    public async Task SubmitRequest_ToOwnTenant_ReturnsSelfError()
    {
        _marketplace.GetSupplierTenantIdAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(_clientTenantId);

        var (dto, error, _) = await _sut.SubmitRequestAsync(
            _clientTenantId, _supplierId, null, _userId);

        Assert.Null(dto);
        Assert.Equal(SupplierAgreementService.SelfRequestError, error);
    }

    [Fact]
    public async Task SubmitRequest_SupplierNotFound_ReturnsNotFound()
    {
        _marketplace.GetSupplierTenantIdAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns((Guid?)null);

        var (dto, error, _) = await _sut.SubmitRequestAsync(
            _clientTenantId, _supplierId, null, _userId);

        Assert.Null(dto);
        Assert.Equal(SupplierAgreementService.SupplierNotFoundError, error);
    }

    // ── ApproveAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Approve_WithoutContractSettings_ReturnsSettingsRequiredError()
    {
        var agreement = PendingAgreement();
        _agreements.GetByIdAsync(agreement.Id, Arg.Any<CancellationToken>()).Returns(agreement);
        _settings.GetByTenantAsync(_supplierTenantId, Arg.Any<CancellationToken>())
            .Returns((SupplierContractSettings?)null);

        var (dto, error) = await _sut.ApproveAsync(_supplierTenantId, agreement.Id);

        Assert.Null(dto);
        Assert.Equal(SupplierAgreementService.ContractSettingsRequiredError, error);
        Assert.Equal(SupplierAgreementStatus.Pending, agreement.Status);
    }

    [Fact]
    public async Task Approve_IncompleteSettings_ReturnsSettingsRequiredError()
    {
        var agreement = PendingAgreement();
        _agreements.GetByIdAsync(agreement.Id, Arg.Any<CancellationToken>()).Returns(agreement);
        _settings.GetByTenantAsync(_supplierTenantId, Arg.Any<CancellationToken>())
            .Returns(new SupplierContractSettings
            {
                TenantId  = _supplierTenantId,
                LegalName = "ТОВ «Тест»",
                Iban      = null,          // missing → not approvable
                ServiceName = "Продукти",
            });

        var (dto, error) = await _sut.ApproveAsync(_supplierTenantId, agreement.Id);

        Assert.Null(dto);
        Assert.Equal(SupplierAgreementService.ContractSettingsRequiredError, error);
    }

    [Fact]
    public async Task Approve_Valid_AssignsSequentialContractNumber_AndAwaitsSignature()
    {
        var agreement = PendingAgreement();
        _agreements.GetByIdAsync(agreement.Id, Arg.Any<CancellationToken>()).Returns(agreement);
        _settings.GetByTenantAsync(_supplierTenantId, Arg.Any<CancellationToken>())
            .Returns(CompleteSettings());

        // Two of the supplier's agreements already carry contract numbers → next is 003.
        _agreements.ListForSupplierAsync(_supplierTenantId, null, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierAgreement>
            {
                new() { SupplierTenantId = _supplierTenantId, ContractNumber = "ДС-2026-001" },
                new() { SupplierTenantId = _supplierTenantId, ContractNumber = "ДС-2026-002" },
                new() { SupplierTenantId = _supplierTenantId, ContractNumber = null },
                agreement,
            });

        var (dto, error) = await _sut.ApproveAsync(_supplierTenantId, agreement.Id);

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal($"ДС-{DateTime.UtcNow.Year}-003", agreement.ContractNumber);
        Assert.Equal(SupplierAgreementStatus.AwaitingSignature, agreement.Status);
        Assert.NotNull(agreement.DecidedAt);
        Assert.False(string.IsNullOrEmpty(agreement.ContractFilePath));
        _pdf.Received(1).Generate(Arg.Is<ContractPdfData>(d => d.ContractNumber == agreement.ContractNumber));
        await _agreements.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approve_FirstContract_StartsAtOne()
    {
        var agreement = PendingAgreement();
        _agreements.GetByIdAsync(agreement.Id, Arg.Any<CancellationToken>()).Returns(agreement);
        _settings.GetByTenantAsync(_supplierTenantId, Arg.Any<CancellationToken>())
            .Returns(CompleteSettings());
        _agreements.ListForSupplierAsync(_supplierTenantId, null, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierAgreement> { agreement });

        var (_, error) = await _sut.ApproveAsync(_supplierTenantId, agreement.Id);

        Assert.Null(error);
        Assert.Equal($"ДС-{DateTime.UtcNow.Year}-001", agreement.ContractNumber);
    }

    [Fact]
    public async Task Approve_WithClientLegalEntity_PassesItsRequisitesToPdfGenerator()
    {
        var legalEntityId = Guid.NewGuid();
        var agreement = PendingAgreement();
        agreement.ClientLegalEntityId = legalEntityId;
        _agreements.GetByIdAsync(agreement.Id, Arg.Any<CancellationToken>()).Returns(agreement);
        _settings.GetByTenantAsync(_supplierTenantId, Arg.Any<CancellationToken>())
            .Returns(CompleteSettings());
        _agreements.ListForSupplierAsync(_supplierTenantId, null, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierAgreement> { agreement });

        _legalEntities.GetByIdAsync(_clientTenantId, legalEntityId, Arg.Any<CancellationToken>())
            .Returns((new ShelfGuard.Application.Features.LegalEntities.Dtos.LegalEntityDto(
                legalEntityId, "ТОВ «Клієнт»", "87654321", "м. Львів", "Петренко П.П.",
                null, null, "UA000000000000000000000000000", "Банк", true, true,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), (string?)null));

        var (dto, error) = await _sut.ApproveAsync(_supplierTenantId, agreement.Id);

        Assert.Null(error);
        Assert.NotNull(dto);
        _pdf.Received(1).Generate(Arg.Is<ContractPdfData>(d =>
            d.ClientLegalName == "ТОВ «Клієнт»" &&
            d.ClientEdrpou == "87654321" &&
            d.ClientIsVatPayer));
    }

    [Fact]
    public async Task Approve_WithDeliveryCoverage_ResolvesRegionCodesToNamesForPdf()
    {
        var agreement = PendingAgreement();
        _agreements.GetByIdAsync(agreement.Id, Arg.Any<CancellationToken>()).Returns(agreement);
        _settings.GetByTenantAsync(_supplierTenantId, Arg.Any<CancellationToken>())
            .Returns(CompleteSettings());
        _agreements.ListForSupplierAsync(_supplierTenantId, null, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierAgreement> { agreement });

        var profile = new SupplierProfile
        {
            TenantId = _supplierTenantId,
            DeliveryCoverage =
                """
                { "served": [ { "regionCode": "UA-32", "terms": "2-3 дні" },
                              { "regionCode": "UA-18-ZHYTOMYR" } ],
                  "notServed": [ "UA-43" ],
                  "note": "Новою Поштою за домовленістю" }
                """,
        };
        (SupplierProfile? Profile, Supplier? Supplier)? row = (profile, null);
        _marketplace.GetOwnProfileAsync(_supplierTenantId, Arg.Any<CancellationToken>()).Returns(row);

        var (dto, error) = await _sut.ApproveAsync(_supplierTenantId, agreement.Id);

        Assert.Null(error);
        Assert.NotNull(dto);
        _pdf.Received(1).Generate(Arg.Is<ContractPdfData>(d =>
            d.DeliveryCoverageServed != null &&
            d.DeliveryCoverageServed.Count == 2 &&
            d.DeliveryCoverageServed[0].RegionName == "Київська" &&
            d.DeliveryCoverageServed[0].Terms == "2-3 дні" &&
            d.DeliveryCoverageServed[1].RegionName == "Житомир" &&
            d.DeliveryCoverageServed[1].Terms == null &&
            d.DeliveryCoverageNotServed != null &&
            d.DeliveryCoverageNotServed.Count == 1 &&
            d.DeliveryCoverageNotServed[0] == "Автономна Республіка Крим" &&
            d.DeliveryCoverageNote == "Новою Поштою за домовленістю"));
    }

    [Fact]
    public async Task Approve_NoDeliveryCoverage_PassesNullCoverageToPdf()
    {
        var agreement = PendingAgreement();
        _agreements.GetByIdAsync(agreement.Id, Arg.Any<CancellationToken>()).Returns(agreement);
        _settings.GetByTenantAsync(_supplierTenantId, Arg.Any<CancellationToken>())
            .Returns(CompleteSettings());
        _agreements.ListForSupplierAsync(_supplierTenantId, null, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierAgreement> { agreement });
        // _marketplace.GetOwnProfileAsync not stubbed → returns null (no profile).

        var (dto, error) = await _sut.ApproveAsync(_supplierTenantId, agreement.Id);

        Assert.Null(error);
        Assert.NotNull(dto);
        _pdf.Received(1).Generate(Arg.Is<ContractPdfData>(d =>
            d.DeliveryCoverageServed == null &&
            d.DeliveryCoverageNotServed == null &&
            d.DeliveryCoverageNote == null));
    }

    [Fact]
    public async Task Approve_ForeignSupplierTenant_ReturnsNotFound()
    {
        var agreement = PendingAgreement();
        _agreements.GetByIdAsync(agreement.Id, Arg.Any<CancellationToken>()).Returns(agreement);

        var (dto, error) = await _sut.ApproveAsync(Guid.NewGuid(), agreement.Id);

        Assert.Null(dto);
        Assert.Equal(SupplierAgreementService.AgreementNotFoundError, error);
    }

    [Fact]
    public async Task Approve_NotPending_ReturnsInvalidStatus()
    {
        var agreement = PendingAgreement();
        agreement.Status = SupplierAgreementStatus.Active;
        _agreements.GetByIdAsync(agreement.Id, Arg.Any<CancellationToken>()).Returns(agreement);

        var (dto, error) = await _sut.ApproveAsync(_supplierTenantId, agreement.Id);

        Assert.Null(dto);
        Assert.Equal(SupplierAgreementService.InvalidStatusError, error);
    }

    // ── Reject / MarkSigned / Terminate ───────────────────────────────────────

    [Fact]
    public async Task Reject_WithoutReason_ReturnsValidationError()
    {
        var (dto, error) = await _sut.RejectAsync(_supplierTenantId, Guid.NewGuid(), "   ");

        Assert.Null(dto);
        Assert.Equal(SupplierAgreementService.RejectReasonRequiredError, error);
    }

    [Fact]
    public async Task Reject_Pending_SetsRejectedWithReason()
    {
        var agreement = PendingAgreement();
        _agreements.GetByIdAsync(agreement.Id, Arg.Any<CancellationToken>()).Returns(agreement);

        var (dto, error) = await _sut.RejectAsync(_supplierTenantId, agreement.Id, "Не працюємо з цим регіоном");

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal(SupplierAgreementStatus.Rejected, agreement.Status);
        Assert.Equal("Не працюємо з цим регіоном", agreement.RejectionReason);
        Assert.NotNull(agreement.DecidedAt);
    }

    [Fact]
    public async Task MarkSigned_AwaitingSignature_BecomesActive()
    {
        var agreement = PendingAgreement();
        agreement.Status = SupplierAgreementStatus.AwaitingSignature;
        _agreements.GetByIdAsync(agreement.Id, Arg.Any<CancellationToken>()).Returns(agreement);

        var (dto, error) = await _sut.MarkSignedAsync(_supplierTenantId, agreement.Id);

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal(SupplierAgreementStatus.Active, agreement.Status);
        Assert.NotNull(agreement.SignedAt);
    }

    [Fact]
    public async Task MarkSigned_Pending_ReturnsInvalidStatus()
    {
        var agreement = PendingAgreement();
        _agreements.GetByIdAsync(agreement.Id, Arg.Any<CancellationToken>()).Returns(agreement);

        var (dto, error) = await _sut.MarkSignedAsync(_supplierTenantId, agreement.Id);

        Assert.Null(dto);
        Assert.Equal(SupplierAgreementService.InvalidStatusError, error);
    }

    [Fact]
    public async Task Terminate_Active_BecomesTerminated()
    {
        var agreement = PendingAgreement();
        agreement.Status = SupplierAgreementStatus.Active;
        _agreements.GetByIdAsync(agreement.Id, Arg.Any<CancellationToken>()).Returns(agreement);

        var (dto, error) = await _sut.TerminateAsync(_supplierTenantId, agreement.Id, "Кінець сезону");

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal(SupplierAgreementStatus.Terminated, agreement.Status);
        Assert.NotNull(agreement.TerminatedAt);
        Assert.Equal("Кінець сезону", agreement.RejectionReason);
    }

    [Fact]
    public async Task Terminate_NotActive_ReturnsInvalidStatus()
    {
        var agreement = PendingAgreement();
        _agreements.GetByIdAsync(agreement.Id, Arg.Any<CancellationToken>()).Returns(agreement);

        var (dto, error) = await _sut.TerminateAsync(_supplierTenantId, agreement.Id, null);

        Assert.Null(dto);
        Assert.Equal(SupplierAgreementService.InvalidStatusError, error);
    }

    // ── SendToVchasnoAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task SendToVchasno_NotConfigured_ReturnsError()
    {
        var agreement = PendingAgreement();
        agreement.Status = SupplierAgreementStatus.AwaitingSignature;
        agreement.ContractFilePath = "/uploads/contracts/x/y.pdf";
        _agreements.GetByIdAsync(agreement.Id, Arg.Any<CancellationToken>()).Returns(agreement);
        _vchasno.GetForTenantAsync(_supplierTenantId, Arg.Any<CancellationToken>())
            .Returns((IVchasnoClient?)null);

        var (dto, error) = await _sut.SendToVchasnoAsync(_supplierTenantId, agreement.Id);

        Assert.Null(dto);
        Assert.Equal(SupplierAgreementService.VchasnoNotConfiguredError, error);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private SupplierAgreement PendingAgreement() => new()
    {
        SupplierTenantId = _supplierTenantId,
        ClientTenantId   = _clientTenantId,
        Status           = SupplierAgreementStatus.Pending,
    };

    private SupplierContractSettings CompleteSettings() => new()
    {
        TenantId    = _supplierTenantId,
        LegalName   = "ТОВ «Тест Постач»",
        Edrpou      = "12345678",
        Iban        = "UA123456789012345678901234567",
        BankName    = "ПриватБанк",
        ServiceName = "Постачання продуктів",
    };
}
