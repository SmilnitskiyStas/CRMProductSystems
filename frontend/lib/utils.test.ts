import { describe, expect, it } from "vitest";
import { cn } from "./utils";

// Trivial smoke test — proves `npm test` (vitest) works end-to-end
// (audit Block 0). Real coverage is a separate, later task (audit Block 13).
describe("cn", () => {
  it("joins truthy class names and drops falsy ones", () => {
    expect(cn("a", false && "b", "c")).toBe("a c");
  });

  it("resolves conflicting Tailwind classes via tailwind-merge (last wins)", () => {
    expect(cn("px-2", "px-4")).toBe("px-4");
  });
});
