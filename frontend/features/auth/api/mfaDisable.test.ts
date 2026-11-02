import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { clearToken, getToken, setToken } from "@/lib/api";
import { authApi } from "./auth";
import { clearStoredUser, getStoredUser } from "../store";

const response = (body: unknown, status = 200) => ({ ok: status < 400, status, json: async () => body });
beforeEach(() => { setToken("old-verified-token"); });
afterEach(() => { clearToken(); clearStoredUser(); vi.unstubAllGlobals(); });
it("stores the replacement session and disabled profile before callers refetch settings", async () => {
  const result = { accessToken: "new-unverified-token", user: { id: "synthetic", twoFactorEnabled: false, permissions: {} } };
  const fetch = vi.fn().mockResolvedValue(response(result)); vi.stubGlobal("fetch", fetch);
  expect(await authApi.disableTwoFactor({ password: "synthetic-password", code: "ABCD-2345" })).toEqual(result);
  expect(getToken()).toBe("new-unverified-token"); expect(getStoredUser()?.twoFactorEnabled).toBe(false);
  expect(fetch).toHaveBeenCalledTimes(1);
  expect(fetch.mock.calls[0][0]).toContain("/api/auth/2fa/disable");
  expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ password: "synthetic-password", code: "ABCD-2345" });
});
it("leaves the verified session unchanged when password or code is rejected", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response({ error: "Invalid code." }, 400)));
  await expect(authApi.disableTwoFactor({ password: "synthetic", code: "123456" })).rejects.toMatchObject({ status: 400 });
  expect(getToken()).toBe("old-verified-token");
});
it("cannot restore a replacement session after logout during disable", async () => {
  let finish!: (value: unknown) => void;
  vi.stubGlobal("fetch", vi.fn().mockReturnValue(new Promise(resolve => { finish = resolve; })));
  const pending = authApi.disableTwoFactor({ password: "synthetic", code: "ABCD-2345" });
  clearToken(); clearStoredUser();
  finish(response({ accessToken: "late-token", user: { id: "synthetic", twoFactorEnabled: false } }));
  await expect(pending).rejects.toMatchObject({ status: 401 });
  expect(getToken()).toBeNull(); expect(getStoredUser()).toBeNull();
});
