import { describe, expect, it } from "vitest";
import { ApiError, isServiceUnavailable } from "./api";

describe("isServiceUnavailable", () => {
  it("is true only for a 503 carrying code service_unavailable", () => {
    const err = new ApiError(503, "down", { error: "x", code: "service_unavailable", service: "ai" });
    expect(isServiceUnavailable(err)).toBe(true);
  });

  it("is false for other statuses, other 503 bodies and non-ApiError values", () => {
    expect(isServiceUnavailable(new ApiError(500, "boom", { code: "service_unavailable" }))).toBe(false);
    expect(isServiceUnavailable(new ApiError(503, "down"))).toBe(false);
    expect(isServiceUnavailable(new ApiError(503, "down", { code: "other" }))).toBe(false);
    expect(isServiceUnavailable(new Error("x"))).toBe(false);
    expect(isServiceUnavailable(null)).toBe(false);
  });
});
