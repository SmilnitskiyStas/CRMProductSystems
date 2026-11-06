// @vitest-environment jsdom
import { act, useState } from "react";
import { createRoot } from "react-dom/client";
import type { Root } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { NextIntlClientProvider } from "next-intl";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import uk from "@/messages/uk.json";
import en from "@/messages/en.json";
import { ApiError } from "@/lib/api";
import { CreateTenantWizard } from "./CreateTenantWizard";
import { providerApi } from "../api/provider";
import { providerBeautyApi } from "../api/providerBeauty";
import { BEAUTY_MODULES } from "../types";

vi.mock("../api/provider", () => ({
  providerApi: { createTenant: vi.fn() },
}));
vi.mock("../api/providerBeauty", () => ({
  providerBeautyApi: { create: vi.fn() },
}));
let itemCategories: { key: string; labelUa: string }[] = [];
vi.mock("@/features/marketplace/hooks/useMarketplace", () => ({
  useItemCategories: () => ({ data: itemCategories }),
}));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const tenantApi = vi.mocked(providerApi);
const beautyApi = vi.mocked(providerBeautyApi);
const SECRET_URL = "https://app.example/invite#t=SECRET-TOKEN";
const BEAUTY_LABEL = uk.Dashboard.provider.businessTypes.beauty;
const RETAIL_LABEL = uk.Dashboard.provider.businessTypes.retail;
const SUPPLIER_LABEL = uk.Dashboard.provider.businessTypes.supplier;
const OK_BEAUTY = {
  slug: "salon", status: "active" as const, inviteUrl: SECRET_URL as string | null, inviteExpiresAt: "2026-11-08T10:00:00Z",
};

let container: HTMLDivElement;
let root: Root;
const onClose = vi.fn();
const onCreated = vi.fn();

function wrap(node: React.ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={qc}>
      <NextIntlClientProvider locale="uk" messages={uk as never} timeZone="UTC">
        {node}
      </NextIntlClientProvider>
    </QueryClientProvider>
  );
}

async function flush() {
  await act(async () => {
    await new Promise((r) => setTimeout(r, 0));
  });
}

function button(text: string): HTMLButtonElement {
  const b = Array.from(document.querySelectorAll("button")).find((x) => x.textContent?.trim() === text);
  if (!b) throw new Error(`button "${text}" not found`);
  return b as HTMLButtonElement;
}

function buttonStartingWith(text: string): HTMLButtonElement {
  const b = Array.from(document.querySelectorAll("button")).find((x) => x.textContent?.trim().endsWith(text));
  if (!b) throw new Error(`button "${text}" not found`);
  return b as HTMLButtonElement;
}

async function click(el: Element) {
  await act(async () => {
    (el as HTMLElement).click();
  });
  await flush();
}

async function type(el: HTMLInputElement, value: string) {
  await act(async () => {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
    setter.call(el, value);
    el.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

function deferred<T>() {
  let resolve!: (v: T) => void;
  const promise = new Promise<T>((res) => {
    resolve = res;
  });
  return { promise, resolve };
}

async function pressEscape() {
  await act(async () => {
    document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
  });
  await flush();
}

function Host() {
  const [open, setOpen] = useState(true);
  return open ? (
    <CreateTenantWizard
      onClose={() => {
        onClose();
        setOpen(false);
      }}
      onCreated={onCreated}
    />
  ) : null;
}

async function renderWizard() {
  await act(async () => {
    root.render(wrap(<Host />));
  });
  await flush();
}

/** Company step -> business type step -> picks the given type (does not advance to step 3). */
async function pickType(typeLabel: string, companyName = "Salon") {
  await renderWizard();
  await type(document.querySelector("input") as HTMLInputElement, companyName);
  await click(button("Далі"));
  await click(buttonStartingWith(typeLabel));
}

async function goToBeautyStep3(companyName = "Salon") {
  await pickType(BEAUTY_LABEL, companyName);
  await click(button("Далі"));
}

async function fillOwnerAndSubmit() {
  await type(document.querySelector('input[type="email"]') as HTMLInputElement, "owner@x.test");
  await click(button("Створити клієнта"));
}

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  vi.resetAllMocks();
  itemCategories = [];
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

describe("CreateTenantWizard beauty", () => {
  it("lists beauty as a business type", async () => {
    await renderWizard();
    await type(document.querySelector("input") as HTMLInputElement, "Salon");
    await click(button("Далі"));
    expect(buttonStartingWith(BEAUTY_LABEL)).toBeTruthy();
  });

  it("shows Beauty modules instead of ShelfGuard modules", async () => {
    await goToBeautyStep3();
    expect(document.querySelectorAll('input[type="checkbox"]')).toHaveLength(BEAUTY_MODULES.length);
  });

  it("rejects an invalid email: nothing is created, an alert is shown and focus goes to the field", async () => {
    await goToBeautyStep3();
    const email = document.querySelector('input[type="email"]') as HTMLInputElement;
    await click(button("Створити клієнта"));
    expect(tenantApi.createTenant).not.toHaveBeenCalled();
    expect(beautyApi.create).not.toHaveBeenCalled();
    expect(document.querySelector('[role="alert"]')?.textContent).toContain("коректний email");
    expect(document.activeElement).toBe(email);
    expect(email.getAttribute("aria-invalid")).toBe("true");
    expect(email.getAttribute("aria-required")).toBe("true");
    const described = document.getElementById(email.getAttribute("aria-describedby")!);
    expect(described?.textContent).toContain("коректний email");
    expect(document.querySelector(`label[for="${email.id}"]`)).not.toBeNull();

    await type(email, "not-an-email");
    await click(button("Створити клієнта"));
    expect(tenantApi.createTenant).not.toHaveBeenCalled();
    expect(beautyApi.create).not.toHaveBeenCalled();
    expect(document.querySelector('[role="alert"]')).not.toBeNull();
  });

  it("disables submit when the last Beauty module is unchecked", async () => {
    await goToBeautyStep3();
    for (const box of Array.from(document.querySelectorAll('input[type="checkbox"]:checked'))) {
      await click(box);
    }
    expect(document.querySelectorAll('input[type="checkbox"]:checked')).toHaveLength(0);
    expect(button("Створити клієнта").disabled).toBe(true);
  });

  it("creates the tenant with modules [] then the Beauty business, and shows the invite once", async () => {
    tenantApi.createTenant.mockResolvedValue({ id: "t1" } as never);
    beautyApi.create.mockResolvedValue(OK_BEAUTY);
    await goToBeautyStep3();
    await fillOwnerAndSubmit();

    expect(tenantApi.createTenant).toHaveBeenCalledTimes(1);
    const req = tenantApi.createTenant.mock.calls[0][0];
    expect(req.businessType).toBe("beauty");
    expect(req.modules).toEqual([]);
    expect(beautyApi.create).toHaveBeenCalledWith(
      expect.objectContaining({
        shelfGuardTenantId: "t1", name: "Salon", slug: "salon", ownerEmail: "owner@x.test",
        modules: expect.arrayContaining(["beauty_booking"]),
      }),
    );
    expect(beautyApi.create.mock.calls[0][0].modules).not.toContain("beauty_marketing");
    expect(tenantApi.createTenant.mock.invocationCallOrder[0]).toBeLessThan(
      beautyApi.create.mock.invocationCallOrder[0],
    );

    expect((document.querySelector("input[readonly]") as HTMLInputElement).value).toBe(SECRET_URL);
    expect(onCreated).not.toHaveBeenCalled();

    await click(document.querySelector('button[aria-label="Закрити"]')!);
    expect(onCreated).toHaveBeenCalledWith("t1");
    expect(onClose).toHaveBeenCalled();
    expect(document.body.innerHTML).not.toContain("SECRET-TOKEN");
    expect(document.querySelector("input[readonly]")).toBeNull();
  });

  it("sends only one createTenant request on a double submit", async () => {
    const d = deferred<{ id: string }>();
    tenantApi.createTenant.mockReturnValue(d.promise as never);
    beautyApi.create.mockResolvedValue(OK_BEAUTY);
    await goToBeautyStep3();
    await type(document.querySelector('input[type="email"]') as HTMLInputElement, "owner@x.test");
    const submit = button("Створити клієнта");
    await act(async () => {
      submit.click();
      submit.click();
    });
    expect(tenantApi.createTenant).toHaveBeenCalledTimes(1);
    await act(async () => d.resolve({ id: "t1" }));
    await flush();
    expect(tenantApi.createTenant).toHaveBeenCalledTimes(1);
    expect(beautyApi.create).toHaveBeenCalledTimes(1);
  });

  it("does not close on Escape, backdrop or the x button while a request is in flight", async () => {
    const d = deferred<{ id: string }>();
    tenantApi.createTenant.mockReturnValue(d.promise as never);
    beautyApi.create.mockResolvedValue(OK_BEAUTY);
    await goToBeautyStep3();
    await fillOwnerAndSubmit();
    expect(button("Створення…").disabled).toBe(true);

    await pressEscape();
    await click(button("×"));
    await click(container.firstElementChild as Element);
    expect(onClose).not.toHaveBeenCalled();
    expect(onCreated).not.toHaveBeenCalled();

    await act(async () => d.resolve({ id: "t1" }));
    await flush();
    expect((document.querySelector("input[readonly]") as HTMLInputElement).value).toBe(SECRET_URL);
  });

  it("retries only the Beauty step after a partial failure", async () => {
    tenantApi.createTenant.mockResolvedValue({ id: "t1" } as never);
    beautyApi.create
      .mockRejectedValueOnce(new ApiError(502, "x", { code: "beauty_unavailable" }))
      .mockResolvedValueOnce(OK_BEAUTY);
    await goToBeautyStep3();
    await fillOwnerAndSubmit();

    const alert = document.querySelector('[role="alert"]')?.textContent ?? "";
    expect(alert).toContain("тимчасово недоступна");
    expect(alert).toContain("ShelfGuard створено");
    expect(button("Назад").disabled).toBe(true);

    await click(button("Повторити створення в Beauty CRM"));
    expect(tenantApi.createTenant).toHaveBeenCalledTimes(1);
    expect(beautyApi.create).toHaveBeenCalledTimes(2);
    expect(beautyApi.create.mock.calls[1][0].shelfGuardTenantId).toBe("t1");
    expect((document.querySelector("input[readonly]") as HTMLInputElement).value).toBe(SECRET_URL);
  });

  it("sends only one Beauty request on a double click of retry", async () => {
    tenantApi.createTenant.mockResolvedValue({ id: "t1" } as never);
    const d = deferred<typeof OK_BEAUTY>();
    beautyApi.create
      .mockRejectedValueOnce(new ApiError(502, "x", { code: "beauty_unavailable" }))
      .mockReturnValueOnce(d.promise);
    await goToBeautyStep3();
    await fillOwnerAndSubmit();
    const retry = button("Повторити створення в Beauty CRM");
    await act(async () => {
      retry.click();
      retry.click();
    });
    expect(beautyApi.create).toHaveBeenCalledTimes(2);
    await act(async () => d.resolve(OK_BEAUTY));
    await flush();
    expect(beautyApi.create).toHaveBeenCalledTimes(2);
    expect(tenantApi.createTenant).toHaveBeenCalledTimes(1);
  });

  it("stays retryable after a repeated partial failure", async () => {
    tenantApi.createTenant.mockResolvedValue({ id: "t1" } as never);
    beautyApi.create.mockRejectedValue(new ApiError(502, "x", { code: "beauty_unavailable" }));
    await goToBeautyStep3();
    await fillOwnerAndSubmit();
    await click(button("Повторити створення в Beauty CRM"));
    expect(beautyApi.create).toHaveBeenCalledTimes(2);
    expect(tenantApi.createTenant).toHaveBeenCalledTimes(1);
    expect(document.querySelector('[role="alert"]')?.textContent).toContain("ShelfGuard створено");
    expect(button("Повторити створення в Beauty CRM").disabled).toBe(false);
    expect(onClose).not.toHaveBeenCalled();
  });

  it("opens the created tenant when the wizard is closed after a partial failure", async () => {
    tenantApi.createTenant.mockResolvedValue({ id: "t1" } as never);
    beautyApi.create.mockRejectedValue(new ApiError(502, "x", { code: "beauty_unavailable" }));
    await goToBeautyStep3();
    await fillOwnerAndSubmit();
    expect(onCreated).not.toHaveBeenCalled();

    await pressEscape();
    expect(onCreated).toHaveBeenCalledWith("t1");
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("points to the re-invite action when Beauty returned no invite link", async () => {
    tenantApi.createTenant.mockResolvedValue({ id: "t1" } as never);
    beautyApi.create.mockResolvedValue({ ...OK_BEAUTY, inviteUrl: null });
    await goToBeautyStep3();
    await fillOwnerAndSubmit();
    expect(document.querySelector("input[readonly]")).toBeNull();
    expect(document.body.textContent).toContain("Нове запрошення власнику");
    await click(button("Готово"));
    expect(onCreated).toHaveBeenCalledWith("t1");
  });

  it("maps a 409 on the tenant request to an i18n message and never creates the Beauty business", async () => {
    tenantApi.createTenant.mockRejectedValue(new ApiError(409, "Slug already exists"));
    await goToBeautyStep3();
    await fillOwnerAndSubmit();
    expect(beautyApi.create).not.toHaveBeenCalled();
    const text = document.querySelector('[role="alert"]')?.textContent ?? "";
    expect(text).toContain(uk.Dashboard.provider.createTenantWizard.tenantSlugTaken);
    expect(text).not.toContain("already exists");
  });

  it("shows the generic message, not server text, for other tenant errors", async () => {
    tenantApi.createTenant.mockRejectedValue(new ApiError(500, "SQL boom"));
    await goToBeautyStep3();
    await fillOwnerAndSubmit();
    const text = document.querySelector('[role="alert"]')?.textContent ?? "";
    expect(text).toContain(uk.Dashboard.provider.createTenantWizard.errorCreateDefault);
    expect(text).not.toContain("SQL boom");
  });
});

describe("CreateTenantWizard non-beauty types", () => {
  it("creates a retail tenant with preset modules without touching Beauty CRM", async () => {
    tenantApi.createTenant.mockResolvedValue({ id: "r1" } as never);
    await pickType(RETAIL_LABEL);
    await click(button("Далі"));
    expect(document.querySelectorAll('input[type="checkbox"]')).toHaveLength(0);
    expect(document.querySelector('input[type="email"]')).toBeNull();
    await click(button("Створити клієнта"));

    expect(tenantApi.createTenant).toHaveBeenCalledTimes(1);
    const req = tenantApi.createTenant.mock.calls[0][0];
    expect(req.businessType).toBe("retail");
    expect(req.modules).toEqual(["inventory", "procurement", "pos"]);
    expect(req).not.toHaveProperty("supplierCategory");
    expect(beautyApi.create).not.toHaveBeenCalled();
    expect(onCreated).toHaveBeenCalledWith("r1");
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("passes supplierCategory only for supplier", async () => {
    itemCategories = [{ key: "food", labelUa: "Їжа" }];
    tenantApi.createTenant.mockResolvedValue({ id: "s1" } as never);
    await pickType(SUPPLIER_LABEL);
    expect(button("Далі").disabled).toBe(true);
    await click(button("Їжа"));
    await click(button("Далі"));
    await click(button("Створити клієнта"));

    const req = tenantApi.createTenant.mock.calls[0][0];
    expect(req.businessType).toBe("supplier");
    expect(req.supplierCategory).toBe("food");
    expect(req.modules).toEqual(["marketplace_supplier"]);
    expect(beautyApi.create).not.toHaveBeenCalled();
  });

  it("resets supplierCategory when the business type changes", async () => {
    itemCategories = [{ key: "food", labelUa: "Їжа" }];
    tenantApi.createTenant.mockResolvedValue({ id: "r1" } as never);
    await pickType(SUPPLIER_LABEL);
    await click(button("Їжа"));
    await click(buttonStartingWith(RETAIL_LABEL));
    await click(button("Далі"));
    await click(button("Створити клієнта"));

    const req = tenantApi.createTenant.mock.calls[0][0];
    expect(req.businessType).toBe("retail");
    expect(req).not.toHaveProperty("supplierCategory");
  });

  it("returns focus to the opener after the wizard finishes", async () => {
    tenantApi.createTenant.mockResolvedValue({ id: "r1" } as never);
    const opener = document.createElement("button");
    document.body.appendChild(opener);
    opener.focus();
    await pickType(RETAIL_LABEL);
    await click(button("Далі"));
    await click(button("Створити клієнта"));
    expect(document.activeElement).toBe(opener);
    opener.remove();
  });
});

describe("i18n parity (wizard)", () => {
  type Provider = Record<string, unknown>;
  const pick = (m: unknown) => (m as { Dashboard: { provider: Provider } }).Dashboard.provider;
  const keys = (o: unknown, p = ""): string[] =>
    o && typeof o === "object"
      ? Object.entries(o).flatMap(([k, v]) => keys(v, `${p}${k}.`))
      : [p];

  it("has the same wizard, beauty section and business type keys in uk and en", () => {
    for (const k of ["createTenantWizard", "businessTypes", "beauty"]) {
      expect(keys(pick(en)[k]).sort()).toEqual(keys(pick(uk)[k]).sort());
    }
  });

  it("has the new keys filled in both locales", () => {
    for (const m of [uk, en]) {
      const prov = pick(m) as Record<string, Record<string, string>>;
      expect(prov.createTenantWizard.tenantSlugTaken).toBeTruthy();
      expect(prov.beauty.inviteAlreadyIssued).toBeTruthy();
    }
  });

  it("has impersonateBeautyBlocked in both locales", () => {
    const find = (o: unknown): string[] =>
      o && typeof o === "object"
        ? Object.entries(o).flatMap(([k, v]) =>
            k === "impersonateBeautyBlocked" && typeof v === "string" ? [v] : find(v))
        : [];
    expect(find(uk).filter(Boolean)).toHaveLength(1);
    expect(find(en).filter(Boolean)).toHaveLength(1);
  });
});
