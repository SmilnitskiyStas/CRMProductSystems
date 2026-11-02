import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { NextIntlClientProvider } from "next-intl";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { ApiError, markLoggedOut } from "@/lib/api";
import { InventoryAdjustmentWorkspace } from "./InventoryAdjustmentWorkspace";
import { createInventoryAdjustment, type InventoryAdjustmentCommand } from "../api/inventory-posting";
import { readAdjustment, saveAdjustment, type SavedAdjustment } from "../lib/inventoryAdjustmentSession";
import uk from "@/messages/uk.json";
import en from "@/messages/en.json";

const mocks = vi.hoisted(() => ({ status: vi.fn(), adjust: vi.fn(), stock: vi.fn() }));
vi.mock("../api/inventory-posting", async original => ({ ...await original<typeof import("../api/inventory-posting")>(),
  inventoryPostingApi: { status: mocks.status, adjust: mocks.adjust } }));
vi.mock("@/features/shelf/api/stock", () => ({ stockApi: { getById: mocks.stock } }));

const identity = { tenantId: "11111111-1111-4111-8111-111111111111", actorId: "22222222-2222-4222-8222-222222222222" };
const batch = { id: "33333333-3333-4333-8333-333333333333", storeId: "44444444-4444-4444-8444-444444444444",
  productName: "Айран", storeName: "Магазин", batchNumber: "A-1", expiryDate: "2026-11-01", quantity: 3.25, pricePurchase: 18.7 };
const messages = uk.Dashboard.stock.adjustment;
const status = { locationId: batch.storeId, configured: true, fenced: true, canAdjust: true };
function receipt(command: InventoryAdjustmentCommand, replayed = false) {
  return { ...command, tenantId: identity.tenantId, quantityBefore: command.expectedQuantity, quantityAfter: command.quantity,
    documentId: "55555555-5555-4555-8555-555555555555", postingId: "posting", entryId: "entry", eventId: "event",
    postedAt: "2026-10-26T12:00:00Z", replayed };
}
function pending(phase: SavedAdjustment["phase"] = "uncertain"): SavedAdjustment {
  return { version: 1, phase, batch, command: createInventoryAdjustment({ requestId: "66666666-6666-4666-8666-666666666666",
    locationId: batch.storeId, batchId: batch.id, expectedQuantity: "3.25", quantity: "5.00", reason: "Перераховано" }) };
}
let container: HTMLDivElement, root: Root, qc: QueryClient;
beforeEach(() => {
  (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
  vi.clearAllMocks(); sessionStorage.clear();
  mocks.status.mockResolvedValue(status); mocks.stock.mockResolvedValue({ ...batch, quantity: 6.5 });
  mocks.adjust.mockImplementation(async command => receipt(command));
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
  qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
});
afterEach(() => { act(() => root.unmount()); qc.clear(); container.remove(); vi.restoreAllMocks(); });
function render(selected: typeof batch | null = batch, who = identity, locale: "uk" | "en" = "uk", close = vi.fn()) {
  act(() => root.render(<QueryClientProvider client={qc}><NextIntlClientProvider locale={locale} timeZone="Europe/Kyiv" messages={locale === "uk" ? uk : en}>
    <InventoryAdjustmentWorkspace key={`${who.tenantId}:${who.actorId}`} {...who} selected={selected} onClose={close} />
  </NextIntlClientProvider></QueryClientProvider>));
}
async function until(test: () => boolean) {
  for (let i = 0; i < 100 && !test(); i++) await act(async () => { await new Promise(resolve => setTimeout(resolve, 10)); });
  expect(test()).toBe(true);
}
function input(label: string) { return container.querySelector<HTMLInputElement | HTMLTextAreaElement>(`[aria-label="${label}"]`)!; }
function set(label: string, value: string) {
  const element = input(label);
  act(() => { Object.getOwnPropertyDescriptor(element instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype, "value")!.set!.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true })); });
}
function button(label: string) { return [...container.querySelectorAll("button")].find(element => element.textContent === label); }
async function click(label: string) { await act(async () => { button(label)!.click(); }); }
async function ready() { render(); await until(() => !!input(messages.quantity) && !input(messages.quantity).disabled); }
async function submit(quantity = "5.00") {
  set(messages.quantity, quantity); set(messages.reason, "Перераховано товар");
  await act(async () => { container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })); });
}

it("posts only the selected batch with the freshly read expected count and invalidates history after a receipt", async () => {
  const invalidate = vi.spyOn(qc, "invalidateQueries"); await ready(); await submit("5,25");
  await until(() => container.textContent!.includes(messages.completed));
  expect(mocks.adjust.mock.calls[0][0]).toMatchObject({ batchId: batch.id, locationId: batch.storeId, expectedQuantity: "6.50", quantity: "5.25" });
  expect(mocks.adjust.mock.calls[0][0]).not.toHaveProperty("pricePurchase");
  expect(container.textContent).toContain("6.50 → 5.25");
  expect(invalidate).toHaveBeenCalledWith({ queryKey: ["product-stock-history"] });
  expect(readAdjustment(identity, sessionStorage)).toBeNull();
});
it.each(["-1", "5.001", "1e2", "100000000", "6.50"])("rejects invalid or unchanged count %s without sending", async quantity => {
  await ready(); await submit(quantity); expect(mocks.adjust).not.toHaveBeenCalled(); expect(container.textContent).toContain(messages.invalid);
});
it("accepts an explicit zero count", async () => {
  await ready(); await submit("0"); expect(mocks.adjust.mock.calls[0][0].quantity).toBe("0.00");
});
it.each([{ configured: false, fenced: false }, { configured: true, fenced: false }, { configured: false, fenced: true }])(
  "does not send when posting is unavailable: %j", async flags => {
    mocks.status.mockResolvedValue({ ...status, ...flags, canAdjust: false }); render();
    await until(() => mocks.status.mock.calls.length === 1 && !!button(messages.submit));
    expect(button(messages.submit)!.disabled).toBe(true); expect(mocks.adjust).not.toHaveBeenCalled();
  });
it("fails closed on an old API without the status endpoint", async () => {
  mocks.status.mockRejectedValue(new ApiError(404, "Missing endpoint")); render();
  await until(() => container.textContent!.includes(messages.loadFailed));
  expect(button(messages.submit)!.disabled).toBe(true); expect(mocks.adjust).not.toHaveBeenCalled();
});
it("blocks a changed batch location instead of applying the chosen row to another scope", async () => {
  mocks.stock.mockResolvedValue({ ...batch, storeId: "other" }); render();
  await until(() => container.textContent!.includes(messages.loadFailed)); expect(mocks.adjust).not.toHaveBeenCalled();
});
it("persists before sending, freezes uncertain input, and retries exactly the same command", async () => {
  mocks.adjust.mockImplementationOnce(async () => { expect(readAdjustment(identity, sessionStorage)?.phase).toBe("uncertain"); throw new TypeError("network lost"); });
  await ready(); await submit();
  const first = mocks.adjust.mock.calls[0][0]; const durable = readAdjustment(identity, sessionStorage)!;
  expect(durable.command).toEqual(first); expect(durable.batch).not.toHaveProperty("pricePurchase");
  expect(input(messages.quantity).readOnly).toBe(true); expect(button(messages.review)).toBeUndefined();
  await click(messages.retry); expect(mocks.adjust.mock.calls[1][0]).toEqual(first);
  await until(() => container.textContent!.includes(messages.completed));
});
it("recovers an unknown outcome after remount, keeps the original count, and shows the replay receipt", async () => {
  const saved = pending(); saveAdjustment(identity, saved, sessionStorage); mocks.adjust.mockResolvedValue(receipt(saved.command, true));
  render(null); await until(() => !!button(messages.resume)); await click(messages.resume);
  await until(() => !!button(messages.retry) && !button(messages.retry)!.disabled);
  expect(input(messages.expected).value).toBe("3.25"); await click(messages.retry);
  await until(() => container.textContent!.includes(messages.replayed));
  expect(container.querySelector('[role="dialog"]')).not.toBeNull(); expect(container.textContent).toContain("3.25 → 5.00");
  expect(mocks.adjust.mock.calls[0][0]).toEqual(saved.command);
});
it("does not replace an older unknown outcome when a retry is disabled or conflicts", async () => {
  saveAdjustment(identity, pending(), sessionStorage); mocks.adjust.mockRejectedValue(new ApiError(409, "Disabled", { code: "inventory_posting_disabled" }));
  await ready(); await click(messages.retry);
  expect(readAdjustment(identity, sessionStorage)?.phase).toBe("uncertain"); expect(button(messages.review)).toBeUndefined();
});
it("requires an explicit count review after a definite conflict before creating a new request", async () => {
  const saved = pending("rejected"); saveAdjustment(identity, saved, sessionStorage); mocks.stock.mockResolvedValue({ ...batch, quantity: 7 });
  await ready(); await click(messages.review);
  await until(() => readAdjustment(identity, sessionStorage) === null);
  expect(mocks.adjust).not.toHaveBeenCalled(); expect(input(messages.expected).value).toBe("7");
  await submit("8"); const command = mocks.adjust.mock.calls[0][0];
  expect(command.requestId).not.toBe(saved.command.requestId); expect(command.expectedQuantity).toBe("7.00");
});
it("keeps a first definite conflict editable only through explicit review", async () => {
  mocks.adjust.mockRejectedValueOnce(new ApiError(409, "Changed", { code: "inventory_posting_conflict" }));
  await ready(); await submit(); expect(readAdjustment(identity, sessionStorage)?.phase).toBe("rejected");
  expect(input(messages.quantity).readOnly).toBe(true); expect(button(messages.review)).toBeDefined();
});
it("keeps the same command after cancelled MFA and retries it", async () => {
  mocks.adjust.mockRejectedValueOnce(new ApiError(403, "MFA confirmation cancelled."));
  await ready(); await submit(); expect(container.textContent).toContain(messages.forbidden);
  const command = mocks.adjust.mock.calls[0][0]; await click(messages.retry); expect(mocks.adjust.mock.calls[1][0]).toEqual(command);
});
it("prevents double submission and close while waiting for confirmation", async () => {
  let complete!: (value: ReturnType<typeof receipt>) => void;
  mocks.adjust.mockImplementation(() => new Promise(resolve => { complete = resolve; }));
  const close = vi.fn(); render(batch, identity, "uk", close); await until(() => !!input(messages.quantity) && !input(messages.quantity).disabled);
  await submit(); await act(async () => { container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })); });
  act(() => document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" })));
  expect(mocks.adjust).toHaveBeenCalledTimes(1); expect(close).not.toHaveBeenCalled();
  await act(async () => complete(receipt(mocks.adjust.mock.calls[0][0])));
});
it("does not send when persistence fails", async () => {
  await ready(); vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("quota"); });
  await submit(); expect(mocks.adjust).not.toHaveBeenCalled(); expect(container.textContent).toContain(messages.storage);
});
it("does not discard an unreadable saved intent and does not send", async () => {
  sessionStorage.setItem(`sg_inventory_adjustment:v1:${identity.tenantId}:${identity.actorId}`, "broken"); render();
  await until(() => container.textContent!.includes(messages.storage)); expect(mocks.adjust).not.toHaveBeenCalled();
});
it.each([{ ...identity, actorId: "77777777-7777-4777-8777-777777777777" }, { ...identity, tenantId: "88888888-8888-4888-8888-888888888888" }])(
  "does not restore another actor or tenant's intent: %j", async who => {
    saveAdjustment(identity, pending(), sessionStorage); render(null, who);
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 20)); });
    expect(button(messages.resume)).toBeUndefined(); expect(mocks.adjust).not.toHaveBeenCalled(); expect(readAdjustment(identity, sessionStorage)).not.toBeNull();
  });
it("retains the old user's intent if the session changes before the response", async () => {
  let complete!: (value: ReturnType<typeof receipt>) => void;
  mocks.adjust.mockImplementation(() => new Promise(resolve => { complete = resolve; }));
  await ready(); await submit(); act(() => markLoggedOut());
  await act(async () => complete(receipt(mocks.adjust.mock.calls[0][0])));
  expect(readAdjustment(identity, sessionStorage)?.phase).toBe("uncertain"); expect(container.textContent).not.toContain(messages.completed);
});
it("keeps a mismatched receipt unresolved", async () => {
  mocks.adjust.mockImplementation(async command => ({ ...receipt(command), tenantId: "wrong" }));
  await ready(); await submit(); expect(readAdjustment(identity, sessionStorage)?.phase).toBe("uncertain");
  expect(container.textContent).not.toContain(messages.completed);
});
it("renders the English flow without missing translations", async () => {
  render(batch, identity, "en"); await until(() => !!button(en.Dashboard.stock.adjustment.submit));
  expect(container.textContent).toContain(en.Dashboard.stock.adjustment.title); expect(container.textContent).not.toContain("Dashboard.stock.adjustment");
});
