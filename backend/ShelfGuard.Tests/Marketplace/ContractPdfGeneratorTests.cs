using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Infrastructure.Documents;
using Xunit;

namespace ShelfGuard.Tests.Marketplace;

/// <summary>
/// TASK-317: smoke test of the real QuestPDF contract renderer — verifies it
/// produces a valid non-trivial PDF from fully-Ukrainian input (the bundled
/// DejaVu Sans fonts must resolve, otherwise QuestPDF renders □□□ or throws).
/// </summary>
public sealed class ContractPdfGeneratorTests
{
    [Fact]
    public void Generate_UkrainianData_ProducesValidPdf()
    {
        var sut = new ContractPdfGenerator();

        var data = new ContractPdfData(
            ContractNumber:      "ДС-2026-001",
            Date:                DateTimeOffset.UtcNow,
            SupplierDisplayName: "Постачальник Тест",
            ClientDisplayName:   "ТОВ «Магазин Український»",
            LegalName:           "ТОВ «Постачальник Тест»",
            Edrpou:              "12345678",
            Iban:                "UA213223130000026007233566001",
            BankName:            "АТ КБ «ПриватБанк»",
            LegalAddress:        "м. Київ, вул. Хрещатик, 1",
            DirectorName:        "Іваненко Іван Іванович",
            Phone:               "+380501234567",
            Email:               "supplier@example.com",
            ServiceName:         "Постачання молочної продукції",
            ServiceDescription:  "Щотижневі поставки згідно із замовленнями",
            IsVatPayer:          true,
            SignatureImage:      null,
            StampImage:          null);

        var pdf = sut.Generate(data);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 1000, $"PDF suspiciously small: {pdf.Length} bytes");
        // %PDF- magic bytes
        Assert.Equal(0x25, pdf[0]);
        Assert.Equal(0x50, pdf[1]);
        Assert.Equal(0x44, pdf[2]);
        Assert.Equal(0x46, pdf[3]);
    }

    [Fact]
    public void Generate_WithImages_EmbedsThem()
    {
        var sut = new ContractPdfGenerator();

        var baseline = sut.Generate(MinimalData(null, null));
        var withImages = sut.Generate(MinimalData(OnePixelPng(), OnePixelPng()));

        Assert.True(withImages.Length > 0);
        Assert.NotEqual(baseline.Length, withImages.Length);
    }

    private static ContractPdfData MinimalData(byte[]? signature, byte[]? stamp) => new(
        "ДС-2026-002", DateTimeOffset.UtcNow, "Постачальник", "Замовник",
        "ТОВ «Тест»", null, "UA000000000000000000000000000", null, null,
        null, null, null, "Товари", null, false, signature, stamp);

    /// <summary>Smallest valid 1×1 transparent PNG.</summary>
    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
}
