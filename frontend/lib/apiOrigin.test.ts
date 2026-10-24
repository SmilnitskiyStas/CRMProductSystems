import { afterEach, expect, it, vi } from "vitest";

afterEach(() => { vi.unstubAllEnvs(); vi.unstubAllGlobals(); vi.resetModules(); });
it("sends browser login to the configured HTTPS API origin with credentials", async () => {
  vi.stubEnv("NEXT_PUBLIC_API_URL", "https://api.agrusystems.pp.ua");
  vi.resetModules();
  const request = vi.fn().mockResolvedValue({ ok: false, status: 401, json: async () => ({ error: "Invalid email or password." }) });
  vi.stubGlobal("fetch", request);
  const { api, ApiError } = await import("./api");
  await expect(api.post("/api/auth/login", { email: "synthetic@example.invalid", password: "synthetic" })).rejects.toBeInstanceOf(ApiError);
  expect(request).toHaveBeenCalledTimes(1);
  expect(request).toHaveBeenCalledWith("https://api.agrusystems.pp.ua/api/auth/login", expect.objectContaining({ method: "POST", credentials: "include" }));
});
