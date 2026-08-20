using System.Text.Json;
using ShelfGuard.Application.Features.LegalEntities;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Application.Features.Marketplace.Vchasno;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// Cooperation agreement lifecycle + supplier contract settings (TASK-317).
/// Contract PDF rendering is delegated to IContractPdfGenerator (Infrastructure /
/// QuestPDF); Вчасно e-signature goes through the per-tenant IVchasnoClientFactory
/// (integration_configs, service = "vchasno") — mirrors the Checkbox pattern.
/// </summary>
public sealed class SupplierAgreementService : ISupplierAgreementService
{
    public const string SupplierNotFoundError = "Постачальника не знайдено.";
    public const string NotSupplierError = "Цей постачальник не приймає заявки на співпрацю.";
    public const string SelfRequestError = "Не можна подати заявку на співпрацю із власним постачальником.";
    public const string AgreementNotFoundError = "Угоду не знайдено.";
    public const string ContractSettingsRequiredError =
        "Спочатку заповніть реквізити договору (юридична назва, IBAN, назва послуги/товару) у налаштуваннях кабінету.";
    public const string InvalidStatusError = "Операція неможлива для поточного статусу угоди.";
    public const string ContractNotGeneratedError = "Договір ще не згенеровано.";
    public const string VchasnoNotConfiguredError = "Інтеграцію Вчасно не налаштовано.";
    public const string RejectReasonRequiredError = "Вкажіть причину відмови.";
    public const string LegalNameRequiredError = "Юридична назва (LegalName) — обовʼязкове поле.";
    public const string UnsupportedImageFormatError = "Непідтримуваний формат зображення. Дозволено: PNG, JPG.";
    public const string InvalidSigningMethodError = "Невірний спосіб підписання.";
    public const string SigningEmailRequiredError = "Вкажіть email для отримання документа у Вчасно.";
    public const string ClientLegalEntityNotOwnedError =
        "Вказана юридична особа не належить вашому тенанту.";

    public const int MaxMessageLength = 2000;

    private static readonly string[] AllowedImageExtensions = [".png", ".jpg", ".jpeg"];

    private readonly ISupplierAgreementRepository _agreements;
    private readonly ISupplierContractSettingsRepository _settings;
    private readonly IMarketplaceRepository _marketplace;
    private readonly ISupplierChatRepository _tenantNames;
    private readonly IContractPdfGenerator _pdf;
    private readonly IVchasnoClientFactory _vchasno;
    private readonly ILegalEntityService _legalEntities;
    private readonly INotificationRepository _notifications;
    private readonly ITenantSessionOverride _tenantSessionOverride;

    public SupplierAgreementService(
        ISupplierAgreementRepository agreements,
        ISupplierContractSettingsRepository settings,
        IMarketplaceRepository marketplace,
        ISupplierChatRepository tenantNames,
        IContractPdfGenerator pdf,
        IVchasnoClientFactory vchasno,
        ILegalEntityService legalEntities,
        INotificationRepository notifications,
        ITenantSessionOverride tenantSessionOverride)
    {
        _agreements    = agreements;
        _settings      = settings;
        _marketplace   = marketplace;
        _tenantNames   = tenantNames;
        _pdf           = pdf;
        _vchasno       = vchasno;
        _legalEntities = legalEntities;
        _notifications = notifications;
        _tenantSessionOverride = tenantSessionOverride;
    }

    // ── Client side ───────────────────────────────────────────────────────────

    public async Task<(CooperationAgreementDto? Agreement, string? Error, bool IsDuplicate)> SubmitRequestAsync(
        Guid clientTenantId, Guid supplierId, string? message, Guid userId,
        Guid? clientLegalEntityId = null, CancellationToken ct = default)
    {
        var supplierTenantId = await _marketplace.GetSupplierTenantIdAsync(supplierId, ct);
        if (supplierTenantId is null)
            return (null, SupplierNotFoundError, false);

        if (supplierTenantId.Value == clientTenantId)
            return (null, SelfRequestError, false);

        var businessType = await _marketplace.GetTenantBusinessTypeAsync(supplierTenantId.Value, ct);
        if (!SupplierOnboarding.IsSupplierBusinessType(businessType))
            return (null, NotSupplierError, false);

        var trimmedMessage = message?.Trim();
        if (trimmedMessage is { Length: > MaxMessageLength })
            return (null, $"Повідомлення не може перевищувати {MaxMessageLength} символів.", false);

        if (clientLegalEntityId.HasValue &&
            !await _legalEntities.BelongsToTenantAsync(clientTenantId, clientLegalEntityId.Value, ct))
            return (null, ClientLegalEntityNotOwnedError, false);

        // One live agreement per pair (partial unique index) — surface the current
        // status so the client UI can explain why a new request is not possible.
        var existing = await _agreements.GetForPairAsync(supplierTenantId.Value, clientTenantId, ct);
        if (existing is not null)
            return (null, $"Заявка на співпрацю вже існує (статус: {existing.Status}).", true);

        var agreement = new SupplierAgreement
        {
            SupplierTenantId    = supplierTenantId.Value,
            ClientTenantId      = clientTenantId,
            ClientLegalEntityId = clientLegalEntityId,
            Status              = SupplierAgreementStatus.Pending,
            RequestMessage      = string.IsNullOrEmpty(trimmedMessage) ? null : trimmedMessage,
            CreatedByUserId     = userId,
        };

        await _agreements.AddAsync(agreement, ct);

        try
        {
            await _agreements.SaveChangesAsync(ct);
        }
        catch (Exception)
        {
            // Lost a race on the partial unique (SupplierTenantId, ClientTenantId)
            // index — another request created the live agreement first.
            var winner = await _agreements.GetForPairAsync(supplierTenantId.Value, clientTenantId, ct);
            if (winner is null) throw;
            return (null, $"Заявка на співпрацю вже існує (статус: {winner.Status}).", true);
        }

        return (await ToDtoAsync(agreement, ct), null, false);
    }

    public async Task<IReadOnlyList<CooperationAgreementDto>> ListForClientAsync(
        Guid clientTenantId, CancellationToken ct = default)
    {
        var rows = await _agreements.ListForClientAsync(clientTenantId, ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task<(byte[]? Content, string? FileName, string? Error)> DownloadContractAsync(
        Guid agreementId, Guid callerTenantId, CancellationToken ct = default)
    {
        var agreement = await _agreements.GetByIdAsync(agreementId, ct);
        if (agreement is null
            || (agreement.SupplierTenantId != callerTenantId && agreement.ClientTenantId != callerTenantId))
            return (null, null, AgreementNotFoundError);

        if (string.IsNullOrEmpty(agreement.ContractFilePath))
            return (null, null, ContractNotGeneratedError);

        var diskPath = ToDiskPath(agreement.ContractFilePath);
        if (!File.Exists(diskPath))
            return (null, null, ContractNotGeneratedError);

        var bytes = await File.ReadAllBytesAsync(diskPath, ct);
        var fileName = string.IsNullOrEmpty(agreement.ContractNumber)
            ? "contract.pdf"
            : $"{agreement.ContractNumber}.pdf";

        return (bytes, fileName, null);
    }

    /// <summary>
    /// Client chooses how the awaiting_signature contract will be signed:
    /// "physical" (mail to the supplier's legal address, no system action —
    /// the supplier still confirms via the existing MarkSignedAsync) or
    /// "vchasno" (immediately uploads the contract PDF to Вчасно with the
    /// client-provided email as recipient — independent of the supplier's
    /// manual edrpou-based SendToVchasnoAsync).
    /// </summary>
    public async Task<(CooperationAgreementDto? Agreement, string? Error)> ChooseSigningMethodAsync(
        Guid clientTenantId, Guid agreementId, string method, string? email, CancellationToken ct = default)
    {
        var agreement = await _agreements.GetByIdAsync(agreementId, ct);
        if (agreement is null || agreement.ClientTenantId != clientTenantId)
            return (null, AgreementNotFoundError);

        if (agreement.Status != SupplierAgreementStatus.AwaitingSignature)
            return (null, InvalidStatusError);

        if (method != "physical" && method != "vchasno")
            return (null, InvalidSigningMethodError);

        if (method == "vchasno")
        {
            var trimmedEmail = email?.Trim();
            if (string.IsNullOrEmpty(trimmedEmail) || !trimmedEmail.Contains('@'))
                return (null, SigningEmailRequiredError);

            if (string.IsNullOrEmpty(agreement.ContractFilePath))
                return (null, ContractNotGeneratedError);

            var client = await _vchasno.GetForTenantAsync(agreement.SupplierTenantId, ct);
            if (client is null)
                return (null, VchasnoNotConfiguredError);

            var diskPath = ToDiskPath(agreement.ContractFilePath);
            if (!File.Exists(diskPath))
                return (null, ContractNotGeneratedError);

            var pdfBytes = await File.ReadAllBytesAsync(diskPath, ct);
            var title = $"Договір про співпрацю № {agreement.ContractNumber}";

            string documentId;
            try
            {
                documentId = await client.UploadDocumentAsync(
                    $"{agreement.ContractNumber}.pdf", pdfBytes, recipientEdrpou: null, recipientEmail: trimmedEmail, title, ct);
            }
            catch (Exception ex)
            {
                return (null, $"Не вдалося надіслати документ у Вчасно: {ex.Message}");
            }

            agreement.VchasnoDocumentId = documentId;
            agreement.SigningEmail = trimmedEmail;
        }
        else
        {
            agreement.SigningEmail = null;
        }

        agreement.SigningMethod = method;
        agreement.UpdatedAt = DateTimeOffset.UtcNow;

        _agreements.Update(agreement);
        await _agreements.SaveChangesAsync(ct);

        return (await ToDtoAsync(agreement, ct), null);
    }

    // ── Supplier side ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<CooperationAgreementDto>> ListForSupplierAsync(
        Guid supplierTenantId, string? status, CancellationToken ct = default)
    {
        var rows = await _agreements.ListForSupplierAsync(supplierTenantId, status, ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task<(CooperationAgreementDto? Agreement, string? Error)> ApproveAsync(
        Guid supplierTenantId, Guid agreementId, CancellationToken ct = default)
    {
        var agreement = await GetOwnAsync(supplierTenantId, agreementId, ct);
        if (agreement is null) return (null, AgreementNotFoundError);

        if (agreement.Status != SupplierAgreementStatus.Pending)
            return (null, InvalidStatusError);

        var settings = await _settings.GetByTenantAsync(supplierTenantId, ct);
        if (settings is null
            || string.IsNullOrWhiteSpace(settings.LegalName)
            || string.IsNullOrWhiteSpace(settings.Iban)
            || string.IsNullOrWhiteSpace(settings.ServiceName))
            return (null, ContractSettingsRequiredError);

        agreement.ContractNumber = await NextContractNumberAsync(supplierTenantId, ct);
        await GenerateAndStoreContractAsync(agreement, settings, ct);

        agreement.Status    = SupplierAgreementStatus.AwaitingSignature;
        agreement.DecidedAt = DateTimeOffset.UtcNow;
        agreement.UpdatedAt = DateTimeOffset.UtcNow;

        _agreements.Update(agreement);
        await _agreements.SaveChangesAsync(ct);

        return (await ToDtoAsync(agreement, ct), null);
    }

    public async Task<(CooperationAgreementDto? Agreement, string? Error)> RejectAsync(
        Guid supplierTenantId, Guid agreementId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return (null, RejectReasonRequiredError);

        var agreement = await GetOwnAsync(supplierTenantId, agreementId, ct);
        if (agreement is null) return (null, AgreementNotFoundError);

        if (agreement.Status != SupplierAgreementStatus.Pending)
            return (null, InvalidStatusError);

        agreement.Status          = SupplierAgreementStatus.Rejected;
        agreement.RejectionReason = reason.Trim();
        agreement.DecidedAt       = DateTimeOffset.UtcNow;
        agreement.UpdatedAt       = DateTimeOffset.UtcNow;

        _agreements.Update(agreement);
        await _agreements.SaveChangesAsync(ct);

        return (await ToDtoAsync(agreement, ct), null);
    }

    public async Task<(CooperationAgreementDto? Agreement, string? Error)> RegenerateContractAsync(
        Guid supplierTenantId, Guid agreementId, CancellationToken ct = default)
    {
        var agreement = await GetOwnAsync(supplierTenantId, agreementId, ct);
        if (agreement is null) return (null, AgreementNotFoundError);

        // Only allowed pre-signature. Once Active, the document is a legally
        // signed record (physical mail or Вчасно) — re-rendering it would
        // produce a new PDF that no longer matches what was actually signed,
        // so regeneration must never be possible after signing.
        if (agreement.Status != SupplierAgreementStatus.AwaitingSignature)
            return (null, InvalidStatusError);

        var settings = await _settings.GetByTenantAsync(supplierTenantId, ct);
        if (settings is null
            || string.IsNullOrWhiteSpace(settings.LegalName)
            || string.IsNullOrWhiteSpace(settings.Iban)
            || string.IsNullOrWhiteSpace(settings.ServiceName))
            return (null, ContractSettingsRequiredError);

        await GenerateAndStoreContractAsync(agreement, settings, ct);

        agreement.UpdatedAt = DateTimeOffset.UtcNow;
        _agreements.Update(agreement);
        await _agreements.SaveChangesAsync(ct);

        return (await ToDtoAsync(agreement, ct), null);
    }

    public async Task<(CooperationAgreementDto? Agreement, string? Error)> SendToVchasnoAsync(
        Guid supplierTenantId, Guid agreementId, CancellationToken ct = default)
    {
        var agreement = await GetOwnAsync(supplierTenantId, agreementId, ct);
        if (agreement is null) return (null, AgreementNotFoundError);

        if (agreement.Status != SupplierAgreementStatus.AwaitingSignature)
            return (null, InvalidStatusError);

        if (string.IsNullOrEmpty(agreement.ContractFilePath))
            return (null, ContractNotGeneratedError);

        var client = await _vchasno.GetForTenantAsync(supplierTenantId, ct);
        if (client is null)
            return (null, VchasnoNotConfiguredError);

        var diskPath = ToDiskPath(agreement.ContractFilePath);
        if (!File.Exists(diskPath))
            return (null, ContractNotGeneratedError);

        var pdfBytes = await File.ReadAllBytesAsync(diskPath, ct);
        var title = $"Договір про співпрацю № {agreement.ContractNumber}";

        string documentId;
        try
        {
            documentId = await client.UploadDocumentAsync(
                $"{agreement.ContractNumber}.pdf", pdfBytes, recipientEdrpou: null, recipientEmail: null, title, ct);
        }
        catch (Exception ex)
        {
            return (null, $"Не вдалося надіслати документ у Вчасно: {ex.Message}");
        }

        agreement.VchasnoDocumentId = documentId;
        agreement.UpdatedAt = DateTimeOffset.UtcNow;

        _agreements.Update(agreement);
        await _agreements.SaveChangesAsync(ct);

        return (await ToDtoAsync(agreement, ct), null);
    }

    public async Task<(CooperationAgreementDto? Agreement, string? Error)> MarkSignedAsync(
        Guid supplierTenantId, Guid agreementId, CancellationToken ct = default)
    {
        var agreement = await GetOwnAsync(supplierTenantId, agreementId, ct);
        if (agreement is null) return (null, AgreementNotFoundError);

        if (agreement.Status != SupplierAgreementStatus.AwaitingSignature)
            return (null, InvalidStatusError);

        agreement.Status    = SupplierAgreementStatus.Active;
        agreement.SignedAt  = DateTimeOffset.UtcNow;
        agreement.UpdatedAt = DateTimeOffset.UtcNow;

        _agreements.Update(agreement);

        // TASK-582: EnqueueSignedNotificationAsync inserts into notification_queue with
        // TenantId = agreement.ClientTenantId (the notification recipient), while the ambient
        // DB session here is authenticated as the SUPPLIER tenant (whoever called this
        // endpoint) — notification_queue's plain tenant_isolation RLS policy only allows
        // TenantId = session tenant, so this insert used to throw an unhandled
        // PostgresException (42501 RLS violation) that propagated to the client as a bare 500
        // (masked as a CORS error, since the exception aborted the connection before headers
        // were sent — see Program.cs's exception handler). Run the notification enqueue and the
        // final SaveChangesAsync under an explicit override of the CLIENT tenant's RLS context
        // instead (ITenantSessionOverride) — safe because agreement.ClientTenantId is already a
        // trusted value at this point (GetOwnAsync above already confirmed this agreement
        // belongs to the calling supplier tenant), and supplier_agreements' own RLS policy is
        // OR-based on both SupplierTenantId/ClientTenantId, so the status-change columns that
        // also get flushed by this same SaveChangesAsync call still satisfy their RLS under
        // either tenant. Bonus: the status change and the outbox row now commit atomically.
        await _tenantSessionOverride.ExecuteAsync(agreement.ClientTenantId, async () =>
        {
            await EnqueueSignedNotificationAsync(agreement, ct);
            await _agreements.SaveChangesAsync(ct);
            return true;
        }, ct);

        return (await ToDtoAsync(agreement, ct), null);
    }

    /// <summary>
    /// ADR-018 §2: Postgres outbox row (EventType = "supplier_agreement.signed") for the
    /// client tenant, picked up by the worker's notification-dispatch job. UserId = null,
    /// Channel = "system", Status = "pending".
    /// </summary>
    private async Task EnqueueSignedNotificationAsync(SupplierAgreement agreement, CancellationToken ct)
    {
        var supplierName = await _tenantNames.GetTenantDisplayNameAsync(agreement.SupplierTenantId, ct)
                           ?? "Постачальник";

        var payload = JsonSerializer.Serialize(new
        {
            agreementId = agreement.Id,
            supplierTenantId = agreement.SupplierTenantId,
            supplierName,
            contractNumber = agreement.ContractNumber,
            signedAt = agreement.SignedAt,
        });

        await _notifications.EnqueueAsync(new NotificationQueue
        {
            TenantId  = agreement.ClientTenantId,
            UserId    = null,
            StoreId   = null,
            Title     = $"Договір підписано: {supplierName}",
            Channel   = "system",
            EventType = "supplier_agreement.signed",
            Payload   = payload,
            Status    = "pending",
        }, ct);
    }

    public async Task<(CooperationAgreementDto? Agreement, string? Error)> TerminateAsync(
        Guid supplierTenantId, Guid agreementId, string? reason, CancellationToken ct = default)
    {
        var agreement = await GetOwnAsync(supplierTenantId, agreementId, ct);
        if (agreement is null) return (null, AgreementNotFoundError);

        if (agreement.Status != SupplierAgreementStatus.Active)
            return (null, InvalidStatusError);

        agreement.Status       = SupplierAgreementStatus.Terminated;
        agreement.TerminatedAt = DateTimeOffset.UtcNow;
        agreement.UpdatedAt    = DateTimeOffset.UtcNow;
        // RejectionReason doubles as the termination reason (documented on the DTO).
        if (!string.IsNullOrWhiteSpace(reason))
            agreement.RejectionReason = reason.Trim();

        _agreements.Update(agreement);
        await _agreements.SaveChangesAsync(ct);

        return (await ToDtoAsync(agreement, ct), null);
    }

    // ── Contract settings ─────────────────────────────────────────────────────

    public async Task<SupplierContractSettingsDto?> GetContractSettingsAsync(
        Guid supplierTenantId, CancellationToken ct = default)
    {
        var settings = await _settings.GetByTenantAsync(supplierTenantId, ct);
        return settings is null ? null : ToSettingsDto(settings);
    }

    public async Task<(SupplierContractSettingsDto? Settings, string? Error)> UpsertContractSettingsAsync(
        Guid supplierTenantId, UpsertContractSettingsDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.LegalName))
            return (null, LegalNameRequiredError);

        var settings = await _settings.GetByTenantAsync(supplierTenantId, ct);
        var isNew = settings is null;
        settings ??= new SupplierContractSettings { TenantId = supplierTenantId };

        settings.LegalName          = request.LegalName.Trim();
        settings.Edrpou             = Norm(request.Edrpou);
        settings.Iban               = Norm(request.Iban);
        settings.BankName           = Norm(request.BankName);
        settings.LegalAddress       = Norm(request.LegalAddress);
        settings.DirectorName       = Norm(request.DirectorName);
        settings.Phone              = Norm(request.Phone);
        settings.Email              = Norm(request.Email);
        settings.ServiceName        = Norm(request.ServiceName);
        settings.ServiceDescription = Norm(request.ServiceDescription);
        settings.IsVatPayer         = request.IsVatPayer;
        settings.UpdatedAt          = DateTimeOffset.UtcNow;

        if (isNew) await _settings.AddAsync(settings, ct);
        else       _settings.Update(settings);

        await _settings.SaveChangesAsync(ct);

        return (ToSettingsDto(settings), null);
    }

    public async Task<(string? Url, string? Error)> UploadContractAssetAsync(
        Guid supplierTenantId, Stream stream, string fileName, ContractAssetKind kind,
        CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(ext))
            return (null, UnsupportedImageFormatError);

        var settings = await _settings.GetByTenantAsync(supplierTenantId, ct);
        var isNew = settings is null;
        if (isNew)
            return (null, ContractSettingsRequiredError);

        var uploadDir = Path.Combine("wwwroot", "uploads", "contract-assets", supplierTenantId.ToString());
        Directory.CreateDirectory(uploadDir);

        var assetName = kind == ContractAssetKind.Signature ? "signature" : "stamp";
        var fileNameOnDisk = $"{assetName}{ext}";

        await using (var fs = new FileStream(Path.Combine(uploadDir, fileNameOnDisk), FileMode.Create))
            await stream.CopyToAsync(fs, ct);

        var url = $"/uploads/contract-assets/{supplierTenantId}/{fileNameOnDisk}";

        if (kind == ContractAssetKind.Signature) settings!.SignatureImageUrl = url;
        else                                     settings!.StampImageUrl     = url;
        settings.UpdatedAt = DateTimeOffset.UtcNow;

        _settings.Update(settings);
        await _settings.SaveChangesAsync(ct);

        return (url, null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Cross-tenant guard: the agreement must belong to THIS supplier tenant.</summary>
    private async Task<SupplierAgreement?> GetOwnAsync(
        Guid supplierTenantId, Guid agreementId, CancellationToken ct)
    {
        var agreement = await _agreements.GetByIdAsync(agreementId, ct);
        return agreement is null || agreement.SupplierTenantId != supplierTenantId
            ? null
            : agreement;
    }

    /// <summary>
    /// Next contract number: «ДС-{yyyy}-{NNN}», NNN sequential per supplier —
    /// count of the supplier's agreements that already carry a contract number + 1.
    /// Simple and safe for the expected volume (per-supplier, low contention).
    /// </summary>
    private async Task<string> NextContractNumberAsync(Guid supplierTenantId, CancellationToken ct)
    {
        var all = await _agreements.ListForSupplierAsync(supplierTenantId, status: null, ct);
        var seq = all.Count(a => !string.IsNullOrEmpty(a.ContractNumber)) + 1;
        return $"ДС-{DateTime.UtcNow.Year}-{seq:D3}";
    }

    private async Task GenerateAndStoreContractAsync(
        SupplierAgreement agreement, SupplierContractSettings settings, CancellationToken ct)
    {
        var supplierName = await _tenantNames.GetTenantDisplayNameAsync(agreement.SupplierTenantId, ct)
                           ?? settings.LegalName;
        var clientName = await _tenantNames.GetTenantDisplayNameAsync(agreement.ClientTenantId, ct)
                         ?? string.Empty;

        // TASK-327: if the client picked one of their own registered legal
        // entities when requesting cooperation, render its requisites on the
        // "Замовник" side of the contract too. Falls back to just the tenant
        // display name (clientName above) when not set — unchanged behavior.
        LegalEntities.Dtos.LegalEntityDto? clientLegalEntity = null;
        if (agreement.ClientLegalEntityId.HasValue)
        {
            var (entity, _) = await _legalEntities.GetByIdAsync(
                agreement.ClientTenantId, agreement.ClientLegalEntityId.Value, ct);
            clientLegalEntity = entity;
        }

        var data = new ContractPdfData(
            agreement.ContractNumber ?? string.Empty,
            DateTimeOffset.UtcNow,
            supplierName,
            clientName,
            settings.LegalName,
            settings.Edrpou,
            settings.Iban,
            settings.BankName,
            settings.LegalAddress,
            settings.DirectorName,
            settings.Phone,
            settings.Email,
            settings.ServiceName,
            settings.ServiceDescription,
            settings.IsVatPayer,
            ReadImageSafe(settings.SignatureImageUrl),
            ReadImageSafe(settings.StampImageUrl),
            clientLegalEntity?.LegalName,
            clientLegalEntity?.Edrpou,
            clientLegalEntity?.Iban,
            clientLegalEntity?.BankName,
            clientLegalEntity?.LegalAddress,
            clientLegalEntity?.DirectorName,
            clientLegalEntity?.IsVatPayer ?? false);

        var pdfBytes = _pdf.Generate(data);

        var dir = Path.Combine("wwwroot", "uploads", "contracts", agreement.SupplierTenantId.ToString());
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, $"{agreement.Id}.pdf"), pdfBytes, ct);

        agreement.ContractFilePath =
            $"/uploads/contracts/{agreement.SupplierTenantId}/{agreement.Id}.pdf";
    }

    /// <summary>Loads a signature/stamp image from its wwwroot URL; null when unset or missing on disk.</summary>
    private static byte[]? ReadImageSafe(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var diskPath = ToDiskPath(url);
        try
        {
            return File.Exists(diskPath) ? File.ReadAllBytes(diskPath) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string ToDiskPath(string relativeUrl) =>
        Path.Combine("wwwroot", relativeUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    private static string? Norm(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private async Task<IReadOnlyList<CooperationAgreementDto>> ToDtosAsync(
        IReadOnlyList<SupplierAgreement> rows, CancellationToken ct)
    {
        // Tenant display names are looked up per distinct id (tenants table — no RLS);
        // per-supplier volume is small, so no SQL-side join is warranted.
        var names = new Dictionary<Guid, string>();
        var addresses = new Dictionary<Guid, string?>();
        var result = new List<CooperationAgreementDto>(rows.Count);
        foreach (var row in rows)
            result.Add(ToDto(row,
                await GetNameCachedAsync(row.SupplierTenantId, names, ct),
                await GetNameCachedAsync(row.ClientTenantId, names, ct),
                await GetSupplierAddressCachedAsync(row, addresses, ct)));
        return result;
    }

    private async Task<CooperationAgreementDto> ToDtoAsync(SupplierAgreement a, CancellationToken ct) =>
        ToDto(a,
            await _tenantNames.GetTenantDisplayNameAsync(a.SupplierTenantId, ct) ?? string.Empty,
            await _tenantNames.GetTenantDisplayNameAsync(a.ClientTenantId, ct) ?? string.Empty,
            await GetSupplierLegalAddressAsync(a, ct));

    private async Task<string> GetNameCachedAsync(
        Guid tenantId, Dictionary<Guid, string> cache, CancellationToken ct)
    {
        if (cache.TryGetValue(tenantId, out var name)) return name;
        name = await _tenantNames.GetTenantDisplayNameAsync(tenantId, ct) ?? string.Empty;
        cache[tenantId] = name;
        return name;
    }

    /// <summary>
    /// Supplier's legal address (for the client's "physical" signing option) —
    /// only fetched for agreements where it is actually relevant
    /// (awaiting_signature / active), to avoid pointless DB round-trips for
    /// pending/rejected/terminated rows.
    /// </summary>
    private async Task<string?> GetSupplierAddressCachedAsync(
        SupplierAgreement a, Dictionary<Guid, string?> cache, CancellationToken ct)
    {
        if (a.Status is not (SupplierAgreementStatus.AwaitingSignature or SupplierAgreementStatus.Active))
            return null;

        if (cache.TryGetValue(a.SupplierTenantId, out var address)) return address;
        var settings = await _settings.GetByTenantAsync(a.SupplierTenantId, ct);
        address = settings?.LegalAddress;
        cache[a.SupplierTenantId] = address;
        return address;
    }

    private async Task<string?> GetSupplierLegalAddressAsync(SupplierAgreement a, CancellationToken ct)
    {
        if (a.Status is not (SupplierAgreementStatus.AwaitingSignature or SupplierAgreementStatus.Active))
            return null;

        var settings = await _settings.GetByTenantAsync(a.SupplierTenantId, ct);
        return settings?.LegalAddress;
    }

    private static CooperationAgreementDto ToDto(
        SupplierAgreement a, string supplierName, string clientName, string? supplierLegalAddress) =>
        new(
            a.Id,
            a.SupplierTenantId,
            a.ClientTenantId,
            supplierName,
            clientName,
            a.Status,
            a.RequestMessage,
            a.RejectionReason,
            a.ContractNumber,
            !string.IsNullOrEmpty(a.ContractFilePath),
            a.VchasnoDocumentId,
            a.RequestedAt,
            a.DecidedAt,
            a.SignedAt,
            a.TerminatedAt,
            a.SigningMethod,
            a.SigningEmail,
            supplierLegalAddress);

    private static SupplierContractSettingsDto ToSettingsDto(SupplierContractSettings s) =>
        new(
            s.LegalName, s.Edrpou, s.Iban, s.BankName, s.LegalAddress, s.DirectorName,
            s.Phone, s.Email, s.ServiceName, s.ServiceDescription,
            s.SignatureImageUrl, s.StampImageUrl, s.IsVatPayer, s.UpdatedAt);
}
