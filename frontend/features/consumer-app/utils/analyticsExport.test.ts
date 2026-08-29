import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { exportAnalyticsCsv, exportAnalyticsXlsx, type AnalyticsExportPayload } from "./analyticsExport";

const payload: AnalyticsExportPayload = {
  from: "2026-08-01",
  to: "2026-08-31",
  storeScope: "Усі магазини",
  contentType: "Усі типи",
  generatedAt: new Date("2026-08-31T12:00:00Z"),
  summary: [{ metric: "Дохід", value: 1250.5, format: "currency" }],
  content: [{ type: "Акція", title: "Серпнева пропозиція", reach: 100, interactions: 20, purchases: 5, conversionPercent: 5, revenue: 1250.5 }],
  daily: [{ type: "Акція", title: "Серпнева пропозиція", date: "2026-08-15", reach: 20, interactions: 4, purchases: 1, revenue: 250.1 }],
  stores: [{ type: "Акція", title: "Серпнева пропозиція", storeName: "Центральний", reach: 100, interactions: 20, purchases: 5, revenue: 1250.5 }],
  products: [{ type: "Акція", title: "Серпнева пропозиція", productName: "Тестовий товар", interactions: 0, purchases: 5, revenue: 1250.5 }],
  audience: [{ segment: "Учасники програми лояльності", reach: 100, interactions: 20, purchases: 5, revenue: 1250.5 }],
};

describe("analytics export", () => {
  const blobs: Blob[] = [];

  beforeEach(() => {
    blobs.length = 0;
    vi.stubGlobal("URL", {
      createObjectURL: vi.fn((blob: Blob) => { blobs.push(blob); return "blob:test"; }),
      revokeObjectURL: vi.fn(),
    });
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => undefined);
  });

  afterEach(() => { vi.restoreAllMocks(); });

  it("creates an Excel-compatible UTF-8 CSV", async () => {
    exportAnalyticsCsv(payload);
    expect(blobs).toHaveLength(1);
    expect(blobs[0].type).toContain("text/csv");
    const bytes = await new Promise<Uint8Array>((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(new Uint8Array(reader.result as ArrayBuffer));
      reader.onerror = () => reject(reader.error);
      reader.readAsArrayBuffer(blobs[0]);
    });
    const text = new TextDecoder("utf-8").decode(bytes);
    expect(text).toContain("Звіт аналітики мобільного застосунку");
    expect(text).toContain("Серпнева пропозиція");
    expect(Array.from(bytes.slice(0, 3))).toEqual([0xef, 0xbb, 0xbf]);
  });

  it("creates a non-empty XLSX workbook", async () => {
    await exportAnalyticsXlsx(payload);
    expect(blobs).toHaveLength(1);
    expect(blobs[0].type).toBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    expect(blobs[0].size).toBeGreaterThan(5_000);
    const bytes = await new Promise<Uint8Array>((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(new Uint8Array(reader.result as ArrayBuffer));
      reader.onerror = () => reject(reader.error);
      reader.readAsArrayBuffer(blobs[0]);
    });
    const ExcelJS = await import("exceljs");
    const workbook = new ExcelJS.Workbook();
    await workbook.xlsx.load(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer);
    expect(workbook.worksheets.map((sheet) => sheet.name)).toEqual(["Підсумок", "Контент", "Динаміка", "Магазини", "Товари", "Аудиторії"]);
    expect(workbook.getWorksheet("Аудиторії")?.getCell("A2").value).toBe("Учасники програми лояльності");
  }, 60_000);
});
