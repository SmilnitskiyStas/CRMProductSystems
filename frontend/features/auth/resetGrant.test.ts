import { describe, expect, it } from "vitest";
import { readResetGrant } from "./resetGrant";
const token = "A".repeat(43);
describe("password reset fragment", () => {
  it("reads proof from a fragment without requiring a query string", () => {
    expect(readResetGrant(`#email=person%40example.com&token=${token}`)).toEqual({ email: "person@example.com", token });
  });
  it.each(["", "#token=short", `#email=person&token=${token}`,
    `#email=person%40example.com&token=${token}&token=${token}`, `#email=person%40example.com&token=${"B".repeat(42)}+`])
  ("rejects missing, ambiguous, or malformed proof: %s", (fragment) => {
    expect(readResetGrant(fragment)).toBeNull();
  });
});
