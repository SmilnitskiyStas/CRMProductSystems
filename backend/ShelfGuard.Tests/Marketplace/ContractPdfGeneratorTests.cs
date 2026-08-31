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

    // ── TASK-652: delivery-coverage section ───────────────────────────────────

    [Fact]
    public void Generate_WithDeliveryCoverage_RendersRegionsSection()
    {
        var sut = new ContractPdfGenerator();

        var baseline = sut.Generate(MinimalData(null, null));
        var withCoverage = sut.Generate(MinimalData(null, null) with
        {
            DeliveryCoverageServed = new[]
            {
                new ContractDeliveryRegion("Київська", "2-3 дні, від 5000 грн"),
                new ContractDeliveryRegion("Житомир", null),
            },
            DeliveryCoverageNotServed = new[] { "Автономна Республіка Крим" },
            DeliveryCoverageNote = "Доставка Новою Поштою за домовленістю",
        });

        // Valid PDF (fonts must resolve — QuestPDF glyph-checks the Ukrainian
        // region names / section title, otherwise it throws).
        Assert.True(withCoverage.Length > 1000, $"PDF suspiciously small: {withCoverage.Length} bytes");
        Assert.Equal(0x25, withCoverage[0]);
        Assert.Equal(0x50, withCoverage[1]);
        Assert.Equal(0x44, withCoverage[2]);
        Assert.Equal(0x46, withCoverage[3]);

        // The extra section (lead line + 2-row table + 5.2 + 5.3) adds content.
        Assert.True(withCoverage.Length > baseline.Length,
            $"expected coverage PDF ({withCoverage.Length}) larger than baseline ({baseline.Length})");
    }

    [Fact]
    public void Generate_ServedWithoutNotServedOrNote_StillRendersSection()
    {
        var sut = new ContractPdfGenerator();

        var servedOnly = sut.Generate(MinimalData(null, null) with
        {
            DeliveryCoverageServed = new[] { new ContractDeliveryRegion("Львівська", null) },
        });
        var servedPlusExtras = sut.Generate(MinimalData(null, null) with
        {
            DeliveryCoverageServed = new[] { new ContractDeliveryRegion("Львівська", null) },
            DeliveryCoverageNotServed = new[] { "Севастополь" },
            DeliveryCoverageNote = "Мінімальне замовлення — 3000 грн.",
        });

        Assert.True(servedOnly.Length > 1000);
        Assert.Equal(0x25, servedOnly[0]);
        // 5.2 + 5.3 lines only render when their data is present.
        Assert.True(servedPlusExtras.Length > servedOnly.Length);
    }

    [Fact]
    public void Generate_NullDeliveryCoverage_OmitsSection_KeepsSignatures()
    {
        var sut = new ContractPdfGenerator();

        // No coverage args → section absent; the rest of the contract (incl. the
        // signatures block, now numbered 6) still renders a valid PDF.
        var pdf = sut.Generate(MinimalData(null, null));

        Assert.True(pdf.Length > 1000, $"PDF suspiciously small: {pdf.Length} bytes");
        Assert.Equal(0x25, pdf[0]);
        Assert.Equal(0x50, pdf[1]);
        Assert.Equal(0x44, pdf[2]);
        Assert.Equal(0x46, pdf[3]);
    }

    private static ContractPdfData MinimalData(byte[]? signature, byte[]? stamp) => new(
        "ДС-2026-002", DateTimeOffset.UtcNow, "Постачальник", "Замовник",
        "ТОВ «Тест»", null, "UA000000000000000000000000000", null, null,
        null, null, null, "Товари", null, false, signature, stamp);

    /// <summary>Smallest valid 1×1 transparent PNG.</summary>
    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
}
