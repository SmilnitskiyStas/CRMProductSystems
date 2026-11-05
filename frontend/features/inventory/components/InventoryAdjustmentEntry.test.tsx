import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { NextIntlClientProvider } from "next-intl";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import StockPage from "@/app/(dashboard)/stock/page";
import { StockTable } from "@/features/shelf/components/StockTable";
import uk from "@/messages/uk.json";
import type { ProductStockDto } from "@/features/shelf/types";

const state = vi.hoisted(() => ({ user: { id: "actor-a", tenantId: "tenant-a" as string | null, role: "store_manager" }, workspace: vi.fn() }));
vi.mock("next/navigation", () => ({ useSearchParams: () => new URLSearchParams(), useRouter: () => ({ push: vi.fn() }) }));
vi.mock("@/features/auth/hooks/useAuth", () => ({ useMe: () => ({ data: state.user }) }));
vi.mock("@/lib/useStoreContext", () => ({ usePrimaryStoreId: () => "store-a" }));
vi.mock("@/features/stores/hooks/useStores", () => ({ useStores: () => ({ data: [] }) }));
vi.mock("@/features/catalog/hooks/useCatalog", () => ({ useCatalogProducts: () => ({ data: [] }) }));
vi.mock("@/features/shelf/hooks/useStock", () => ({ useStock: () => ({ data: { items: [row], totalCount: 1 }, isLoading: false }), useVerifyStock: () => ({ mutate: vi.fn() }) }));
vi.mock("@/features/shelf/components/StockFilters", () => ({ StockFilters: () => null }));
vi.mock("./InventoryAdjustmentWorkspace", () => ({ InventoryAdjustmentWorkspace: (props: unknown) => { state.workspace(props); return null; } }));

const row: ProductStockDto = { id: "batch-a", productId: "product-a", productName: "Айран", productBarcode: "123", storeId: "store-a", storeName: "Магазин",
  zoneId: null, zoneName: null, shelfNumber: null, batchNumber: "A-1", quantity: 0, quantityInitial: 5, expiryDate: "2026-11-01", daysLeft: 6,
  status: "sold_out", sourceType: "receipt", addedAt: "2026-10-20", lastCheckedAt: "2026-10-26", priceRetail: 50, pricePurchase: 20,
  defaultReimbursementType: null, defaultReimbursementValue: null };
let container: HTMLDivElement, root: Root;
beforeEach(() => {
  (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
  state.user = { id: "actor-a", tenantId: "tenant-a", role: "store_manager" }; vi.clearAllMocks();
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
});
afterEach(() => { act(() => root.unmount()); container.remove(); });
function render(content: React.ReactNode = <StockPage />) {
  act(() => root.render(<NextIntlClientProvider locale="uk" timeZone="Europe/Kyiv" messages={uk}>{content}</NextIntlClientProvider>));
}
function action() {
  const trigger = container.querySelector("svg.lucide-more-horizontal")?.closest("button");
  expect(trigger).toBeTruthy(); act(() => trigger!.click());
  return [...document.querySelectorAll("button")].find(button => button.textContent?.includes(uk.Dashboard.shelf.stockTable.adjustBatch));
}
it("opens adjustment for the exact zero-count batch from its row action", () => {
  render(); const adjust = action(); expect(adjust).toBeTruthy(); act(() => adjust!.click());
  const props = state.workspace.mock.calls.at(-1)![0];
  expect(props).toMatchObject({ tenantId: "tenant-a", actorId: "actor-a", selected: { id: "batch-a", quantity: 0 } });
});
it.each(["cashier", "provider", "provider_admin", "supplier_admin"])("does not offer adjustment to %s", role => {
  state.user.role = role; render(); expect(action()).toBeUndefined(); expect(state.workspace).not.toHaveBeenCalled();
});
it("does not offer adjustment without a tenant", () => {
  state.user.tenantId = null; render(); expect(action()).toBeUndefined(); expect(state.workspace).not.toHaveBeenCalled();
});
it("does not carry a selected batch into another user or tenant's workspace", () => {
  render(); const adjust = action(); act(() => adjust!.click());
  state.user = { id: "actor-b", tenantId: "tenant-b", role: "store_manager" }; render();
  expect(state.workspace.mock.calls.at(-1)![0]).toMatchObject({ actorId: "actor-b", tenantId: "tenant-b", selected: null });
});
it("does not invent a correction action when a consumer of StockTable provides no callback", () => {
  render(<StockTable items={[row]} isLoading={false} selectedIds={new Set()} onSelectId={vi.fn()} onSelectAll={vi.fn()}
    sortBy="expirydate" sortDescending={false} onSort={vi.fn()} />);
  expect(action()).toBeUndefined();
});
