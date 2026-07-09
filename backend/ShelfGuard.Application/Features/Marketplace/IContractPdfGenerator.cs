namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// Everything the PDF generator needs to render a cooperation contract —
/// supplier requisites snapshot + party display names. Image bytes are
/// pre-loaded by the caller (the generator stays IO-free).
/// Client*-prefixed fields (TASK-327) are optional: populated only when the
/// client selected one of their own registered legal entities
/// (SupplierAgreement.ClientLegalEntityId) when requesting cooperation; when
/// null, the contract keeps rendering only ClientDisplayName as before.
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
    bool ClientIsVatPayer = false);

/// <summary>
/// Renders the cooperation contract PDF («ДОГОВІР ПРО СПІВПРАЦЮ») from supplier
/// requisites (TASK-317). Implementation lives in ShelfGuard.Infrastructure
/// (QuestPDF) — never couple document rendering to business logic.
/// </summary>
public interface IContractPdfGenerator
{
    byte[] Generate(ContractPdfData data);
}
