using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Application.Features.Marketplace.Vchasno;
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

    public const int MaxMessageLength = 2000;

    private static readonly string[] AllowedImageExtensions = [".png", ".jpg", ".jpeg"];

    private readonly ISupplierAgreementRepository _agreements;
    private readonly ISupplierContractSettingsRepository _settings;
    private readonly IMarketplaceRepository _marketplace;
    private readonly ISupplierChatRepository _tenantNames;
    private readonly IContractPdfGenerator _pdf;
    private readonly IVchasnoClientFactory _vchasno;

    public SupplierAgreementService(
        ISupplierAgreementRepository agreements,
        ISupplierContractSettingsRepository settings,
        IMarketplaceRepository marketplace,
        ISupplierChatRepository tenantNames,
        IContractPdfGenerator pdf,
        IVchasnoClientFactory vchasno)
    {
        _agreements  = agreements;
        _settings    = settings;
        _marketplace = marketplace;
        _tenantNames = tenantNames;
        _pdf         = pdf;
        _vchasno     = vchasno;
    }

    // ── Client side ───────────────────────────────────────────────────────────

    public async Task<(CooperationAgreementDto? Agreement, string? Error, bool IsDuplicate)> SubmitRequestAsync(
        Guid clientTenantId, Guid supplierId, string? message, Guid userId,
        CancellationToken ct = default)
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

        // One live agreement per pair (partial unique index) — surface the current
        // status so the client UI can explain why a new request is not possible.
        var existing = await _agreements.GetForPairAsync(supplierTenantId.Value, clientTenantId, ct);
        if (existing is not null)
            return (null, $"Заявка на співпрацю вже існує (статус: {existing.Status}).", true);

        var agreement = new SupplierAgreement
        {
            SupplierTenantId = supplierTenantId.Value,
            ClientTenantId   = clientTenantId,
            Status           = SupplierAgreementStatus.Pending,
            RequestMessage   = string.IsNullOrEmpty(trimmedMessage) ? null : trimmedMessage,
            CreatedByUserId  = userId,
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
                $"{agreement.ContractNumber}.pdf", pdfBytes, recipientEdrpou: null, title, ct);
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
        await _agreements.SaveChangesAsync(ct);

        return (await ToDtoAsync(agreement, ct), null);
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
            ReadImageSafe(settings.StampImageUrl));

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
        var result = new List<CooperationAgreementDto>(rows.Count);
        foreach (var row in rows)
            result.Add(ToDto(row,
                await GetNameCachedAsync(row.SupplierTenantId, names, ct),
                await GetNameCachedAsync(row.ClientTenantId, names, ct)));
        return result;
    }

    private async Task<CooperationAgreementDto> ToDtoAsync(SupplierAgreement a, CancellationToken ct) =>
        ToDto(a,
            await _tenantNames.GetTenantDisplayNameAsync(a.SupplierTenantId, ct) ?? string.Empty,
            await _tenantNames.GetTenantDisplayNameAsync(a.ClientTenantId, ct) ?? string.Empty);

    private async Task<string> GetNameCachedAsync(
        Guid tenantId, Dictionary<Guid, string> cache, CancellationToken ct)
    {
        if (cache.TryGetValue(tenantId, out var name)) return name;
        name = await _tenantNames.GetTenantDisplayNameAsync(tenantId, ct) ?? string.Empty;
        cache[tenantId] = name;
        return name;
    }

    private static CooperationAgreementDto ToDto(
        SupplierAgreement a, string supplierName, string clientName) =>
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
            a.TerminatedAt);

    private static SupplierContractSettingsDto ToSettingsDto(SupplierContractSettings s) =>
        new(
            s.LegalName, s.Edrpou, s.Iban, s.BankName, s.LegalAddress, s.DirectorName,
            s.Phone, s.Email, s.ServiceName, s.ServiceDescription,
            s.SignatureImageUrl, s.StampImageUrl, s.IsVatPayer, s.UpdatedAt);
}
