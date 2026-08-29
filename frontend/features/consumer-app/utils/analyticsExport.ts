export interface AnalyticsExportRow {
  type: string;
  title: string;
  reach: number;
  interactions: number;
  purchases: number | null;
  conversionPercent: number;
  revenue: number | null;
}

export interface AnalyticsDailyExportRow {
  type: string;
  title: string;
  date: string;
  reach: number;
  interactions: number;
  purchases: number;
  revenue: number;
}

export interface AnalyticsStoreExportRow {
  type: string;
  title: string;
  storeName: string;
  reach: number;
  interactions: number;
  purchases: number;
  revenue: number;
}

export interface AnalyticsProductExportRow {
  type: string;
  title: string;
  productName: string;
  interactions: number;
  purchases: number;
  revenue: number;
}

export interface AnalyticsExportPayload {
  from: string;
  to: string;
  storeScope: string;
  contentType: string;
  generatedAt: Date;
  summary: Array<{ metric: string; value: number; format: "number" | "currency" | "percent" }>;
  content: AnalyticsExportRow[];
  daily: AnalyticsDailyExportRow[];
  stores: AnalyticsStoreExportRow[];
  products: AnalyticsProductExportRow[];
  audience: Array<{ segment: string; reach: number; interactions: number; purchases: number; revenue: number }>;
}

function safeFileName(value: string) {
  return value.replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-|-$/g, "");
}

function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}

function csvCell(value: string | number | null) {
  if (value === null) return "";
  const normalized = String(value).replace(/"/g, '""');
  return `"${normalized}"`;
}

export function exportAnalyticsCsv(payload: AnalyticsExportPayload) {
  const rows: Array<Array<string | number | null>> = [
    ["Звіт аналітики мобільного застосунку"],
    ["Період", `${payload.from} — ${payload.to}`],
    ["Магазини", payload.storeScope],
    ["Тип контенту", payload.contentType],
    [],
    ["Тип", "Назва", "Охоплення", "Взаємодії", "Покупки/чеки", "Конверсія, %", "Дохід, грн"],
    ...payload.content.map((row) => [row.type, row.title, row.reach, row.interactions, row.purchases, row.conversionPercent, row.revenue]),
  ];
  const csv = `\uFEFF${rows.map((row) => row.map(csvCell).join(";")).join("\r\n")}`;
  downloadBlob(new Blob([csv], { type: "text/csv;charset=utf-8" }), `consumer-app-analytics-${safeFileName(payload.from)}-${safeFileName(payload.to)}.csv`);
}

export async function exportAnalyticsXlsx(payload: AnalyticsExportPayload) {
  const ExcelJS = await import("exceljs");
  const workbook = new ExcelJS.Workbook();
  workbook.creator = "CRM Product Systems";
  workbook.created = payload.generatedAt;
  workbook.modified = payload.generatedAt;

  const headerFill = { type: "pattern", pattern: "solid", fgColor: { argb: "FF1D4ED8" } } as const;
  const headerFont = { bold: true, color: { argb: "FFFFFFFF" } };
  const sectionFill = { type: "pattern", pattern: "solid", fgColor: { argb: "FFE8EEF8" } } as const;
  const border = { bottom: { style: "thin", color: { argb: "FFD7DEEA" } } } as const;

  const styleHeader = (sheet: any, rowNumber: number) => {
    const row = sheet.getRow(rowNumber);
    row.eachCell((cell: any) => {
      cell.fill = headerFill;
      cell.font = headerFont;
      cell.alignment = { vertical: "middle", horizontal: "left" };
    });
    row.height = 22;
    sheet.views = [{ state: "frozen", ySplit: rowNumber }];
    sheet.autoFilter = { from: { row: rowNumber, column: 1 }, to: { row: rowNumber, column: row.cellCount } };
  };

  const summary = workbook.addWorksheet("Підсумок", { views: [{ showGridLines: false }] });
  summary.columns = [{ width: 34 }, { width: 24 }];
  summary.mergeCells("A1:B1");
  summary.getCell("A1").value = "Аналітика мобільного застосунку";
  summary.getCell("A1").font = { bold: true, size: 18, color: { argb: "FF17365D" } };
  summary.getCell("A3").value = "Період"; summary.getCell("B3").value = `${payload.from} — ${payload.to}`;
  summary.getCell("A4").value = "Магазини"; summary.getCell("B4").value = payload.storeScope;
  summary.getCell("A5").value = "Тип контенту"; summary.getCell("B5").value = payload.contentType;
  summary.getCell("A6").value = "Сформовано"; summary.getCell("B6").value = payload.generatedAt;
  summary.getCell("B6").numFmt = "yyyy-mm-dd hh:mm";
  summary.getRow(8).values = ["Показник", "Значення"];
  styleHeader(summary, 8);
  payload.summary.forEach((item) => {
    const row = summary.addRow([item.metric, item.value]);
    row.getCell(2).numFmt = item.format === "currency" ? '#,##0.00 "грн"' : item.format === "percent" ? "0.00%" : "#,##0";
    row.eachCell((cell) => { cell.border = border; });
  });

  const addSheet = (name: string, headers: string[], widths: number[], rows: Array<Array<string | number | null>>, currencyColumns: number[] = [], percentColumns: number[] = []) => {
    const sheet = workbook.addWorksheet(name, { views: [{ showGridLines: false }] });
    sheet.columns = widths.map((width) => ({ width }));
    sheet.addRow(headers);
    styleHeader(sheet, 1);
    rows.forEach((values) => {
      const row = sheet.addRow(values);
      currencyColumns.forEach((column) => { row.getCell(column).numFmt = '#,##0.00 "грн"'; });
      percentColumns.forEach((column) => { row.getCell(column).numFmt = "0.00%"; });
      row.eachCell((cell) => { cell.border = border; cell.alignment = { vertical: "top", wrapText: true }; });
    });
    if (rows.length === 0) {
      sheet.addRow(["За вибраними фільтрами даних немає"]);
      sheet.getRow(2).getCell(1).fill = sectionFill;
    }
    return sheet;
  };

  addSheet("Контент", ["Тип", "Назва", "Охоплення", "Взаємодії", "Покупки/чеки", "Конверсія", "Дохід"], [16, 38, 16, 16, 16, 14, 18], payload.content.map((row) => [row.type, row.title, row.reach, row.interactions, row.purchases, row.conversionPercent / 100, row.revenue]), [7], [6]);
  addSheet("Динаміка", ["Тип", "Контент", "Дата", "Охоплення", "Взаємодії", "Покупки/чеки", "Дохід"], [16, 38, 14, 16, 16, 16, 18], payload.daily.map((row) => [row.type, row.title, row.date, row.reach, row.interactions, row.purchases, row.revenue]), [7]);
  addSheet("Магазини", ["Тип", "Контент", "Магазин", "Охоплення", "Взаємодії", "Покупки/чеки", "Дохід"], [16, 34, 30, 16, 16, 16, 18], payload.stores.map((row) => [row.type, row.title, row.storeName, row.reach, row.interactions, row.purchases, row.revenue]), [7]);
  addSheet("Товари", ["Тип", "Контент", "Товар", "Взаємодії", "Продано", "Дохід"], [16, 34, 42, 16, 14, 18], payload.products.map((row) => [row.type, row.title, row.productName, row.interactions, row.purchases, row.revenue]), [6]);
  addSheet("Аудиторії", ["Сегмент", "Охоплення", "Взаємодії", "Покупки/чеки", "Дохід"], [34, 18, 18, 18, 20], payload.audience.map((row) => [row.segment, row.reach, row.interactions, row.purchases, row.revenue]), [5]);

  const buffer = await workbook.xlsx.writeBuffer();
  downloadBlob(new Blob([new Uint8Array(buffer)], { type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }), `consumer-app-analytics-${safeFileName(payload.from)}-${safeFileName(payload.to)}.xlsx`);
}
