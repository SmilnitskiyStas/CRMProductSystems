import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { api, ApiError, clearToken, getToken, markLoggedOut, setToken } from "./api";

// Covers the core contract of the shared API client: auth header injection,
// JSON/204 handling, error surfacing, and the 401 -> refresh -> retry flow
// (including the two deliberate carve-outs: anonymous auth endpoints and
// in-flight requests racing a manual logout). This module previously had 0%
// coverage despite being the single choke point every feature's API calls
// go through (audit Block 13).

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  } as Response;
}

/** Module-level auth state (_token/_loggedOut) survives across tests in this
 *  file — reset it explicitly so tests don't leak into each other. */
function resetAuthState() {
  setToken("reset-sentinel"); // clears _loggedOut
  clearToken(); // clears the token itself, keeps _loggedOut false
}

describe("lib/api", () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    resetAuthState();
    fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  describe("token storage", () => {
    it("persists the token across getToken() calls and to localStorage", () => {
      setToken("abc123");
      expect(getToken()).toBe("abc123");
      expect(localStorage.getItem("sg_token")).toBe("abc123");
    });

    it("clearToken() removes it from memory and localStorage", () => {
      setToken("abc123");
      clearToken();
      expect(getToken()).toBeNull();
      expect(localStorage.getItem("sg_token")).toBeNull();
    });
  });

  describe("request building", () => {
    it("sends Authorization header when a token is set", async () => {
      setToken("my-token");
      fetchMock.mockResolvedValueOnce(jsonResponse({ ok: true }));

      await api.get("/api/whatever");

      const [, init] = fetchMock.mock.calls[0];
      expect((init.headers as Record<string, string>).Authorization).toBe("Bearer my-token");
    });

    it("omits Authorization header when no token is set", async () => {
      fetchMock.mockResolvedValueOnce(jsonResponse({ ok: true }));

      await api.get("/api/whatever");

      const [, init] = fetchMock.mock.calls[0];
      expect((init.headers as Record<string, string>).Authorization).toBeUndefined();
    });

    it("JSON-encodes POST bodies with a Content-Type header", async () => {
      fetchMock.mockResolvedValueOnce(jsonResponse({ id: "1" }));

      await api.post("/api/items", { name: "Milk" });

      const [, init] = fetchMock.mock.calls[0];
      expect(init.method).toBe("POST");
      expect(init.body).toBe(JSON.stringify({ name: "Milk" }));
      expect((init.headers as Record<string, string>)["Content-Type"]).toBe("application/json");
    });

    it("does not force a JSON Content-Type for FormData bodies (multipart upload)", async () => {
      fetchMock.mockResolvedValueOnce(jsonResponse({ imageUrl: "x" }));
      const form = new FormData();
      form.append("file", new Blob(["x"]));

      await api.postForm("/api/items/1/image", form);

      const [, init] = fetchMock.mock.calls[0];
      expect((init.headers as Record<string, string>)["Content-Type"]).toBeUndefined();
      expect(init.body).toBe(form);
    });
  });

  describe("response handling", () => {
    it("returns undefined for a 204 No Content response", async () => {
      fetchMock.mockResolvedValueOnce(jsonResponse(undefined, 204));
      await expect(api.delete("/api/items/1")).resolves.toBeUndefined();
    });

    it("resolves with parsed JSON on 2xx", async () => {
      fetchMock.mockResolvedValueOnce(jsonResponse({ id: "42" }));
      await expect(api.get<{ id: string }>("/api/items/42")).resolves.toEqual({ id: "42" });
    });

    it("throws ApiError with the server's message on non-2xx", async () => {
      fetchMock.mockResolvedValueOnce(jsonResponse({ error: "Not found" }, 404));

      let caught: unknown;
      try {
        await api.get("/api/items/missing");
      } catch (e) {
        caught = e;
      }

      expect(caught).toBeInstanceOf(ApiError);
      expect(caught).toMatchObject({ status: 404, message: "Not found" });
    });

    it("falls back to a generic message when the error body isn't JSON", async () => {
      const res = {
        ok: false,
        status: 500,
        json: async () => {
          throw new Error("not json");
        },
      } as unknown as Response;
      fetchMock.mockResolvedValueOnce(res);

      await expect(api.get("/api/whatever")).rejects.toMatchObject({
        status: 500,
        message: "HTTP 500",
      });
    });
  });

  describe("401 -> refresh -> retry", () => {
    it("retries once with a new token after a successful silent refresh", async () => {
      setToken("stale-token");
      fetchMock
        .mockResolvedValueOnce(jsonResponse({ error: "unauthorized" }, 401)) // original request
        .mockResolvedValueOnce(jsonResponse({ accessToken: "fresh-token" })) // /api/auth/refresh
        .mockResolvedValueOnce(jsonResponse({ id: "1" })); // retried request

      const result = await api.get<{ id: string }>("/api/items/1");

      expect(result).toEqual({ id: "1" });
      expect(getToken()).toBe("fresh-token");
      expect(fetchMock).toHaveBeenCalledTimes(3);
      expect(fetchMock.mock.calls[1][0]).toContain("/api/auth/refresh");
    });

    it("clears the token and throws when refresh fails", async () => {
      setToken("stale-token");
      fetchMock
        .mockResolvedValueOnce(jsonResponse({ error: "unauthorized" }, 401))
        .mockResolvedValueOnce(jsonResponse({ error: "no refresh cookie" }, 401));

      await expect(api.get("/api/items/1")).rejects.toMatchObject({ status: 401 });
      expect(getToken()).toBeNull();
    });

    it("does not attempt refresh for the anonymous /api/auth/login endpoint", async () => {
      fetchMock.mockResolvedValueOnce(jsonResponse({ error: "bad credentials" }, 401));

      await expect(api.post("/api/auth/login", { email: "a", password: "b" })).rejects.toMatchObject({
        status: 401,
      });
      // Only the login call itself — no refresh attempt.
      expect(fetchMock).toHaveBeenCalledTimes(1);
    });

    it("does not attempt refresh once markLoggedOut() has been called (racing logout)", async () => {
      setToken("stale-token");
      markLoggedOut();
      fetchMock.mockResolvedValueOnce(jsonResponse({ error: "unauthorized" }, 401));

      await expect(api.get("/api/notifications")).rejects.toMatchObject({ status: 401 });
      expect(fetchMock).toHaveBeenCalledTimes(1); // no refresh call
    });

    it("a fresh setToken() (new login) clears the loggedOut flag so future 401s refresh again", async () => {
      markLoggedOut();
      setToken("new-session-token");
      fetchMock
        .mockResolvedValueOnce(jsonResponse({ error: "unauthorized" }, 401))
        .mockResolvedValueOnce(jsonResponse({ accessToken: "fresher-token" }))
        .mockResolvedValueOnce(jsonResponse({ ok: true }));

      await expect(api.get("/api/notifications")).resolves.toEqual({ ok: true });
      expect(fetchMock).toHaveBeenCalledTimes(3);
    });
  });
});
