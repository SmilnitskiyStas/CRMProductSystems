namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// Everything the PDF generator needs to render a cooperation contract —
/// supplier requisites snapshot + party display names. Image bytes are
/// pre-loaded by the caller (the generator stays IO-free).
/// Client*-prefixed fields (TASK-327) are optional: populated only when the
/// client selected one of their own registered legal entities
/// (SupplierAgreement.ClientLegalEntityId) when requesting cooperation; when
/// null, the contract keeps rendering only ClientDisplayName as before.
/// DeliveryCoverage* fields (TASK-652) are optional: the supplier's declared
/// delivery coverage, with region codes ALREADY resolved to Ukrainian names by
/// the caller (SupplierAgreementService) so the generator stays IO-/lookup-free.
/// The "5. РЕГІОНИ ТА УМОВИ ДОСТАВКИ" section renders only when
/// DeliveryCoverageServed is non-empty.
/// </summary>
public record ContractPdfData(
    string ContractNumber,
    DateTimeOffset Date,
    string SupplierDisplayName,
    string ClientDisplayName,
    string LegalName,
    string? Edrpou,
    string? Iban,
    string? BankName,
    string? LegalAddress,
    string? DirectorName,
    string? Phone,
    string? Email,
    string? ServiceName,
    string? ServiceDescription,
    bool IsVatPayer,
    byte[]? SignatureImage,
    byte[]? StampImage,
    string? ClientLegalName = null,
    string? ClientEdrpou = null,
    string? ClientIban = null,
    string? ClientBankName = null,
    string? ClientLegalAddress = null,
    string? ClientDirectorName = null,
    bool ClientIsVatPayer = false,
    IReadOnlyList<ContractDeliveryRegion>? DeliveryCoverageServed = null,
    IReadOnlyList<string>? DeliveryCoverageNotServed = null,   // resolved region NAMES, not codes
    string? DeliveryCoverageNote = null);

/// <summary>
/// One served delivery region for the cooperation contract's coverage section
/// (TASK-652). <paramref name="RegionName"/> is the resolved Ukrainian display
/// name (never a raw code); <paramref name="Terms"/> is the supplier's optional
/// free-text delivery terms for that region.
/// </summary>
public record ContractDeliveryRegion(string RegionName, string? Terms);

/// <summary>
/// Renders the cooperation contract PDF («ДОГОВІР ПРО СПІВПРАЦЮ») from supplier
/// requisites (TASK-317). Implementation lives in ShelfGuard.Infrastructure
/// (QuestPDF) — never couple document rendering to business logic.
/// </summary>
public interface IContractPdfGenerator
{
    byte[] Generate(ContractPdfData data);
}
