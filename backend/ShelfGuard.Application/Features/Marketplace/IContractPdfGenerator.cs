namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// Everything the PDF generator needs to render a cooperation contract —
/// supplier requisites snapshot + party display names. Image bytes are
/// pre-loaded by the caller (the generator stays IO-free).
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
    byte[]? StampImage);

/// <summary>
/// Renders the cooperation contract PDF («ДОГОВІР ПРО СПІВПРАЦЮ») from supplier
/// requisites (TASK-317). Implementation lives in ShelfGuard.Infrastructure
/// (QuestPDF) — never couple document rendering to business logic.
/// </summary>
public interface IContractPdfGenerator
{
    byte[] Generate(ContractPdfData data);
}
