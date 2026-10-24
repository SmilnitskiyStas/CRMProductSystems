import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { authApi } from "./auth";
import { clearToken, getToken, setToken } from "@/lib/api";
import { clearStoredUser, getStoredUser, setStoredUser } from "../store";
import type { AuthUserDto } from "../types";

const proof = { email: "synthetic@example.com", token: "A".repeat(43), newPassword: "SyntheticPassword42" };

beforeEach(() => {
  setToken("existing-session");
  setStoredUser({ id: "synthetic-user" } as AuthUserDto);
});
afterEach(() => { clearToken(); clearStoredUser(); vi.unstubAllGlobals(); });

it("requesting a link preserves the current local login", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true, status: 204 }));
  await authApi.forgotPassword({ email: proof.email });
  expect(getToken()).toBe("existing-session");
  expect(getStoredUser()?.id).toBe("synthetic-user");
});

it("MFA enrollment upgrades local credentials before recovery codes are shown", async () => {
  const result = { accessToken: "mfa-upgraded", recoveryCodes: ["SYNTHETIC"],
    user: { id: "synthetic-user", twoFactorEnabled: true, permissions: {} } };
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => result }));
  expect((await authApi.enableTwoFactor("synthetic-code")).recoveryCodes).toEqual(["SYNTHETIC"]);
  expect(getToken()).toBe("mfa-upgraded"); expect(getStoredUser()?.twoFactorEnabled).toBe(true);
});

it("failed global logout keeps local credentials for a retry", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: false, status: 503, json: async () => ({ error: "Unavailable" }) }));
  await expect(authApi.logoutAll()).rejects.toMatchObject({ status: 503 });
  expect(getToken()).toBe("existing-session"); expect(getStoredUser()?.id).toBe("synthetic-user");
});

it("successful proof confirmation clears local credentials without creating a session", async () => {
  const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 204 });
  vi.stubGlobal("fetch", fetchMock);
  await authApi.resetPassword(proof);
  expect(getToken()).toBeNull();
  expect(getStoredUser()).toBeNull();
  expect(fetchMock).toHaveBeenCalledTimes(1);
  const [url, init] = fetchMock.mock.calls[0];
  expect(url).not.toContain(proof.token);
  expect(url).toContain("/api/auth/reset-password");
  expect(JSON.parse(init.body)).toEqual(proof);
});

it("failed proof confirmation leaves the current local login intact", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: false, status: 400, json: async () => ({ error: "Invalid proof" }) }));
  await expect(authApi.resetPassword(proof)).rejects.toMatchObject({ status: 400 });
  expect(getToken()).toBe("existing-session");
  expect(getStoredUser()?.id).toBe("synthetic-user");
});
