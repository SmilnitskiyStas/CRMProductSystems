import { describe, expect, it } from "vitest";
import { isIdle } from "./useIdleLogout";

describe("isIdle", () => {
  const T = 5 * 60 * 1000;
  it("is false while activity is recent", () => {
    expect(isIdle(1_000_000 + T - 1, 1_000_000, T)).toBe(false);
  });
  it("is true at and after the threshold", () => {
    expect(isIdle(1_000_000 + T, 1_000_000, T)).toBe(true);
    expect(isIdle(1_000_000 + T * 2, 1_000_000, T)).toBe(true);
  });
});
