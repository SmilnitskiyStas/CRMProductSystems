// @vitest-environment jsdom
import { act } from "react";
import { createRoot } from "react-dom/client";
import type { Root } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { NextIntlClientProvider } from "next-intl";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import uk from "@/messages/uk.json";
import en from "@/messages/en.json";
import { ApiError } from "@/lib/api";
import { BeautySection, BeautyInviteDialog, beautyErrorKey } from "./BeautySection";
import { providerBeautyApi } from "../api/providerBeauty";
import { BEAUTY_MODULES } from "../types";

vi.mock("../api/providerBeauty", () => ({
  providerBeautyApi: {
    get: vi.fn(),
    create: vi.fn(),
    suspend: vi.fn(),
    resume: vi.fn(),
    ownerInvite: vi.fn(),
    updateModules: vi.fn(),
  },
}));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const api = vi.mocked(providerBeautyApi);
const SECRET_URL = "https://app.example/invite#t=SECRET-TOKEN";

let container: HTMLDivElement;
let root: Root;

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

async function render(node: React.ReactNode) {
  await act(async () => {
    root.render(wrap(node));
  });
  await flush();
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

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  vi.clearAllMocks();
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

describe("BeautySection", () => {
  it("shows the empty state with a create button", async () => {
    api.get.mockResolvedValue({ exists: false });
    await render(<BeautySection tenantId="t1" tenantName="Salon" tenantSlug="salon" />);
    expect(container.textContent).toContain("Немає");
    expect(button("Створити бізнес Beauty CRM")).toBeTruthy();
  });

  it("creates a business and shows the invite URL once, then drops it on close", async () => {
    api.get.mockResolvedValue({ exists: false });
    api.create.mockResolvedValue({
      slug: "salon", status: "active", inviteUrl: SECRET_URL, inviteExpiresAt: "2026-11-08T10:00:00Z",
    });
    await render(<BeautySection tenantId="t1" tenantName="Salon" tenantSlug="salon" />);
    await click(button("Створити бізнес Beauty CRM"));

    const dialog = document.querySelector('[role="dialog"]')!;
    expect(dialog.getAttribute("aria-modal")).toBe("true");
    expect(dialog.getAttribute("aria-labelledby")).toBeTruthy();

    const email = dialog.querySelector('input[type="email"]') as HTMLInputElement;
    await type(email, "owner@x.test");
    await click(button("Створити"));

    expect(api.create).toHaveBeenCalledWith(
      expect.objectContaining({
        shelfGuardTenantId: "t1", name: "Salon", slug: "salon", ownerEmail: "owner@x.test",
        modules: expect.arrayContaining(["beauty_booking"]),
      }),
    );
    const sent = api.create.mock.calls[0][0].modules!;
    expect(sent).not.toContain("beauty_marketing");

    // Shown once, with the warning.
    const input = document.querySelector('input[readonly]') as HTMLInputElement;
    expect(input.value).toBe(SECRET_URL);
    expect(document.body.textContent).toContain("показується один раз");
    expect(document.body.textContent).toContain("72 год");

    await click(document.querySelector('button[aria-label="Закрити"]')!);
    expect(document.body.innerHTML).not.toContain("SECRET-TOKEN");
    expect(container.innerHTML).not.toContain("SECRET-TOKEN");
  });

  it("blocks invalid input client-side without calling the API", async () => {
    api.get.mockResolvedValue({ exists: false });
    await render(<BeautySection tenantId="t1" tenantName="Salon" tenantSlug="salon" />);
    await click(button("Створити бізнес Beauty CRM"));
    await click(button("Створити"));
    expect(api.create).not.toHaveBeenCalled();
    expect(document.body.textContent).toContain("Вкажіть коректний email");
  });

  it("maps slug_taken from the server to a readable message", async () => {
    api.get.mockResolvedValue({ exists: false });
    api.create.mockRejectedValue(new ApiError(409, "x", { code: "slug_taken" }));
    await render(<BeautySection tenantId="t1" tenantName="Salon" tenantSlug="salon" />);
    await click(button("Створити бізнес Beauty CRM"));
    await type(document.querySelector('input[type="email"]') as HTMLInputElement, "o@x.test");
    await click(button("Створити"));
    expect(document.querySelector('[role="alert"]')?.textContent).toContain("уже зайнятий");
  });

  it("suspends only after confirmation", async () => {
    api.get.mockResolvedValue({
      exists: true, slug: "salon", status: "active", modules: ["beauty_booking"], ownerState: "invited",
    });
    api.suspend.mockResolvedValue({ status: "suspended" });
    await render(<BeautySection tenantId="t1" tenantName="Salon" tenantSlug="salon" />);
    expect(container.textContent).toContain("Активний");

    await click(button("Призупинити"));
    expect(api.suspend).not.toHaveBeenCalled();
    expect(document.querySelector('[role="alertdialog"]')).toBeTruthy();

    const confirms = Array.from(document.querySelectorAll('[role="alertdialog"] button')).filter(
      (b) => b.textContent?.trim() === "Призупинити",
    );
    await click(confirms[0]);
    expect(api.suspend).toHaveBeenCalledWith("t1");
    expect(document.querySelector('[role="alertdialog"]')).toBeNull();
  });

  it("disables the re-invite button while suspended and offers resume", async () => {
    api.get.mockResolvedValue({ exists: true, slug: "salon", status: "suspended", modules: [], ownerState: "invited" });
    await render(<BeautySection tenantId="t1" tenantName="Salon" tenantSlug="salon" />);
    expect(button("Нове запрошення власнику").disabled).toBe(true);
    expect(button("Відновити")).toBeTruthy();
  });

  it("re-issues an owner invite and shows the link", async () => {
    api.get.mockResolvedValue({ exists: true, slug: "salon", status: "active", modules: ["beauty_booking"], ownerState: "invited" });
    api.ownerInvite.mockResolvedValue({ inviteUrl: SECRET_URL, expiresAt: "2026-11-08T10:00:00Z" });
    await render(<BeautySection tenantId="t1" tenantName="Salon" tenantSlug="salon" />);
    await click(button("Нове запрошення власнику"));
    expect((document.querySelector("input[readonly]") as HTMLInputElement).value).toBe(SECRET_URL);
  });

  it("saves edited modules", async () => {
    api.get.mockResolvedValue({ exists: true, slug: "salon", status: "active", modules: ["beauty_booking"], ownerState: "active" });
    api.updateModules.mockResolvedValue({ modules: ["beauty_booking", "beauty_ai"] });
    await render(<BeautySection tenantId="t1" tenantName="Salon" tenantSlug="salon" />);
    await click(button("Модулі"));
    const boxes = Array.from(document.querySelectorAll('[role="dialog"] input[type="checkbox"]')) as HTMLInputElement[];
    expect(boxes).toHaveLength(BEAUTY_MODULES.length);
    await click(boxes[BEAUTY_MODULES.indexOf("beauty_ai")]);
    await click(button("Зберегти"));
    expect(api.updateModules).toHaveBeenCalledWith("t1", ["beauty_booking", "beauty_ai"]);
  });
});

describe("BeautyInviteDialog", () => {
  it("copies the link and closes on Escape", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
    const onClose = vi.fn();
    await render(<BeautyInviteDialog invite={{ url: SECRET_URL, expiresAt: "2026-11-08T10:00:00Z" }} onClose={onClose} />);

    await click(button("Копіювати"));
    expect(writeText).toHaveBeenCalledWith(SECRET_URL);
    expect(button("Скопійовано")).toBeTruthy();

    await act(async () => {
      document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    });
    expect(onClose).toHaveBeenCalled();
  });

  it("moves focus into the dialog", async () => {
    await render(<BeautyInviteDialog invite={{ url: SECRET_URL, expiresAt: "2026-11-08T10:00:00Z" }} onClose={() => {}} />);
    const dialog = document.querySelector('[role="dialog"]')!;
    expect(dialog.contains(document.activeElement)).toBe(true);
  });
});

describe("beautyErrorKey", () => {
  it("maps contract error codes", () => {
    expect(beautyErrorKey(new ApiError(409, "", { code: "slug_taken" }))).toBe("slugTaken");
    expect(beautyErrorKey(new ApiError(409, "", { code: "external_ref_conflict" }))).toBe("externalRefConflict");
    expect(beautyErrorKey(new ApiError(502, "", { code: "beauty_unavailable" }))).toBe("unavailable");
    expect(beautyErrorKey(new ApiError(422, "", { code: "validation" }))).toBe("validation");
    expect(beautyErrorKey(new Error("boom"))).toBe("generic");
  });
});

describe("i18n parity", () => {
  it("has the same Beauty keys in uk and en", () => {
    const keys = (o: unknown, p = ""): string[] =>
      o && typeof o === "object"
        ? Object.entries(o).flatMap(([k, v]) => keys(v, `${p}${k}.`))
        : [p];
    const pick = (m: unknown) => (m as { Dashboard: { provider: { beauty: unknown }; providerLeads: unknown } }).Dashboard;
    expect(keys(pick(en).provider.beauty).sort()).toEqual(keys(pick(uk).provider.beauty).sort());
    expect(keys(pick(en).providerLeads).sort()).toEqual(keys(pick(uk).providerLeads).sort());
  });
});
