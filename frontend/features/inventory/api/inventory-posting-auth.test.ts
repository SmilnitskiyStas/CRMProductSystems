import { afterEach, expect, it, vi } from "vitest";
import { setToken, clearToken, ApiError } from "@/lib/api";
import { createInventoryAdjustment, inventoryPostingApi } from "./inventory-posting";

const mfa = vi.hoisted(() => vi.fn());
vi.mock("@/features/auth/mfaPrompt", () => ({ requestMfaCode: mfa }));
afterEach(() => { clearToken(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });
const command = createInventoryAdjustment({ requestId: "11111111-1111-4111-8111-111111111111",
  locationId: "22222222-2222-4222-8222-222222222222", batchId: "33333333-3333-4333-8333-333333333333",
  expectedQuantity: "3.25", quantity: "5", reason: "Підтверджено перерахунком" });
function json(body: unknown, status = 200) { return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } }); }

it("the real API client repeats identical adjustment bytes after MFA step-up", async () => {
  setToken("before-mfa"); mfa.mockResolvedValue("123456");
  const fetcher = vi.fn().mockResolvedValueOnce(json({ code: "mfa_step_up_required" }, 403))
    .mockResolvedValueOnce(json({ accessToken: "after-mfa" })).mockResolvedValueOnce(json({ replayed: false }));
  vi.stubGlobal("fetch", fetcher); await inventoryPostingApi.adjust(command);
  expect(fetcher.mock.calls).toHaveLength(3);
  expect(fetcher.mock.calls[0][1].body).toBe(fetcher.mock.calls[2][1].body);
  expect(JSON.parse(fetcher.mock.calls[2][1].body)).toEqual(command);
  expect(fetcher.mock.calls[1][0]).toContain("/api/auth/2fa/step-up");
  expect(fetcher.mock.calls[2][1].headers.Authorization).toBe("Bearer after-mfa");
});
it("MFA cancellation leaves the exact command available for an explicit retry", async () => {
  setToken("before-mfa"); mfa.mockResolvedValue(null);
  const fetcher = vi.fn().mockResolvedValueOnce(json({ code: "mfa_step_up_required" }, 403)).mockResolvedValueOnce(json({ replayed: true }));
  vi.stubGlobal("fetch", fetcher);
  await expect(inventoryPostingApi.adjust(command)).rejects.toBeInstanceOf(ApiError);
  await inventoryPostingApi.adjust(command);
  expect(fetcher.mock.calls).toHaveLength(2); expect(fetcher.mock.calls[0][1].body).toBe(fetcher.mock.calls[1][1].body);
});
