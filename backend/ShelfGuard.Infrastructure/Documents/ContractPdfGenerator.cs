using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ShelfGuard.Application.Features.Marketplace;

namespace ShelfGuard.Infrastructure.Documents;

/// <summary>
/// QuestPDF renderer for the cooperation contract «ДОГОВІР ПРО СПІВПРАЦЮ»
/// (TASK-317). Uses the bundled DejaVu Sans fonts (Fonts/*.ttf, copied to the
/// output directory) — default PDF fonts lack Ukrainian glyphs and would
/// render □□□.
/// </summary>
public sealed class ContractPdfGenerator : IContractPdfGenerator
{
    private const string FontFamily = "DejaVu Sans";

    static ContractPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;

        // Fonts ship next to the assembly (CopyToOutputDirectory). Probe the app
        // base dir first, then the repo-relative path used by `dotnet run`.
        foreach (var fileName in new[] { "DejaVuSans.ttf", "DejaVuSans-Bold.ttf" })
        {
            var path = ResolveFontPath(fileName);
            if (path is null) continue;
            using var stream = File.OpenRead(path);
            FontManager.RegisterFont(stream);
        }
    }

    private static string? ResolveFontPath(string fileName)
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Fonts", fileName),
            Path.Combine("Fonts", fileName),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    public byte[] Generate(ContractPdfData data)
    {
        var dateText = data.Date.ToString("dd.MM.yyyy");

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontFamily(FontFamily).FontSize(10.5f));

                page.Header().Column(col =>
                {
                    col.Item().AlignCenter().Text($"ДОГОВІР ПРО СПІВПРАЦЮ № {data.ContractNumber}")
                        .FontSize(14).Bold();
                    col.Item().PaddingTop(6).Row(row =>
                    {
                        row.RelativeItem().Text("м. ______________");
                        row.RelativeItem().AlignRight().Text(dateText);
                    });
                    col.Item().PaddingTop(4).LineHorizontal(0.75f).LineColor(Colors.Grey.Medium);
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(8);

                    // ── Сторони ─────────────────────────────────────────────
                    col.Item().Text(text =>
                    {
                        text.Span($"{data.LegalName}");
                        text.Span(data.DirectorName is { Length: > 0 }
                            ? $", в особі {data.DirectorName}, "
                            : ", ");
                        text.Span("надалі — «Постачальник», з однієї сторони, та ");
                        text.Span($"{data.ClientDisplayName}");
                        text.Span(", надалі — «Замовник», з іншої сторони, разом — «Сторони», уклали цей Договір про наступне:");
                    });

                    // ── 1. Предмет договору ─────────────────────────────────
                    Section(col, "1. ПРЕДМЕТ ДОГОВОРУ");
                    col.Item().Text(
                        $"1.1. Постачальник зобов'язується постачати Замовнику: {data.ServiceName}" +
                        (data.ServiceDescription is { Length: > 0 } ? $" — {data.ServiceDescription}" : string.Empty) +
                        ", а Замовник зобов'язується приймати та оплачувати їх на умовах цього Договору.");
                    col.Item().Text(
                        "1.2. Найменування, асортимент, кількість та ціна кожної партії визначаються " +
                        "у замовленнях Замовника, розміщених через платформу ShelfGuard Marketplace.");

                    // ── 2. Порядок розрахунків ──────────────────────────────
                    Section(col, "2. ПОРЯДОК РОЗРАХУНКІВ");
                    col.Item().Text(
                        "2.1. Розрахунки здійснюються у безготівковій формі на поточний рахунок Постачальника, " +
                        "зазначений у реквізитах цього Договору.");
                    col.Item().Text(data.IsVatPayer
                        ? "2.2. Ціни включають ПДВ. Постачальник є платником ПДВ."
                        : "2.2. Постачальник не є платником ПДВ.");

                    // ── 3. Строк дії ────────────────────────────────────────
                    Section(col, "3. СТРОК ДІЇ ДОГОВОРУ");
                    col.Item().Text(
                        "3.1. Договір набирає чинності з моменту його підписання обома Сторонами та діє до моменту " +
                        "його розірвання за ініціативою однієї зі Сторін.");
                    col.Item().Text(
                        "3.2. Кожна зі Сторін має право розірвати Договір, попередивши іншу Сторону не пізніше ніж " +
                        "за 14 календарних днів.");

                    // ── 4. Реквізити ────────────────────────────────────────
                    Section(col, "4. РЕКВІЗИТИ ПОСТАЧАЛЬНИКА");
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cd =>
                        {
                            cd.ConstantColumn(150);
                            cd.RelativeColumn();
                        });

                        void Row(string label, string? value)
                        {
                            if (string.IsNullOrEmpty(value)) return;
                            table.Cell().PaddingVertical(2).Text(label).SemiBold();
                            table.Cell().PaddingVertical(2).Text(value);
                        }

                        Row("Юридична назва:", data.LegalName);
                        Row("ЄДРПОУ / ІПН:", data.Edrpou);
                        Row("IBAN:", data.Iban);
                        Row("Банк:", data.BankName);
                        Row("Юридична адреса:", data.LegalAddress);
                        Row("Телефон:", data.Phone);
                        Row("Email:", data.Email);
                        Row("ПДВ-статус:", data.IsVatPayer ? "платник ПДВ" : "неплатник ПДВ");
                    });

                    // ── 5. Підписи ──────────────────────────────────────────
                    Section(col, "5. ПІДПИСИ СТОРІН");
                    col.Item().PaddingTop(10).Row(row =>
                    {
                        row.RelativeItem().Column(side =>
                        {
                            side.Item().Text("ПОСТАЧАЛЬНИК").Bold();
                            side.Item().PaddingTop(2).Text(data.LegalName);
                            if (data.DirectorName is { Length: > 0 })
                                side.Item().Text(data.DirectorName);

                            // Signature with the stamp overlaid next to it, when provided.
                            side.Item().PaddingTop(6).Row(sig =>
                            {
                                if (data.SignatureImage is { Length: > 0 })
                                    sig.ConstantItem(120).MaxHeight(60)
                                        .Image(data.SignatureImage).FitArea();
                                else
                                    sig.ConstantItem(120).PaddingTop(30)
                                        .LineHorizontal(0.75f).LineColor(Colors.Black);

                                if (data.StampImage is { Length: > 0 })
                                    sig.ConstantItem(90).PaddingLeft(8).MaxHeight(90)
                                        .Image(data.StampImage).FitArea();
                            });
                            side.Item().Text("(підпис, печатка)").FontSize(8)
                                .FontColor(Colors.Grey.Medium);
                        });

                        row.ConstantItem(30);

                        row.RelativeItem().Column(side =>
                        {
                            side.Item().Text("ЗАМОВНИК").Bold();
                            side.Item().PaddingTop(2).Text(data.ClientDisplayName);
                            side.Item().PaddingTop(48).LineHorizontal(0.75f).LineColor(Colors.Black);
                            side.Item().Text("(підпис)").FontSize(8).FontColor(Colors.Grey.Medium);
                        });
                    });
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Medium));
                    t.Span($"Договір № {data.ContractNumber} · сформовано платформою ShelfGuard · {dateText} · стор. ");
                    t.CurrentPageNumber();
                    t.Span(" з ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    private static void Section(ColumnDescriptor col, string title) =>
        col.Item().PaddingTop(6).Text(title).FontSize(11).Bold();
}
