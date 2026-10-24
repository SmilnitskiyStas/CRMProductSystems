import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { api, clearToken, getToken, setToken } from "@/lib/api";
import { authApi } from "./auth";
import { registerMfaPrompt } from "../mfaPrompt";
import { canWorkPermission } from "@/lib/workPermissions";

const response = (body: unknown, status = 200) => ({ ok: status < 400, status, json: async () => body });
let unregister: (() => void) | undefined;
beforeEach(() => { setToken("old-token"); });
afterEach(() => { unregister?.(); clearToken(); vi.unstubAllGlobals(); });

it("keeps the enrollment proof out of access-token storage", async () => {
  const result = { requiresMfaEnrollment: true, enrollmentToken: "limited-proof" };
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response(result)));
  expect(await authApi.login({ email: "synthetic@test.example", password: "password" })).toEqual(result);
  expect(getToken()).toBe("old-token");
});
it("enrollment errors do not invoke refresh or replace the current identity", async () => {
  const fetch = vi.fn().mockResolvedValue(response({ error: "Expired proof" }, 401)); vi.stubGlobal("fetch", fetch);
  await expect(authApi.enableEnrollment("expired-proof", "123456")).rejects.toMatchObject({ status: 401 });
  expect(fetch).toHaveBeenCalledTimes(1); expect(getToken()).toBe("old-token");
});
it("retries the critical action once after MFA and stores its new same-session access token", async () => {
  unregister = registerMfaPrompt(async () => "123456");
  const fetch = vi.fn().mockResolvedValueOnce(response({ code: "mfa_step_up_required" }, 403))
    .mockResolvedValueOnce(response({ accessToken: "verified-token" })).mockResolvedValueOnce(response({ done: true }));
  vi.stubGlobal("fetch", fetch);
  expect(await api.post("/api/production/orders/synthetic/complete")).toEqual({ done: true });
  expect(fetch.mock.calls[1][0]).toContain("/api/auth/2fa/step-up");
  expect(JSON.parse(fetch.mock.calls[1][1].body)).toEqual({ code: "123456" });
  expect(fetch.mock.calls[2][1].headers.Authorization).toBe("Bearer verified-token");
});
it("cancellation and ordinary permission denials never replay a critical write", async () => {
  unregister = registerMfaPrompt(async () => null);
  const fetch = vi.fn().mockResolvedValue(response({ code: "mfa_step_up_required" }, 403)); vi.stubGlobal("fetch", fetch);
  await expect(api.post("/critical")).rejects.toMatchObject({ status: 403 }); expect(fetch).toHaveBeenCalledTimes(1);
  fetch.mockResolvedValue(response({ error: "Denied" }, 403));
  await expect(api.post("/critical")).rejects.toMatchObject({ status: 403 }); expect(fetch).toHaveBeenCalledTimes(2);
});
it("does not submit a code or replay a write after logout while the MFA dialog is open", async () => {
  unregister = registerMfaPrompt(async () => { clearToken(); return "123456"; });
  const fetch = vi.fn().mockResolvedValue(response({ code: "mfa_step_up_required" }, 403)); vi.stubGlobal("fetch", fetch);
  await expect(api.post("/critical")).rejects.toMatchObject({ status: 401 }); expect(fetch).toHaveBeenCalledTimes(1);
});
it("does not infer manage or cancel from a complete grant, and respects explicit denials", () => {
  const staff = { role: "staff", capabilities: ["production.complete"] };
  expect(canWorkPermission(staff, "production.complete")).toBe(true);
  expect(canWorkPermission(staff, "production.manage")).toBe(false);
  expect(canWorkPermission(staff, "production.cancel")).toBe(false);
  expect(canWorkPermission({ role: "enterprise_admin", permissions: { "production.complete": false } }, "production.complete")).toBe(false);
  expect(canWorkPermission({ role: "provider", capabilities: ["production.complete"] }, "production.complete")).toBe(false);
});

it("a late enrollment response cannot restore credentials after logout", async () => {
  let finish!: (value: unknown) => void;
  vi.stubGlobal("fetch", vi.fn().mockReturnValue(new Promise(resolve => { finish = resolve; })));
  const pending = authApi.enableEnrollment("limited-proof", "123456");
  clearToken();
  finish(response({ accessToken: "late-token", recoveryCodes: [], user: { id: "synthetic" } }));
  await expect(pending).rejects.toMatchObject({ status: 401 });
  expect(getToken()).toBeNull();
});
