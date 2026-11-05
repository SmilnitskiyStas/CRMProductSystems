import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { NextIntlClientProvider } from "next-intl";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { StockWriteOffDialog } from "./StockWriteOffDialog";
import { StockTable } from "./StockTable";
import { CreateWriteOffForm } from "@/features/write-offs/components/CreateWriteOffForm";
import { canWriteOffBatch, getWriteOffBatches, shareWriteOffStore } from "../lib/stockWriteOff";
import type { ProductStockDto } from "../types";
import uk from "@/messages/uk.json";

const mocks = vi.hoisted(() => ({ create: vi.fn(), approve: vi.fn(), onCompleted: vi.fn() }));
vi.mock("@/features/write-offs/hooks/useWriteOffs", () => ({
  useCreateWriteOff: () => ({ mutateAsync: mocks.create, isPending: false }),
  useApproveWriteOff: () => ({ mutateAsync: mocks.approve, isPending: false }),
}));
vi.mock("@/features/locations/hooks/useLocations", () => ({ useLocations: () => ({ data: [{ id: "store-a", name: "Пчілка" }] }) }));
vi.mock("@/features/shelf/hooks/useStock", () => ({ useStock: () => ({ data: { items: [] }, isLoading: false }) }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));

function batch(id: string, quantity = 6.04, storeId = "store-a", daysLeft = 3): ProductStockDto {
  return { id, quantity, storeId, daysLeft, productId: "same-product", productName: "Айран", productBarcode: "4823005209720",
    storeName: "Пчілка", expiryDate: "2026-10-27", quantityInitial: 15, batchNumber: id, status: quantity > 0 ? "critical" : "sold_out",
    priceRetail: 50, pricePurchase: 30, defaultReimbursementType: null, defaultReimbursementValue: null,
    zoneId: null, zoneName: null, shelfNumber: null, sourceType: "receipt", addedAt: "2026-10-01", lastCheckedAt: "2026-10-24" };
}
const saved = { id: "saved-request", storeId: "store-a", storeName: "Пчілка", status: "pending_approval", items: [{ productStockId: "second" }] };
let container: HTMLDivElement;
let root: Root;
beforeEach(() => {
  (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
  vi.clearAllMocks();
  mocks.create.mockResolvedValue(saved); mocks.approve.mockResolvedValue({ ...saved, status: "approved" });
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
});
afterEach(() => { act(() => root.unmount()); container.remove(); });
function render(content: React.ReactNode) {
  act(() => root.render(<NextIntlClientProvider locale="uk" timeZone="Europe/Kyiv" messages={uk}>{content}</NextIntlClientProvider>));
}
async function submit(explanation = "Пошкоджено пакування товару") {
  const textarea = container.querySelector<HTMLTextAreaElement>("textarea");
  if (textarea) act(() => {
    Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")!.set!.call(textarea, explanation);
    textarea.dispatchEvent(new Event("input", { bubbles: true }));
  });
  await act(async () => { container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })); });
}
function button(text: string) { return [...container.querySelectorAll("button")].find(button => button.textContent === text)!; }

describe("write off exact store batches", () => {
  it("selects the stock row ID, not all batches sharing the barcode/product", () => {
    const rows = [batch("first"), batch("second"), batch("empty", 0)];
    expect(getWriteOffBatches(rows, new Set(["second"])).map(row => row.id)).toEqual(["second"]);
  });
  it("excludes depleted/archived batches and rejects mixed-store bulk requests", () => {
    expect(canWriteOffBatch(batch("empty", 0))).toBe(false);
    expect(canWriteOffBatch({ ...batch("old"), status: "archived" })).toBe(false);
    expect(shareWriteOffStore([batch("a"), batch("b", 2, "other-store")])).toBe(false);
    expect(shareWriteOffStore([])).toBe(false);
    expect(shareWriteOffStore([batch("a"), batch("b")])).toBe(true);
  });
  it("prefills the selected batch quantity/store and submits that exact ID", async () => {
    render(<CreateWriteOffForm initialBatches={[batch("second")]} onSuccess={vi.fn()} onCancel={vi.fn()} />);
    expect((container.querySelector("select") as HTMLSelectElement).value).toBe("store-a");
    expect((container.querySelector("select") as HTMLSelectElement).disabled).toBe(true);
    expect((container.querySelector('input[type="number"]') as HTMLInputElement).value).toBe("6.04");
    await submit();
    expect(mocks.create).toHaveBeenCalledWith(expect.objectContaining({ storeId: "store-a", reason: "other", items: [expect.objectContaining({ productStockId: "second", productId: "same-product", quantity: 6.04 })] }));
  });
  it("keeps two selected expiry batches of the same product separate", async () => {
    render(<CreateWriteOffForm initialBatches={[batch("first", 1), batch("second", 2)]} onSuccess={vi.fn()} onCancel={vi.fn()} />);
    await submit();
    expect(mocks.create.mock.calls[0][0].items.map((item: { productStockId: string }) => item.productStockId)).toEqual(["first", "second"]);
  });
  it.each(["", " ".repeat(30), "а".repeat(14), "а".repeat(101)])("rejects invalid manual explanation %j", async explanation => {
    render(<CreateWriteOffForm initialBatches={[batch("second")]} onSuccess={vi.fn()} onCancel={vi.fn()} />);
    await submit(explanation);
    expect(mocks.create).not.toHaveBeenCalled();
    expect(container.querySelector('[role="alert"]')?.textContent).toBe(uk.Dashboard.writeOffs.createForm.errorExplanationLength);
  });
  it.each([15, 100])("accepts a trimmed explanation with %i characters", async length => {
    render(<CreateWriteOffForm initialBatches={[batch("second")]} onSuccess={vi.fn()} onCancel={vi.fn()} />);
    await submit("  " + "а".repeat(length) + "  ");
    expect(mocks.create.mock.calls[0][0].notes).toBe("а".repeat(length));
  });
  it("defaults expired batches to the expired reason", async () => {
    render(<CreateWriteOffForm initialBatches={[batch("expired", 1, "store-a", -2)]} onSuccess={vi.fn()} onCancel={vi.fn()} />);
    await submit();
    expect(mocks.create.mock.calls[0][0].reason).toBe("expired");
    expect(container.querySelector("textarea")).toBeNull();
    expect(mocks.create.mock.calls[0][0].notes).toBeNull();
  });
  it("does not create a write-off for a zero-quantity batch", async () => {
    render(<CreateWriteOffForm initialBatches={[batch("empty", 0)]} onSuccess={vi.fn()} onCancel={vi.fn()} />);
    expect((container.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(true);
    await submit(); expect(mocks.create).not.toHaveBeenCalled();
  });
  it("requires explicit approval and retries the saved request without creating a duplicate", async () => {
    mocks.approve.mockRejectedValueOnce(new Error("Insufficient stock"));
    render(<StockWriteOffDialog batches={[batch("second")]} canApprove onClose={vi.fn()} onCompleted={mocks.onCompleted} />);
    await submit();
    expect(mocks.create).toHaveBeenCalledTimes(1); expect(mocks.approve).not.toHaveBeenCalled();
    await act(async () => button(uk.Dashboard.stock.writeOff.approve).click());
    expect(container.querySelector('[role="alert"]')?.textContent).toContain("Insufficient stock");
    expect(mocks.onCompleted).not.toHaveBeenCalled();
    await act(async () => button(uk.Dashboard.stock.writeOff.approve).click());
    expect(mocks.create).toHaveBeenCalledTimes(1);
    expect(mocks.approve).toHaveBeenLastCalledWith({ id: "saved-request" });
    expect(mocks.onCompleted).toHaveBeenCalledTimes(1);
  });
  it("leaves stock unchanged pending manager approval for non-managers", async () => {
    render(<StockWriteOffDialog batches={[batch("second")]} canApprove={false} onClose={vi.fn()} onCompleted={mocks.onCompleted} />);
    await submit();
    expect(container.querySelector('[role="status"]')?.textContent).toBe(uk.Dashboard.stock.writeOff.pendingApproval);
    expect(button(uk.Dashboard.stock.writeOff.approve)).toBeUndefined();
    expect(mocks.approve).not.toHaveBeenCalled(); expect(mocks.onCompleted).not.toHaveBeenCalled();
  });
  it("selects a single batch independently and disables actions on the empty batch", () => {
    const onSelect = vi.fn();
    const onWriteOff = vi.fn();
    render(<StockTable items={[batch("empty", 0), batch("second")]} isLoading={false} selectedIds={new Set()} onSelectId={onSelect} onSelectAll={vi.fn()} onWriteOff={onWriteOff} sortBy="expirydate" sortDescending={false} onSort={vi.fn()} />);
    const checkboxes = container.querySelectorAll<HTMLInputElement>('input[type="checkbox"]');
    expect(checkboxes[1].disabled).toBe(true);
    act(() => checkboxes[2].click()); expect(onSelect).toHaveBeenCalledWith("second", true);
    expect([...container.querySelectorAll("button")].some(button => button.textContent === uk.Dashboard.shelf.stockTable.writeOffBatch)).toBe(false);
    const triggers = [...container.querySelectorAll("svg.lucide-more-horizontal")].map(icon => icon.closest("button")!);
    act(() => triggers[0].click());
    let action = [...document.body.querySelectorAll("button")].find(button => button.textContent === uk.Dashboard.shelf.stockTable.writeOffBatch)!;
    expect(action.disabled).toBe(true);
    act(() => triggers[0].click());
    act(() => triggers[1].click());
    action = [...document.body.querySelectorAll("button")].find(button => button.textContent === uk.Dashboard.shelf.stockTable.writeOffBatch)!;
    expect(action.disabled).toBe(false);
    act(() => action.click());
    expect(onWriteOff).toHaveBeenCalledWith(expect.objectContaining({ id: "second" }));
    expect(container.textContent).toContain(uk.Dashboard.shelf.status.sold_out);
  });
});
